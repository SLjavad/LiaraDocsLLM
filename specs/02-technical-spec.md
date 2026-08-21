# Technical Spec — Liara Docs Assistant

Implementation-level contract for what `01-architecture.md` decided (which also
holds the functional/non-functional requirements — FR/NFR tables in §8–9):
DB schema,
Redis key scheme, router contract, API request/response shapes, SSE protocol,
agent tool signatures, taxonomy, and config. OpenCode should treat this as the
source of truth for exact shapes; `01-architecture.md` for the *why*.

## 0. Pinned tooling & versions

No "use the latest" anywhere below — versions are pinned explicitly so there's
nothing to look up or guess. If a pinned package has a newer version available
by the time this is implemented, stay on what's pinned here unless the tech
lead updates this section; don't self-upgrade mid-build.

**Backend**
| Tool/package | Version | Notes |
|---|---|---|
| .NET SDK | **10.0** (LTS) | GA Nov 2025, supported to Nov 2028 |
| ASP.NET Core Web API | ships with .NET 10 SDK | |
| `Microsoft.Agents.AI` (Microsoft Agent Framework) | **1.18.0** | GA'd April 2026; `dotnet add package Microsoft.Agents.AI` |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | **9.0.1+** | |
| EF Core | **10.x** (paired with .NET 10) | |
| `Pgvector` | **0.3.2+** | C# `Vector` type for pgvector columns |
| `Pgvector.EntityFrameworkCore` | **0.3.0** | EF Core integration for `Pgvector` — see §1a for how it's used |
| `AngleSharp` | **1.5.2** | HTML parsing for the in-process crawler (§4a) — replaces the earlier Node/cheerio reuse plan; targets net10.0 directly |
| `Serilog.AspNetCore` | latest stable at build time | structured logging, NFR3 |
| `StackExchange.Redis` | latest stable at build time | Redis client |
| `Microsoft.Extensions.Http.Resilience` | latest stable at build time | timeout/retry, NFR4 |
| Test framework | **xUnit** | Phase 6 unit tests |

