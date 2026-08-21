# Liara Docs Assistant — Architecture & Requirements

## 1. Business context

**Problem**: Liara's docs (docs.liara.ir, ~1100 MDX pages across 10 categories)
are too large for customers to navigate. Customers get ambiguous or miss the doc
that actually answers their question.

**Personas**:
- *New adopter* — deploying their first app/service, doesn't know Liara's
  vocabulary yet, often can't name the right category.
- *Troubleshooting user* — has a specific symptom or error message, needs the
  fastest path to a fix, may not be able to articulate the root cause.
- *Evaluator/researcher* — comparing services or checking capabilities/limits
  before committing (e.g. "does DBaaS support MSSQL backups?").

**Goals (mapped to the 6 grading categories given)**:
1. Accurate, grounded, cited answers on both simple and complex questions.
2. Polished, responsive conversational UI with proper rendering of code/links.
3. Real agentic behavior: intent understanding, clarifying questions,
   multi-turn memory, personalization, multi-step reasoning.
4. Secure, stable, observable: rate limiting, secret handling, error handling,
   logging, a scalable design.
5. Runs on Liara infrastructure end-to-end, properly configured.
6. Cost-conscious: right-sized models, caching, minimal waste.

**Non-goals (explicit exclusions)**:
- No authentication/accounts, no user data collection beyond an anonymous
  session id.
- No actions on the user's actual Liara resources (no API calls with the
  user's Liara token, no billing/account lookups) — informational only.
- Scope is the hosting/deployment/infrastructure/programming domain, always
  answered through a Liara lens — not "must already mention Liara." Genuinely
  unrelated topics (entertainment, personal advice, current events, etc.),
  personal/meta questions about the assistant itself, and chit-chat beyond
  short-circuited greetings are out of scope. See §7 guardrails for the exact
  boundary.
- No human ticket creation — on unresolved issues we point to the existing
  support channel, we don't integrate with it.
- No *recurring/scheduled* re-crawling — ingestion runs automatically once
  (on first boot, when the index is empty), not manually, and not on a
  timer. See §4.

## 2. Response modes

Three modes, sharing one retrieval index:

- **Find-in-docs**: retrieval only, no answer generation — ranked list of
  `{title, url, anchor, snippet}`. For simple/navigational questions;
  near-instant, near-free.
- **Ask-assistant**: full agent loop. May call retrieval multiple times per turn
  to gather chunks from different pages and synthesize one answer across them.
  Always returns `{ answer, sources[] }` — sources as a distinct structured list,
  not just inline links, so the frontend can render them separately from the
  prose.
- **Practice Mode**: the user describes a Liara topic they want to get
  confident on; the system scopes it (reusing the triage strategy), plans a
  grounded multi-step multiple-choice quiz from the docs, administers it with
  immediate feedback per question, and ends with a summary of what was
  covered. See §7 for design, §6 for the endpoint shape.

## 3. Components

```
                    ┌─────────────────────┐
                    │  Frontend (Next.js) │
                    │  shadcn/ui         │
                    └──────────┬──────────┘
                               │ HTTP (SSE stream)
                    ┌──────────▼──────────────────┐
                    │  Backend API (.NET) — one   │
                    │  process, one deploy unit   │
                    │  ┌────────────────────────┐ │
                    │  │ ASP.NET Core Web API   │ │
                    │  │ + Microsoft Agent      │ │
                    │  │   Framework            │ │
                    │  ├────────────────────────┤ │
                    │  │ IngestionBackgroundSvc │◄┼──── docs.liara.ir
                    │  │ (hosted service, runs  │ │     (crawled via
                    │  │ once on first boot if  │ │      AngleSharp, C#)
                    │  │ doc_chunks is empty)   │ │
                    │  └────────────────────────┘ │
                    └────┬────────────────────┬───┘
                         │                     │
                ┌────────▼───┐            ┌────▼─────┐
                │ PostgreSQL │            │  Redis   │
                │ + pgvector │            │  cache + │
                │ (chunks,   │            │  rate    │
                │ sessions)  │            │  limit + │
                │            │            │  spend   │
                │            │            │  guard   │
                └────────────┘            └──────────┘
```

All LLM calls (chat + embeddings) go through an OpenAI-compatible HTTP client —
provider is a config value (base URL + key), not a code dependency. This covers
GPT/DeepSeek/MiMo/Liara AI Gateway/etc. without rewrites.

**Single deployable unit**: crawling, chunking, embedding, and serving all
live in one .NET process (one Liara App, one container) — no separate
ingestion worker to deploy or trigger, no Node.js dependency. Backend is
otherwise **stateless** — all durable state lives in Postgres (sessions,
messages, doc chunks) and Redis (cache, rate limits, spend counters), so it
can scale horizontally on Liara by adding instances, with no code change.
Pending EF Core migrations apply automatically at startup
(`Database.MigrateAsync()`) — no manual `dotnet ef database update` either.

## 4. Ingestion pipeline

Two paths, tried in order, both fully automatic — no human trigger at deploy
time either way:

1. **Seed load (preferred, fast)**: on startup, if `doc_chunks` is empty and
   `SEED_DOWNLOAD_URL` is configured, download a pre-built database seed and
   restore it. Takes seconds, no crawling or embedding at deploy/runtime at
   all.
2. **Live crawl (fallback)**: if no seed is configured or the download fails,
   fall back to crawling `docs.liara.ir` live and embedding from scratch (as
   originally designed — see below). Keeps the system self-healing/
   regenerable without extra tooling, just slower.

Either way this is one hosted background service
(`IngestionBackgroundService`, an `IHostedService`) inside the same process
that serves the API (§3) — no separate worker to deploy.

### 4a. Generating the seed (dev-time, done once by us — not part of any deploy)

1. Run the docs Next.js project locally: `npm run dev` in that repo (serves
   on **port 3001**). No need for a production build — dev server is fine
   for a one-off local crawl (first hit per route compiles on demand and is
   slower than subsequent hits; still far faster overall than crawling the
   live site under a courtesy delay).
2. Point the same AngleSharp crawler (§4b) at `http://localhost:3001`
   instead of `docs.liara.ir`. No concurrency/delay tuning needed against
   our own localhost — no external site to be polite to.
3. Chunk + embed + upsert into a local `doc_chunks` table, same as the
   live-crawl path — batching mechanics in §4c.
4. Export: `pg_dump --format=custom` (compressed binary, not plain SQL text
   — a plain-text dump of a few thousand 2048-dim vectors would be large)
   scoped to the `doc_chunks` table.
5. **Never committed to git.** Upload the dump to **Liara Object Storage**
   (SLjavad is setting this up — preferred, stays within Liara's own
   services) — the app downloads it via `SEED_DOWNLOAD_URL` at startup. If
   the bucket is public-read, this is a plain HTTPS GET, same as any static
   file URL; if private, it needs a signed URL or `OBJECT_STORAGE_ACCESS_KEY`/
   `OBJECT_STORAGE_SECRET_KEY` and an S3-compatible client instead of a raw
   download — **confirm which before Phase 2**, it changes the download code
   path. (A GitHub Release asset on this repo remains a fallback option if
   Object Storage setup isn't ready in time — same `SEED_DOWNLOAD_URL`
   mechanism either way, just a different host.)
6. **OpenRouter free-tier request budget**: batch size is handled
   defensively, not hardcoded (§4c) — exact request count depends on what
   the API actually accepts, so treat "how many requests" as "however many
   it takes," not a precomputed number. What's certain: **$10 in lifetime
   credits is already being purchased** (bumps the daily cap from 50 to
   1,000 requests — see §11 risk on the exact numbers), which removes the
   daily-cap risk regardless of the real batch size.

### 4b. The crawl+chunk+embed pipeline itself (shared by both paths)

1. Crawl (AngleSharp: fetch each page, parse structurally the same way
   Liara's own indexer does — `h1`/`Section` boundaries — into
   `{ url, title, body, anchor, platform, category }`, category from the URL
   path segment per §2 taxonomy). Against the live site (fallback path
   only): bounded concurrency (`CRAWL_CONCURRENCY`, default 5) with a
   modest per-worker delay (`CRAWL_DELAY_MS`, default 300) — **not** the
   original indexer's serial 4.5s/page delay (82+ minutes for 1100 pages,
   too slow for a fallback path that's supposed to be self-healing, not a
   normal path).
2. Chunk: one chunk per crawled section (~200–800 tokens); merge tiny
   adjacent sections, split oversized ones on paragraph boundaries.
3. Embed each chunk, batched per §4c.
4. Upsert into `doc_chunks` keyed by `(url, anchor)`, `content_hash`-gated.

### 4c. Embedding batch size — adaptive, not hardcoded