**Frontend**
| Tool/package | Version | Notes |
|---|---|---|
| Next.js | **16.2.7**, App Router | Pages Router is legacy — do not use it; Turbopack is the default bundler, leave it on |
| React | **19.2** | ships with Next.js 16.2.7 |
| TypeScript | latest 5.x stable at build time | |
| Tailwind CSS | **v4** | config lives in `globals.css` via `@theme`, not `tailwind.config.js` — v3-style config is wrong for this stack |
| shadcn/ui | via `npx shadcn@latest init` | Tailwind v4-native; CLI has **built-in RTL support** (converts physical→logical CSS classes) — relevant since Persian content needs RTL layout, see `05-frontend-plan.md` |
| `streamdown` | latest stable at build time | drop-in `react-markdown` replacement built for AI streaming — handles incomplete/unterminated markdown mid-stream, GFM, Shiki code blocks. Do **not** use plain `react-markdown` for the streamed chat view (it wasn't built for partial input and can flicker/misrender mid-stream); it's fine for the fully-formed Practice Mode explanation text if a lighter dependency is preferred there. |
| Package manager | npm | no strong reason to deviate |

No Vercel AI SDK (`ai` package) dependency — our SSE event schema (`meta`/
`sources` separate from token deltas) doesn't match its `useChat` data-stream
protocol, and pulling it in just for that would fight the framework rather
than use it. See `05-frontend-plan.md` for the custom SSE client instead.

## 1. Data model (PostgreSQL + pgvector + pg_trgm)

```sql
create extension if not exists vector;
create extension if not exists pg_trgm;

create table doc_chunks (
  id            uuid primary key default gen_random_uuid(),
  url           text not null,
  anchor        text,                 -- nullable: page-level chunk has no anchor
  title         text not null,
  category      text not null,        -- see §2 taxonomy ids
  platform      text,                 -- nextjs|django|nodejs|... nullable
  body          text not null,
  token_count   int not null,
  embedding     vector(EMBED_DIM) not null,  -- EMBED_DIM fixed at migration time, see §7
  content_hash  text not null,        -- sha256(body); skip re-embedding unchanged chunks on re-ingest
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now(),
  unique (url, anchor)
);
create index doc_chunks_embedding_idx on doc_chunks using hnsw (embedding vector_cosine_ops);
create index doc_chunks_title_trgm_idx on doc_chunks using gin (title gin_trgm_ops);
create index doc_chunks_category_idx on doc_chunks (category);

create table sessions (
  id                     uuid primary key,       -- client-generated, see §6 session identity
  locale                 text,                    -- 'fa' | 'en', last detected
  profile                jsonb not null default '{}', -- { platform?, framework?, service? }
  pending_clarification  jsonb,                   -- { originalQuery, roundsAsked } | null, see §5
  created_at             timestamptz not null default now(),
  last_active_at         timestamptz not null default now()
);

create table messages (
  id            uuid primary key default gen_random_uuid(),
  session_id    uuid not null references sessions(id),
  role          text not null,          -- 'user' | 'assistant'
  content       text not null,
  sources       jsonb,                  -- [{title,url,anchor,score}] | null, assistant only
  router_scope  text,                   -- 'trivial' | 'out_of_scope' | 'in_scope', assistant only
  tokens_in     int,
  tokens_out    int,
  created_at    timestamptz not null default now()
);
create index messages_session_idx on messages (session_id, created_at);

create table message_feedback (
  id          uuid primary key default gen_random_uuid(),
  message_id  uuid not null references messages(id),
  vote        text not null,            -- 'up' | 'down'
  created_at  timestamptz not null default now()
);

create table doc_gap_events (           -- NFR14, see 01-architecture.md §13
  id             uuid primary key default gen_random_uuid(),
  query          text not null,
  best_score     real,
  category_guess text,
  mode           text not null,         -- 'search' | 'chat' | 'practice'
  session_id     uuid references sessions(id),
  created_at     timestamptz not null default now()
);

create table practice_exams (            -- FR14/FR15, see 01-architecture.md §7
  id                 uuid primary key default gen_random_uuid(),
  session_id         uuid not null references sessions(id),
  topic              text not null,       -- user's original description
  status             text not null,       -- 'planned' | 'in_progress' | 'completed'
  current_step_index int not null default 0,
  steps              jsonb not null,      -- [{ index, question, options: [3], correctIndex, explanation, source:{title,url,anchor} }]
  created_at         timestamptz not null default now(),
  completed_at       timestamptz
);

create table practice_exam_answers (
  id             uuid primary key default gen_random_uuid(),
  exam_id        uuid not null references practice_exams(id),
  step_index     int not null,
  selected_index int not null,
  is_correct     boolean not null,
  answered_at    timestamptz not null default now(),
  unique (exam_id, step_index)
);
```

Idempotent re-ingestion: on re-run, compute `content_hash` per chunk; if a row
exists for `(url, anchor)` with the same hash, skip re-embedding (cost saving);
if the hash differs, re-embed and update; if `(url, anchor)` no longer appears
in the crawl output, leave it (no deletion pass in MVP — acceptable staleness
risk, not worth the complexity here).

### 1a. How this schema gets created (EF Core + raw SQL migration)

Don't try to express the pgvector-specific pieces (extensions, the `vector`
column type, the HNSW index, the `pg_trgm` GIN index) through EF Core's
fluent API by guesswork — several of these don't have a stable, well-known
fluent-API surface, and getting it subtly wrong is a bad failure mode (silent
wrong index type, no cosine ops). Use raw SQL for those specific pieces
instead, inside a normal EF Core migration:

1. Define entity classes for all six tables normally (POCO classes + a
   `DbContext`). For `DocChunk.Embedding`, use the `Pgvector.Vector` type as
   the property type (from the `Pgvector` package) and configure
   `.HasColumnType($"vector({EMBED_DIM})")` in `OnModelCreating` — this part
   *is* well-supported by `Pgvector.EntityFrameworkCore`, use it normally.