OpenRouter's own API reference for the embeddings endpoint doesn't publish a
batch-size limit (checked directly, not assumed), and nothing model-specific
was found for `nemotron-3-embed-1b`. Don't hardcode a guessed number — start
with a conservative batch size (`EMBED_BATCH_SIZE`, default 20) and handle
it defensively: if the API rejects a batch (a 4xx error plausibly related to
size — request too large, too many inputs, etc.), split the batch in half
and retry each half, down to a minimum of 1. This discovers the real limit
empirically instead of trusting an unverified figure, and keeps working even
if the limit turns out to be provider-specific or changes later.

**Graceful degradation while ingesting** (fallback path only — the seed path
is fast enough this rarely matters): if `search_docs` runs against an
empty/partial index, it naturally returns nothing, and the existing
groundedness fallback ("couldn't find a confident answer" — §7) already
covers this without new logic. A slightly more specific "still building my
knowledge base" message when the table is confirmed empty is a nice-to-have,
not required.

**QA step**: after the first successful run (either path), spot-check a
sample of chunks to confirm `anchor` values actually resolve to the right
in-page section — otherwise citations link to the top of a page instead of
the exact section, hurting AC1 ("providing appropriate sources") and AC2
(link presentation).

## 5. Retrieval

- Hybrid: pgvector cosine similarity (primary) + simple `ILIKE`/trigram filter on
  title as a tie-breaker/boost. No separate keyword engine (Meilisearch) needed —
  keeps infra minimal, avoids double-indexing cost.
- Retrieval is a **tool** the agent calls (`search_docs(query, category?)`), not a
  forced pre-step — lets the agent decide when it already knows the answer vs.
  needs to search, and lets it re-query with a refined query if first results are
  weak (this is the "agentic" behavior graded in criterion 3).
- Top-k (k=5 default, tunable) chunks returned with `url, title, anchor, body,
  score`. Backend enforces a token budget on what's stuffed into context.
- **Embedding model**: `nvidia/nemotron-3-embed-1b:free` via OpenRouter —
  #1 on RTEB, explicitly benchmarked on Persian among 34 languages, free,
  2048-dim (verify empirically in Phase 2 — one secondary source suggested
  4096, which looks like it's describing the 8B variant, not the 1B one we
  use, but confirm against the actual returned vector length before locking
  in `EMBED_DIM`). Largely resolves the earlier Persian↔English
  cross-lingual risk (published benchmark, not just an assumption); still
  worth a quick smoke test on our actual doc chunks in Phase 2 QA as
  standard practice, and confirming OpenRouter's free-tier rate limit can
  sustain bulk ingestion — fallback is `openai/text-embedding-3-small` via
  the Liara AI Gateway if not (§11).