2. Run `dotnet ef migrations add InitialCreate` to generate the migration
   from those entities.
3. Edit the generated migration's `Up()` method: before the generated
   `CreateTable` calls, add `migrationBuilder.Sql("create extension if not
   exists vector;")` and the same for `pg_trgm`. After the generated table
   creation, add `migrationBuilder.Sql(...)` with the exact `create index ...
   using hnsw (...)` and `... using gin (...)` statements from §1 above,
   verbatim.
4. Everything else (columns, foreign keys, uniques, defaults) comes from the
   normal EF Core-generated migration — only the four pgvector/pg_trgm-specific
   statements are raw SQL.
- For querying, `Pgvector.EntityFrameworkCore` supports LINQ methods like
  `.OrderBy(c => c.Embedding.CosineDistance(queryVector))` — use that for the
  retrieval service rather than raw SQL, it's the well-supported part of the
  package.
- Steps 1–3 above are a one-time **dev-time** action (write the migration
  file, commit it). **Applying** the migration at runtime is automatic —
  `Database.MigrateAsync()` runs at API startup (§2a) — no one ever runs
  `dotnet ef database update` by hand, locally or on Liara.

## 2. Documentation taxonomy

Derived from `indexer/src/utils/getUrl.js`. Used by `list_categories`, by
ingestion (category = URL path segment), and by the `category` filter on
`search_docs` / `/api/search`.

| id | URL path segment | label (fa) | label (en) |
|---|---|---|---|
| `paas` | `/paas` | پلتفرم به‌عنوان سرویس | PaaS |
| `ai` | `/ai` | هوش مصنوعی | AI |
| `iaas` | `/iaas` | زیرساخت به‌عنوان سرویس | IaaS |
| `dbaas` | `/dbaas` | پایگاه‌داده به‌عنوان سرویس | DBaaS |
| `mail` | `/email-server` | سرور ایمیل | Email Server |
| `dns` | `/dns-management-system` | مدیریت DNS | DNS Management |
| `object_storage` | `/object-storage` | فضای ذخیره‌سازی ابری | Object Storage |
| `one_click_app` | `/one-click-apps` | اپلیکیشن‌های یک‌کلیکی | One-Click Apps |
| `references` | `/references` | مرجع API | References |
| `overview` | `/overview` | معرفی کلی | Overview |

`/mirrors` and `/tv` pages exist on the site but are outside the sitemap
parser's crawl targets — excluded from ingestion scope, not part of the
taxonomy.

## 2a. Ingestion background service

Implements `01-architecture.md` §4 (auto-triggered, single-process, no human
trigger, seed-first with live-crawl fallback). Concrete mechanics:

- **Registration**: `IngestionBackgroundService : BackgroundService`,
  registered with `builder.Services.AddHostedService<IngestionBackgroundService>()`
  in the API's `Program.cs`. Runs once in `ExecuteAsync` at startup, not on a
  timer.
- **Startup sequence**:
  1. `await db.Database.MigrateAsync()` — apply any pending EF Core
     migrations automatically, no manual `dotnet ef database update`.
  2. `await db.DocChunks.CountAsync()` — if `> 0`, return immediately
     (already populated on a prior boot, skip everything below).
  3. If `0` and `SEED_DOWNLOAD_URL` is set: download it, `pg_restore` into
     the database, log success/failure. On success, done — skip the crawl
     below entirely.
  4. If `0` and no seed configured, or the seed download/restore failed:
     run the live crawl pipeline below.
- **Enumerate pages**: fetch `DOCS_SITEMAP_URL`
  (default `https://docs.liara.ir/sitemap.xml`; point at
  `http://localhost:3001/sitemap.xml` when generating the seed locally per
  `01-architecture.md` §4a), filter URLs by the path prefixes in §2's
  taxonomy table (same filtering Liara's own indexer does in `getUrl.js`).
- **Crawl** (AngleSharp, §0): for each URL, fetch the HTML and parse with
  `AngleSharp.Html.Parser.HtmlParser`. Extract `h1` text as the page title,
  then split into sections by heading elements (`h2`–`h6`) the same way the
  original crawler does: section title = heading text, section body = text
  of siblings up to the next heading, anchor = the heading's (or its
  adjacent element's) `id` attribute if present. Concurrency against the
  **live** site: a bounded worker pool (`SemaphoreSlim(CRAWL_CONCURRENCY)`);
  each worker waits `CRAWL_DELAY_MS` between its own consecutive requests —
  not a single global serial delay (that's what made the original crawler
  82+ minutes). Against **localhost** (seed generation), concurrency/delay
  tuning doesn't matter — no external site to be polite to.
- **Chunk/embed/upsert**: as in §1 — one chunk per section, `content_hash =
  sha256(body)`, embed in batches of **96 texts per request** (the
  documented max for `nemotron-3-embed-1b:free` on OpenRouter — see
  `01-architecture.md` §4a for the resulting request-count math), upsert
  into `doc_chunks` keyed on `(url, anchor)`.
- **Failure handling**: a single page failure (timeout, 404, transient
  error) is logged and skipped — it does not abort the whole run. The
  pipeline should finish with whatever it could reach, consistent with the
  NFR4 resilience philosophy applied elsewhere.
- **Observability**: log run start, which path was taken (seed vs. live
  crawl), periodic progress (every N pages, live-crawl path only), and
  completion (chunk count, duration) via Serilog (NFR3) — this is the only
  visibility into ingestion progress for MVP; no separate status endpoint.

## 3. Redis key scheme

| Key pattern | Purpose | TTL |
|---|---|---|
| `rate:session:{sessionId}:{windowStart}` | NFR1 session rate limit counter | window length |
| `rate:ip:{ip}:{windowStart}` | NFR1 IP rate limit counter | window length |
| `spend:daily:{yyyy-mm-dd}` | NFR8 global daily token/cost counter | 48h |
| `cache:search:{hash(query+category+platform)}` | `/api/search` response cache | 1h |
| `cache:embedding:{hash(text)}` | embedding lookup cache (deterministic per model version) | 7d |

## 4. Router contract (shared scope-gate + query processing)

One cheap-model JSON-mode call, used by both endpoints (NFR7). For `/api/chat`
it only gates scope; the full agent handles triage/multi-hop itself. For
`/api/search` it also decomposes the query, since that endpoint has no agent
loop.

**Input**: `{ message: string, mode: "search" | "chat" | "practice", recentMessages?: string[] }`
(`practice` mode: same scope-classification behavior as `chat` — no query
decomposition, this call is only the scope gate for the topic description
given to `POST /api/practice/start`, see §6a.)
(`recentMessages`: last 2–4 turns, chat mode only, for context-aware scope
classification — e.g. a bare "why?" following an in-scope answer is in-scope).

**Output**:
```json
{
  "scope": "trivial" | "out_of_scope" | "in_scope",
  "reason": "meta_question" | "personal_question" | "general_knowledge" | "jailbreak_attempt" | null,
  "subQueries": ["..."]
}
```
- `subQueries` only populated when `mode == "search"` and `scope == "in_scope"`
  (empty/single-element array when the query doesn't need decomposition).
- Refusal/trivial-response **text is never model-generated** — it's a static,
  localized template selected by `reason` (or by "trivial" for greetings). This
  keeps refusals consistent, on-brand, and immune to being talked around, and
  it's cheaper than generating text. Templates live in backend config/resources,
  keyed by `reason × locale`.
- `in_scope` is **domain-based**: any hosting/deployment/infrastructure/
  programming-technical question qualifies, whether or not it mentions Liara
  by name (e.g. "what does exit code 137 mean" is in-scope on its own — see
  `01-architecture.md` §7 groundedness policy / diagnostic reasoning). Reserve
  `general_knowledge` for genuinely unrelated non-technical topics. The router
  prompt must state this domain boundary explicitly, not rely on keyword/
  mention matching. `recentMessages` is for resolving follow-ups/pronouns, not
  for gating whether a technical question counts as in-scope.

## 5. `/api/chat` orchestration & triage state

Triage round-capping (01-architecture.md §7) is enforced **in code, not by
relying on the LLM to count** — model-tracked counters are unreliable across
turns.

- `sessions.pending_clarification` starts `null`.
- When the agent's response for a turn is a clarifying question, the backend
  sets `pending_clarification = { originalQuery, roundsAsked }`, incrementing
  `roundsAsked` if already set for this thread.
- When the agent's response is an actual answer (not another clarifying
  question), the backend clears `pending_clarification = null`.
- Before letting the agent ask another clarifying question, the backend checks
  `roundsAsked`. At `>= 2` (`MAX_CLARIFYING_ROUNDS`), it forces the agent's next
  turn into "best-effort answer + escalation fallback" mode instead (via an
  injected system note), regardless of what the model would otherwise do, and
  clears `pending_clarification`.

## 6. API endpoints

Session identity: client-generated UUID (`X-Session-Id` header), created on
first frontend load, persisted in `localStorage`. Required on `/api/chat` and
`/api/feedback`; optional on `/api/search` (only used there for rate limiting
and cache namespacing, no conversation state).

### `POST /api/search`

Request:
```json
{ "sessionId": "uuid|null", "query": "string", "category": "string|null", "platform": "string|null" }
```
Response (in-scope):
```json
{
  "scope": "in_scope",
  "subQueries": ["...", "..."],
  "results": [
    { "title": "...", "url": "...", "anchor": "#...", "snippet": "...", "score": 0.81, "category": "paas", "matchedSubQuery": "..." }
  ],
  "tookMs": 120
}
```
Response (out-of-scope/trivial):
```json
{ "scope": "out_of_scope", "reason": "general_knowledge", "message": "<static template>", "results": [] }
```

### `POST /api/chat` (SSE, `text/event-stream`)

Request:
```json
{ "sessionId": "uuid", "message": "string" }
```
Events (each `data:` line is JSON):

| event | payload | when |
|---|---|---|
| `meta` | `{ "kind": "scope_refusal", "reason": "..." }` | router blocked the message |
| `meta` | `{ "kind": "triage", "triageRound": 1 }` | agent is about to ask a clarifying question |
| `meta` | `{ "kind": "answer" }` | agent is about to stream a real answer |
| `meta` | `{ "kind": "escalation" }` | escalation fallback triggered |
| `token` | `{ "delta": "..." }` | streamed answer/question/refusal text, repeated |
| `sources` | `{ "sources": [{title,url,anchor,score}] }` | once, only for `kind: "answer"`, before `done` |
| `done` | `{}` | end of turn |
| `error` | `{ "message": "...", "retryable": true }` | NFR4 failure path, in place of a raw 500 |

### `POST /api/practice/start` — §6a Practice Mode

Plain JSON, not SSE — a quiz is a structured object, not prose to stream.

Request:
```json
{ "sessionId": "uuid", "description": "string" }
```
Response (out-of-scope, via router §4):
```json
{ "status": "out_of_scope", "reason": "general_knowledge", "message": "<static template>" }
```
Response (needs narrowing — reuses triage, same 2-round cap as chat):
```json
{ "status": "needs_clarification", "question": "...", "triageRound": 1 }
```
Response (ready — full plan generated, but only the current step's question/
options are sent; `correctIndex`/`explanation`/`source` for *future* steps are
never sent ahead of time, so they can't be read from the network tab):
```json
{
  "status": "ready",
  "examId": "uuid",
  "topic": "...",
  "stepCount": 5,
  "step": { "index": 0, "question": "...", "options": ["...", "...", "..."] }
}
```

### `POST /api/practice/{examId}/answer`

Request:
```json
{ "stepIndex": 0, "selectedIndex": 1 }
```
Response — grading is a code-level index comparison against the pre-planned
`correctIndex` (NFR16), no LLM call:
```json
{
  "isCorrect": false,
  "correctIndex": 2,
  "explanation": "...",
  "source": { "title": "...", "url": "...", "anchor": "#..." },
  "next": { "index": 1, "question": "...", "options": ["...", "...", "..."] }
}
```
`next` is `null` on the last step — the frontend should then call the summary
endpoint.

### `GET /api/practice/{examId}/summary`

```json
{
  "topic": "...",
  "score": { "correct": 4, "total": 5 },
  "steps": [
    { "index": 0, "question": "...", "selectedIndex": 1, "correctIndex": 2, "isCorrect": false, "explanation": "...", "source": {...} }
  ]
}
```

### `GET /api/sessions/{sessionId}/messages`

Resume a conversation. Response:
```json
{ "messages": [{ "id": "...", "role": "user|assistant", "content": "...", "sources": [...], "createdAt": "..." }] }
```

### `GET /api/categories`

Static taxonomy (§2), for a category filter in the find-in-docs UI:
```json
{ "categories": [{ "id": "paas", "labelFa": "...", "labelEn": "PaaS" }, ...] }
```

### `POST /api/feedback`

```json
{ "messageId": "uuid", "vote": "up" | "down" }
```
→ `{ "ok": true }`. Writes `message_feedback`; a `down` vote also writes a
`doc_gap_events` row (mode `chat`) even if the original retrieval score was
above threshold, since a low-quality answer despite decent retrieval is itself
a signal worth capturing.

### `GET /health`

→ `{ "status": "ok" }`. Liveness only (no downstream dependency checks needed
for MVP).

## 7. Agent tools (Microsoft Agent Framework)

```
search_docs(query: string, category?: string, platform?: string) -> {
  results: [{ title, url, anchor, body, score, category, platform? }]
}
```
Hybrid retrieval: pgvector cosine similarity (primary, top-k default 5) +
`pg_trgm` similarity on `title` as a tie-breaker boost. After every call, if
`max(results.score) < RETRIEVAL_GROUNDEDNESS_THRESHOLD`, the backend writes a
`doc_gap_events` row (`query`, `best_score`, `category_guess` = top result's
category if any, `mode`, `sessionId`) — this is the same signal the agent uses
to trigger its groundedness refusal.

```
list_categories() -> { categories: [{ id, labelFa, labelEn }] }
```
Static taxonomy from §2, used by the agent for disambiguating questions.

## 8. Configuration reference

**Black-box rule (NFR2)**: every row below is read from the environment at
startup, with no in-code fallback. If a required var is missing, the app
throws a configuration error and refuses to start — it never silently runs
with a guessed/default model. Nothing in this table is hardcoded anywhere in
source; the "notes" column is guidance for *your own local, untracked `.env`*,
not a value that appears in any committed file. See §9 for the git-hygiene
mechanics (`.env.example` vs `.env`, `.gitignore`).

| Env var | Purpose | Notes (for your local `.env` — never committed) |
|---|---|---|
| `CHAT_MODEL_BASE_URL` / `CHAT_MODEL_API_KEY` / `CHAT_MODEL_NAME` | main synthesis model (OpenAI-compatible) | current candidate: Liara AI Gateway, e.g. `openai/gpt-4.1-mini` — swap freely, code has no opinion on which |
| `ROUTER_MODEL_NAME` | cheap model for §4 router (can be same provider, smaller/cheaper model) | current candidate: `openai/gpt-4o-mini` |
| `EMBEDDING_BASE_URL` / `EMBEDDING_API_KEY` / `EMBEDDING_MODEL_NAME` | embedding provider — separate from chat (OpenRouter, not the Liara AI Gateway); OpenAI-compatible endpoint | current candidate: `nvidia/nemotron-3-embed-1b:free` via OpenRouter — #1 on RTEB, benchmarked on Persian (resolves the risk in `01-architecture.md` §5/§11), free. Verify free-tier rate limit sustains bulk ingestion (Phase 2) — fallback `openai/text-embedding-3-small` via Liara AI Gateway is a config-only swap. |
| `EMBED_DIM` | vector column dimension, fixed at migration time — changing providers/models with a different dim requires a new migration + full re-ingest | `2048` for `nemotron-3-embed-1b`; `1536` for `text-embedding-3-small` |
| `OPENROUTER_API_KEY` | separate credential needed even for the free-tier embedding model | — |
| `POSTGRES_CONNECTION_STRING` | Postgres/pgvector | — |
| `REDIS_CONNECTION_STRING` | Redis | — |
| `RATE_LIMIT_SESSION_PER_MIN` | NFR1 | `20` |
| `RATE_LIMIT_IP_PER_MIN` | NFR1 | `60` |
| `DAILY_SPEND_BUDGET_TOKENS` | NFR8 hard stop | tune to demo budget |
| `RETRIEVAL_TOP_K` | §7 | `5` |
| `RETRIEVAL_GROUNDEDNESS_THRESHOLD` | §7, agent groundedness policy | `0.5` |
| `MAX_CLARIFYING_ROUNDS` | §5 (also reused by Practice Mode topic-scoping) | `2` |
| `PRACTICE_MIN_STEPS` / `PRACTICE_MAX_STEPS` | Practice Mode quiz length | `3` / `6` |
| `MAX_INPUT_CHARS` | NFR9 | `2000` |
| `CORS_ALLOWED_ORIGIN` | NFR12 | frontend URL |
| `SUPPORT_CHANNEL_URL` | escalation fallback (01-architecture.md §7) | Liara support link |
| `SEED_DOWNLOAD_URL` | §2a — pre-built `doc_chunks` dump (GitHub Release asset), tried before falling back to a live crawl | unset locally unless testing the seed path; set in prod once generated |
| `DOCS_SITEMAP_URL` | §2a ingestion (live-crawl fallback, or point at `localhost:3001` when generating a seed) | `https://docs.liara.ir/sitemap.xml` |
| `CRAWL_CONCURRENCY` | §2a — bounded worker pool size (live-crawl fallback only) | `5` |
| `CRAWL_DELAY_MS` | §2a — per-worker delay between its own requests (live-crawl fallback only) | `300` |

## 9. Git hygiene & config loading

- **`.env.example`** is committed: lists every var name above with an **empty
  or generic placeholder value** (e.g. `CHAT_MODEL_NAME=`), never a real
  model id, key, or URL. It's the template a new environment copies to `.env`
  and fills in locally.
- **`.env`** (and any `appsettings.*.json` that could hold real values) is
  **gitignored from the first commit** — this must be in place before the
  first push to the repo you already have ready, not added later as a
  follow-up.
- **Local**: backend reads `.env` (via standard .NET config providers/
  `dotnet-env` or docker-compose `env_file`); frontend reads `.env.local`
  (Next.js convention, also gitignored).
- **Liara**: real values are set through Liara's own environment-variable
  configuration for each app — never through a file in the repo at all.
- **Pre-push check**: before the first push, grep the working tree for
  anything that looks like a live API key or a real base URL outside
  `.env`/`.env.local` — this is a five-second check that prevents an
  unrecoverable leak (rotating a key is easy; scrubbing git history after a
  public push is not).

## 10. Open implementation notes

- `EMBED_DIM` must be locked in before the first ingestion run; pgvector
  columns are fixed-dimension, so a provider/model change means a migration +
  full re-embed, not a config tweak.
- Refusal/trivial templates (§4) need both `fa` and `en` variants per `reason`
  code — write these once, review for tone before demo.
- `content_hash` dedup (§1) assumes ingestion is re-run wholesale each time;
  no incremental/partial crawl in MVP scope.