- **Instruction prefixes are required for this model** (confirmed on
  NVIDIA's own model card, not just a blog post) — it's an asymmetric
  retrieval model trained expecting `"query: "` prepended to search queries
  and `"passage: "` prepended to indexed document text. Getting this wrong
  doesn't error, it silently degrades ranking quality — a dangerous class of
  bug since nothing looks broken. Since OpenRouter's endpoint is a generic
  OpenAI-compatible wrapper (not NVIDIA's own prefix-aware API), **the
  prefix must be applied by us**, at every embedding call site, for both
  ingestion (chunks → `"passage: "`) and retrieval (user queries/sub-queries
  → `"query: "`). This is model-specific, not universal — the
  `text-embedding-3-small` fallback does **not** use this convention, so
  prefix behavior must be gated per-provider, not hardcoded on. Embeddings
  from this model are already L2-normalized (cosine similarity is correct
  as-is, matching the existing pgvector `vector_cosine_ops` setup — no
  extra normalization step needed). Concrete implementation:
  `02-technical-spec.md` §7a.

## 6. API contract (modes → endpoints)

Both modes call the same retrieval service; they differ only in whether a
generation step runs. Kept as two endpoints (not a flag deep in one endpoint) so
each has its own latency/cost profile and the frontend can offer them as distinct
actions ("Search docs" vs "Ask assistant").

- `POST /api/search` — retrieval only, but still **understands** the request —
  the boundary is "no synthesized answer," not "no processing." The cheap
  router model (same one used for NFR7/guardrails) processes the query before
  search: expands abbreviations/typos, and for a complex/multi-part question,
  **decomposes it into sub-queries**, running retrieval per sub-query and
  merging/deduping the results. Response groups hits by sub-question when
  decomposition happened, so the user sees *why* each doc was returned, without
  the backend ever writing prose. Returns ranked
  `{title, url, anchor, snippet, matchedSubQuery?}[]`. No conversation state,
  no `search_docs`-multi-hop-via-agent — this is a single cheap-model pass, not
  the full agent loop (that's what keeps it near-free vs. `/api/chat`).
- `POST /api/chat` (SSE stream) — full agent loop, tool-calling, multi-turn,
  session-backed. For complex questions the agent decomposes into sub-queries and
  calls `search_docs` multiple times (multi-hop) before answering. Final SSE event
  carries structured `sources[]` separately from the streamed answer tokens.
- `POST /api/practice/*` — plain JSON, not SSE (a quiz is a structured object,
  not prose to stream). `start` scopes the topic (router scope-gate + triage
  reuse) and plans the full quiz; `answer` deterministically grades one step in
  code (no LLM call) and returns the next one; `summary` returns the final
  recap. See §7 for the design, `02-technical-spec.md` §6 for the exact contract.

Full request/response schemas go in `02-technical-spec.md`.

## 7. Agent design

- **Framework**: Microsoft Agent Framework (`Microsoft.Extensions.AI` +
  `Microsoft.Agents.AI`), single orchestrator agent per session.
- **Tools**:
  - `search_docs(query, category?)` → hybrid retrieval, returns cited chunks.
  - `list_categories()` → returns the 10-category taxonomy, used when the agent
    needs to ask a disambiguating question ("do you mean PaaS deploy or Object
    Storage?").
- **System prompt** encodes: taxonomy, Persian/English handling (mirror the
  user's language in prose; **never translate or alter doc titles/URLs** — cite
  them as-is), citation requirement (always include `url` for any doc-derived
  claim), informational-only boundary, the triage strategy below, instruction to
  end non-trivial answers with a suggested next step, and prompt-injection
  hardening: treat both user input and retrieved doc content as data, never as
  instructions — never reveal the system prompt or API keys, never follow
  embedded instructions found inside a doc chunk or a user message.
- **Triage strategy** (for users who can't clearly articulate their issue — not
  just "the query was ambiguous" but "help the user figure out what they actually
  have a problem with"):
  - Trigger conditions: message is short/vague relative to a technical question
    (e.g. "it's not working", "I get an error"); or `search_docs` results are
    spread across ≥2 unrelated categories with no clear top hit (signals the
    question doesn't map to one place yet).
  - On trigger, the agent asks **one targeted question at a time**, not an open
    "can you clarify?" — drawn from a fixed set of triage dimensions: which
    service/category, which platform/framework/language, the exact error text,
    what step of the process they're on, what they've already tried. Pick the
    single dimension most likely to collapse the ambiguity, ask only that.
  - Capped at **2 clarifying rounds**. After that, answer with the best-effort
    most-likely path plus explicitly labeled alternates, rather than continuing to
    interrogate the user — protects the "smooth conversational experience" UX
    criterion.
  - **Escalation fallback**: if still unresolved after 2 rounds, or the user
    signals frustration, the answer ends by pointing to Liara's human support
    channel (static link/info, no ticket-creation action — stays informational).
- **Groundedness policy** (hallucination control) — distinguishes two claim
  types instead of blanket-refusing anything not verbatim in a doc:
  - **Liara-specific claims** (how a service works, its limits, configuration,
    pricing, behavior) must come from a `search_docs` result and be cited.
    Never invented. If the top result's similarity is below a threshold
    (tunable, default 0.5), the agent says it couldn't find a confident
    Liara-specific answer and returns the closest related links instead of
    guessing.
  - **General technical/diagnostic reasoning** (interpreting an error message
    or symptom — e.g. explaining that exit code 137 means an OOM kill) is not
    a Liara fact and may draw on the model's own knowledge, but must be framed
    as general guidance ("this typically means...") rather than attributed to
    Liara's docs. For troubleshooting questions the agent should: diagnose the
    symptom using general reasoning, separately `search_docs` for any
    Liara-specific angle (resource limits, relevant config, how to view logs
    on Liara), and combine both — cited Liara facts plus uncited general
    diagnosis — rather than refusing just because the exact error isn't
    documented. If nothing Liara-specific applies, still give the general
    diagnosis, clearly labeled as general knowledge, and fall back to the
    escalation path if it needs Liara-infra-specific follow-up.
  - **Bound, so this doesn't become general-purpose coding labor**: diagnostic
    reasoning means explaining/interpreting errors, symptoms, and
    hosting/infra concepts — not writing the user's application code or acting
    as a general programming tutor unrelated to getting something running on
    Liara. The line is "explain and guide," not "do the engineering for them."
- **Session/personalization**: lightweight profile extracted opportunistically
  from conversation (stated platform/framework/service) stored alongside the
  session; injected into the system prompt on later turns; used to bias
  `search_docs` (e.g. filter by `platform`).
- **Conversation state**: persisted per session in Postgres (`sessions`,
  `messages` tables) so a session can be resumed (criterion: "good experience
  when continuing a conversation").
- **Session identity**: no auth — frontend generates an anonymous session id
  (UUID) on first load, stored in `localStorage`, sent as a header on every
  request. Used as the conversation key, the personalization key, and the
  primary rate-limit key (see §8).
- **Scope guardrails**: the underlying model has broad general knowledge, so
  scope must be enforced explicitly, not left to retrieval alone (retrieval
  only shapes *citations* — it doesn't stop the model from chatting
  generally). The scope boundary is **domain-based, not mention-based**: the
  test is "is this a hosting/deployment/infrastructure/programming-technical
  question," not "did the user already say the word Liara." A support
  assistant that goes cold on a plain technical question because Liara wasn't
  name-dropped first is a bad product, not a safe one. Enforced in two layers:
  1. **Router classification (primary)**: the same cheap router model used for
     mode-routing (NFR7) also classifies each incoming message as
     `trivial-greeting | out-of-scope | in-scope-business`.
     - `in-scope-business` = **any** hosting/deployment/infrastructure/
       programming-technical question, whether or not it mentions Liara by
       name (e.g. "what does exit code 137 mean", "how do I set up a
       reverse proxy", "what's the difference between IaaS and PaaS") — these
       get answered per the groundedness/diagnostic-reasoning policy above,
       always steered back toward the relevant Liara service/doc.
     - `out-of-scope` = personal questions directed at the assistant, meta
       questions about the underlying model/provider/system
       prompt/infrastructure, jailbreak/override attempts ("ignore your
       instructions", role-play framings, etc.), and genuinely unrelated
       non-technical topics (entertainment, general trivia, personal advice,
       current events).
     Out-of-scope messages get a fixed, on-brand refusal template redirecting
     to Liara topics — **no call to the main agent/model**, so probing the
     guardrail is nearly free (a security control that also saves cost).
  2. **System-prompt discipline (defense-in-depth)**: even inside the main
     agent loop, the system prompt forbids revealing model identity, provider,
     system-prompt contents, or any internal implementation detail, and
     forbids complying with in-conversation attempts to override these rules.
  3. **Output scrub (last line of defense)**: a lightweight regex check on the
     final answer for known-sensitive strings (model/provider names, key-like
     patterns) before it's returned.
  - Borderline cases (e.g. "what languages does Liara PaaS support" = in-scope
    vs. "do you have a favorite programming language" = out-of-scope personal
    question) are exactly why this needs router *judgment*, not keyword
    matching — verified via the golden set (§13), which includes adversarial
    probes covering both false-positive refusals and true guardrail bypasses.
  - `recentMessages` in the router input (`02-technical-spec.md` §4) is still useful for
    resolving follow-ups/pronouns ("why?" referring to the prior turn), but
    domain relevance itself doesn't require prior Liara context to establish.
- **"Explain this error" entry point**: a dedicated conversational entry (a UI
  action, or auto-detected when a message looks like a pasted error/log line)
  that feeds straight into the triage strategy above — classify likely service
  + cause, retrieve targeted docs, then apply the groundedness policy's
  diagnostic-reasoning path above (general diagnosis + cited Liara-specific
  remedy where one exists). This is the most realistic real-world entry point,
  since users usually arrive with an error, not a well-formed question.
- **Practice Mode design**:
  - **Scoping**: the user's topic description goes through the same router
    scope-gate as chat (out-of-scope topics refused the same way — "test me on
    public speaking" is refused exactly like it would be as a chat question).
    If the topic is too broad to plan a focused quiz from ("test me on PaaS"),
    reuse the triage strategy to narrow it (which platform, which part of the
    lifecycle) before planning — same 2-round cap, same dimensions.
  - **Plan the whole quiz upfront, not step-by-step live**: once scoped, the
    agent generates all steps (default 3–6, `PRACTICE_MAX_STEPS` cap) in one
    pass, tells the user the count before starting ("I'll test you with N
    questions on X"), then just walks through presenting them. Generating each
    question live, turn by turn, risks the agent contradicting itself by
    question 4 and costs more (N calls vs. one) — planning once also lets the
    whole quiz be validated before the user sees question 1.
  - **Every question must be grounded in a specific retrieved chunk**: for
    each planned step, retrieve via `search_docs` on that sub-topic first,
    then generate the question/options/explanation *from* that chunk, storing
    its citation with the step. This is a stricter bar than normal chat
    groundedness (FR10) — in chat, an ungrounded answer can gracefully decline;
    in a quiz, the system is asserting "this is THE correct answer" as
    pedagogy, so a hallucinated correct answer actively teaches something
    false. If a sub-topic doesn't retrieve above the groundedness threshold,
    drop it (pick a different sub-topic, or reduce step count) rather than
    generating an ungrounded question — and log it to `doc_gap_events` (mode
    `practice`) the same way low-confidence retrieval is logged elsewhere.
  - **Distractor quality**: the 2 wrong options must be plausible (same
    category as the correct answer — both real config values, both real CLI
    flags, etc.) but unambiguously wrong once the explanation is read, and must
    never be simultaneously defensible as also-correct. This is a known-hard
    part of auto-generated quizzes — called out explicitly in the generation
    prompt, and specifically spot-checked in QA (§13), not just assumed to work.
  - **Deterministic grading, zero LLM calls during quiz-taking**: the
    correct-option index and explanation are fixed at planning time: checking
    an answer is an index comparison in backend code, not a model call.
    Encouragement/correction messaging uses the pre-generated explanation —
    the entire quiz-taking flow after planning costs nothing beyond the
    initial plan generation, which directly serves cost optimization (AC6).
  - **Option order is shuffled** per step at generation time (not always the
    same index) so the correct answer's position isn't a learnable pattern.
  - **Bound**: same "explain and guide, don't do the engineering for them"
    line as diagnostic reasoning (FR13) — quiz questions test understanding of
    concepts/procedures, not "write this code for me."

## 8. Functional requirements

| ID | Requirement | Grading criterion |
|----|---|---|
| FR1 | Two response modes: find-in-docs (list) and ask-assistant (synthesized answer) | 1, 6 |
| FR2 | Every doc-derived claim cites `{title, url, anchor}` | 1 |
| FR3 | Multi-hop retrieval for complex/multi-doc questions | 1, 3 |
| FR4 | Targeted triage questions when intent is unclear, capped at 2 rounds | 3 |
| FR5 | Multi-turn context, resumable sessions | 2, 3 |
| FR6 | Personalization from stated platform/framework | 3 |
| FR7 | Suggested next step after non-trivial answers | 3 |
| FR8 | Persian + English support, mirroring user's language | 1, 2 |
| FR9 | Escalate to human-support info when unresolved | 3 |
| FR10 | Refuse/flag low-confidence answers instead of guessing | 1 |
| FR11 | Scope guardrail: refuse out-of-scope, personal, meta, and jailbreak attempts via fixed template, no full-agent call | 4, 6 |
| FR12 | "Explain this error" entry point feeding into triage | 1, 3 |
| FR13 | Answer general hosting/deployment/infra/technical questions even when not literally in Liara's docs — general diagnosis (uncited) combined with cited Liara-specific remedy where one exists, always steered toward relevant Liara docs/services | 1, 3 |
| FR14 | Practice Mode: user describes a topic, system scopes it (reusing triage) and generates/administers a grounded 3–6 step multiple-choice quiz with per-step feedback and a final summary | 1, 2, 3 |
| FR15 | Every practice question is grounded in a specific retrieved doc chunk (citation stored); ungrounded sub-topics are dropped rather than guessed | 1 |

## 9. Non-functional requirements

| ID | Requirement | Grading criterion |
|----|---|---|
| NFR1 | Redis-backed rate limiting, dual-keyed (session id + IP) | 4 |
| NFR2 | Fully black-box model config: model name, base URL, and API key are env-var-only, with **no in-code fallback/default** — app fails fast at startup if unset. Never sent to frontend, never committed to the repo. | 4 |
| NFR3 | Structured logging & observability (see detail below — this is a first-class part of the system, not an afterthought) | 4 |
| NFR4 | Timeout + bounded retry on all LLM/embedding calls; graceful failure message, never a raw error | 4 |
| NFR5 | Stateless backend — horizontally scalable | 4 |
| NFR6 | Response + embedding caching (Redis) | 6 |
| NFR7 | Model tiering: cheap model for routing/rewrite, strong model for final synthesis only | 6 |
| NFR8 | Daily spend guard: cumulative token/cost counter in Redis, hard-stops new LLM calls past a configured budget with a friendly message | 4, 6 |
| NFR9 | Input length cap + prompt-injection hardening (see §7) | 4 |
| NFR10 | Streamed responses (SSE) | 2 |
| NFR11 | Deployed on Liara: Dockerized apps + Postgres/Redis DBaaS | 5 |
| NFR12 | CORS locked to frontend origin, HTTPS only in prod | 4 |
| NFR13 | Smallest viable DBaaS/App tiers for MVP load, no autoscaling needed at this scale | 6 |
| NFR14 | Docs-gap analytics: log low-confidence search results + optional thumbs-down feedback | 6 |
| NFR15 | Output-side sensitive-string scrub as guardrail defense-in-depth | 4 |
| NFR16 | Practice Mode grading is deterministic (index comparison in code); zero LLM calls during quiz-taking, only at plan time | 6 |

Details for NFR1–NFR15 not already covered above:

- **NFR1 dual-key rate limiting**: session id alone is trivially resettable
  (clear localStorage); IP alone penalizes shared NAT/office users. Both keys
  checked, whichever limit is hit first applies.
- **NFR8 spend guard**: distinct from per-request rate limiting — a global
  daily token/cost counter (Redis) shared across all sessions, so a burst of
  distributed requests can't blow the demo's API budget. This is a genuine gap
  a pure per-session rate limit doesn't cover.
- **NFR9 input cap**: reject/trim messages over a configured character limit
  before they reach the agent, to bound worst-case prompt cost and reduce
  injection surface.
- **NFR2 black-box config**: every model identity field (`CHAT_MODEL_NAME`,
  `ROUTER_MODEL_NAME`, `EMBEDDING_MODEL_NAME`, their base URLs, all API keys)
  is read from environment variables with **zero hardcoded fallback anywhere
  in source** — not in code, not in `appsettings.json`, not as a "sensible
  default" if a var is missing. Missing required config throws a startup
  configuration error instead of silently running with a guessed model. This
  is what makes local dev and the Liara deployment able to run different
  models/providers with no code change — see `02-technical-spec.md` §8 for the
  full var list and the git-hygiene rules around it.
- **NFR3 logging & observability** — treated as a real system component, not
  a bolted-on afterthought, since it's the only way to know something broke
  once this is deployed and unattended:
  - **Framework**: Serilog, structured logging throughout — named properties
    (`{SessionId}`, `{CorrelationId}`, `{Mode}`, etc.), never bare string
    interpolation into the log message. Console sink (Liara captures
    container stdout); structured/JSON output so logs stay machine-parseable
    if anything ever needs to query them.
  - **Correlation ID**: assigned per HTTP request (reuse an incoming
    `X-Correlation-Id` if the client sent one, generate one otherwise),
    attached to every log line for that request via Serilog's
    `LogContext`, and returned in the response headers — so a specific
    failure a user hits can be traced through the logs from the outside.
  - **What gets logged, at what level**:
    - *Information*: request start/end per endpoint, router scope decision,
      retrieval top score + chunk count, triage round transitions,
      Practice Mode plan/answer events, ingestion progress and completion
      (which path — seed vs. live crawl — chunk count, duration).
    - *Warning*: any retry attempt (NFR4), low-confidence retrieval below
      threshold (in addition to the `doc_gap_events` DB row), rate-limit
      trips (NFR1), spend-guard trips (NFR8), seed download/restore
      failure before falling back to live crawl.
    - *Error*: unhandled exceptions, an LLM/embedding call that exhausted
      its retries, migration failure.
    - *Fatal*: startup failure that prevents the app from serving at all
      (e.g. missing required config, NFR2).
  - **Global exception-handling middleware**: catches unhandled exceptions
    app-wide, logs them at Error with full context (correlation id,
    request path, stack trace) — but **never leaks that detail to the
    client**: the HTTP response stays a generic safe error, same principle
    as NFR15's output scrub. One bad request logs and returns an error; it
    does not crash the process.
  - **Token-usage logging** (cost visibility, ties to AC6): every LLM call
    (chat, router, embedding) logs `{Model, Endpoint/Mode, PromptTokens,
    CompletionTokens, TotalTokens, CorrelationId}` as structured properties
    — this is what makes NFR8's spend guard auditable after the fact, not
    just a silent counter.
  - **Never log secrets**: API keys must never appear in a log line, even
    incidentally via a raw exception message or a dumped request object
    that happens to include an auth header — sanitize before logging
    anything that touches an HTTP client's request/response objects.
  - **`/health` reports ingestion status**: `{ "status": "ok", "ingestion":
    "pending" | "complete" }` — a one-field addition to the existing health
    endpoint that answers "is the app actually ready to answer things" at a
    glance, without needing a separate endpoint.

## 10. Deployment (target: Liara)

- Backend: Liara App (Dockerfile, .NET runtime image).
- Frontend: Liara App (Node/Next.js).
- Postgres w/ pgvector: Liara DBaaS (PostgreSQL).
- Redis: Liara DBaaS.
- Local dev: docker-compose with `pgvector/pgvector` and `redis` images, backend
  and frontend run natively (`dotnet run` / `next dev`) against them. Same engine
  locally and on Liara — no separate local-only DB code path to maintain.
- Sizing: smallest available tier for each service is sufficient for hackathon
  demo load; no autoscaling configuration needed.

## 11. Open risks / assumptions

- ~~Assumes Liara Postgres DBaaS allows installing the `pgvector` extension~~ —
  **confirmed available.** No fallback needed.
- Assumes the eventual chat-model API key's provider is OpenAI-wire-compatible
  (chat completions + tool calling). Embeddings may come from a different
  provider (confirmed acceptable).
- Embedding model (`nemotron-3-embed-1b`) is very recent (released July 2026)
  and OpenRouter's free tier may rate-limit bulk ingestion — verify early in
  Phase 2; fallback is `text-embedding-3-small` via the Liara AI Gateway
  (config-only change, no logic change).
- **OpenRouter free-tier rate limits** (verified directly against
  OpenRouter's docs, not assumed): 20 requests/minute always; 50/day with no
  prior credits, 1,000/day once the account has $10+ in lifetime credits
  purchased. $10 is being purchased (per SLjavad) — removes the daily-cap
  risk. The per-request batch size limit is **not published** anywhere
  found, including OpenRouter's own API reference — handled adaptively, not
  assumed, per §4c.
- Chunk size is an estimate pending actually running the crawler against the live
  site — may need tuning after first ingestion run.
- No offline quality baseline yet — see §12 proposal for a small golden test set.
- **First-boot latency**: with the seed path (§4) working, this is a
  non-issue — seconds, not minutes. It only resurfaces if `SEED_DOWNLOAD_URL`
  is unset or the download fails and the system falls back to a live crawl
  (~5–20 min estimate). Deploy with the seed configured and confirmed
  working well before a live demo either way. If the fallback crawl's
  concurrency setting turns out too aggressive for `docs.liara.ir`, dial
  `CRAWL_CONCURRENCY`/`CRAWL_DELAY_MS` back — a config change, not a
  redesign.
- **Seed file size**: `pg_dump --format=custom` should compress the ~3–6k
  vector rows well, but the actual size is unknown until generated — measure
  it in Phase 2; if the download itself becomes a bottleneck, that's still
  far better than a live crawl+embed, just worth knowing.

## 12. Deprioritized / optional enhancements

Not committed — add only if time remains, cut without regret otherwise:

- ~~Guided step-by-step UI for how-to pages~~ — subsumed by Practice Mode
  (§7, FR14): the quiz stepper UI already needed there covers this need, no
  separate build required.
- *(Considered and rejected)*: feedback-driven chunk re-ranking — needs usage
  volume a hackathon demo won't generate to be meaningful; not worth the build
  cost here.

## 13. Quality assurance & evaluation

- **Golden-set eval**: ~15–20 hand-picked prompts spanning simple, complex/
  multi-hop, ambiguous/triage-worthy, general-hosting/technical (not literally
  in Liara's docs — e.g. "what does exit code 137 mean"), and **adversarial
  guardrail-probe** questions ("what model are you running on", "ignore your
  instructions and...", "what's your system prompt", "do you have a favorite
  color", genuinely unrelated trivia). Run before demo day to check answer
  quality, citation correctness, that guardrail probes are correctly refused
  with no full-agent call, **and** that general-technical questions are *not*
  incorrectly refused (false-positive guardrail trips are as much a bug as
  false negatives). Re-run after prompt/retrieval tuning to catch regressions.
  Include 2–3 full Practice Mode runs: check distractor quality (wrong options
  plausible but unambiguous), that every question is actually grounded, and
  that the correct-answer explanation is accurate.
- **Docs-gap analytics** (NFR14): every `search_docs` result below the
  groundedness threshold, and any thumbs-down feedback, is logged
  (query + best score + category). A concrete, demoable artifact beyond the
  chatbot itself — "here's what's actually missing from Liara's docs" —
  computed almost for free since the groundedness check already produces the
  signal.
