# Execution Plan — Liara Docs Assistant

Ordered for a hackathon: de-risk unknowns first, build the backbone before
polish, keep AI-dependent work unblocked from non-AI plumbing wherever
possible. Each phase references the spec sections it implements. "Owner: SLjavad"
marks steps that need a human decision/credential, not code.

## Phase 0 — Unblock (Owner: SLjavad)

No longer a hard blocker on other phases — all model choices are config values
(02-technical-spec.md §8), not code, so build proceeds now with placeholders and gets the
real values swapped in later:

- Chat model + router model: placeholder `openai/gpt-4.1-mini` /
  `openai/gpt-4o-mini` via the Liara AI Gateway (OpenAI-wire-compatible per
  01-architecture.md §3) — swap for the real choice whenever decided.
- Embedding model: `nvidia/nemotron-3-embed-1b:free` via OpenRouter — #1 on
  RTEB, explicitly Persian-benchmarked, free — `EMBED_DIM=2048`. Needs its own
  `OPENROUTER_API_KEY` (separate from the Liara Gateway key). Locked in now so
  DB migration work isn't blocked; verify free-tier rate limits handle bulk
  ingestion in Phase 2 — fallback is `openai/text-embedding-3-small` via the
  Liara AI Gateway (`EMBED_DIM=1536`), a config-only swap either way.
- ~~$10 lifetime credits on the OpenRouter account~~ — **being purchased**
  (per SLjavad); bumps the free-tier daily request cap from 50 to 1,000
  (01-architecture.md §11).
- ~~Liara account access to verify the `pgvector` extension~~ — **confirmed
  available on Liara's managed Postgres.**
- ~~Liara Object Storage for the ingestion seed file~~ — SLjavad is setting
  this up (01-architecture.md §4a).
- Still needed before Phase 7 (deployment) at the latest: real API key(s) for
  whichever model(s) are finalized, and the Liara support channel URL for the
  escalation fallback (`SUPPORT_CHANNEL_URL`).

## Phase 1 — Repo scaffolding & infra plumbing

Target structure — everything backend is **one solution, one deployable
process** (01-architecture.md §3), no separate ingestion service/language:
```
LiaraChallenge/
  specs/                     (done)
  backend/
    LiaraDocsAssistant.sln
    src/
      Api/                   ASP.NET Core Web API — endpoints, SSE, middleware,
                              Program.cs registers IngestionBackgroundService
      Agent/                 Microsoft Agent Framework orchestrator, tools, prompts
      Retrieval/             search_docs hybrid retrieval service
      Ingestion/             AngleSharp crawler + chunker + embedder (Phase 2),
                              referenced by Api as a hosted service, not a
                              separate console app
      Data/                  EF Core DbContext, entities, migrations
    tests/
  frontend/                  Next.js + shadcn/ui
  docker-compose.yml         local Postgres+pgvector, Redis
  .env.example
```

- **First, before anything else** (a real git repo is already waiting for the
  first push): `.gitignore` covering `.env`, `.env.local`, and any
  `appsettings.*.json` that could carry secrets; `.env.example` with var names
  only, no real values (02-technical-spec §9, NFR2). Do this before writing
  any code that reads config, not after.
- `docker-compose.yml`: `pgvector/pgvector` + `redis` images (01-architecture §10).
- Backend: ASP.NET Core Web API skeleton, `/health` (02-technical-spec §6), Serilog
  structured logging (NFR3), config binding for the full env var table
  (02-technical-spec §8), CORS locked to frontend origin (NFR12).
- EF Core `DbContext` + initial migration for the schema in 02-technical-spec §1 —
  **blocked on `EMBED_DIM` from Phase 0**.
- Redis client wiring: key scheme from 02-technical-spec §3 (rate limit, spend guard,
  caches) — services can be scaffolded now, business logic lands in Phase 3–4.

## Phase 2 — Ingestion background service (01-architecture §4, 02-technical-spec §1–2a)

Fully automatic either way — no manual crawler run, no separate worker to
invoke at deploy time. Build `backend/src/Ingestion/` and wire it as a
hosted service, shared by both the seed path and the live-crawl fallback:

1. **`IngestionBackgroundService`** (`BackgroundService`, registered in
   `Api`'s `Program.cs`): on startup, apply migrations
   (`Database.MigrateAsync()`), check `doc_chunks` row count — skip if
   already populated; if empty, try the seed download (`SEED_DOWNLOAD_URL`)
   first, fall back to the live crawl only if that's unset or fails
   (02-technical-spec §2a startup sequence).
2. **Crawl** (AngleSharp): fetch `DOCS_SITEMAP_URL`, filter by taxonomy path
   prefixes, fetch+parse each page (`h1` title, `h2`–`h6` section
   boundaries, anchor from heading/adjacent element `id`), category from URL
   path segment. Same code path serves both uses:
   - **Seed generation (dev-time, run by us)**: `docs` repo running locally
     via `npm run dev` (port 3001), `DOCS_SITEMAP_URL` pointed at
     `localhost:3001` — no concurrency/delay tuning needed.
   - **Live-crawl fallback (production, rare)**: `DOCS_SITEMAP_URL` at
     `docs.liara.ir`, bounded concurrency (`CRAWL_CONCURRENCY`,
     `CRAWL_DELAY_MS`) — **not** the original indexer's serial 4.5s delay
     (82+ minutes for 1100 pages).
3. **Chunk/embed/upsert**: as in `02-technical-spec.md` §1 — one chunk per
   section, embed starting at `EMBED_BATCH_SIZE` (default 20), halving and
   retrying on a size-related API error rather than trusting a hardcoded
   max (OpenRouter doesn't publish one for this endpoint — 01-architecture
   §4c). Upsert into `doc_chunks` keyed by `(url, anchor)` with
   `content_hash` idempotency.
4. **Generate and publish the seed** (dev-time, once, by us — not part of
   any automated deploy):
   - Run the pipeline locally against `localhost:3001` (step 2).
   - $10 in lifetime OpenRouter credits is being purchased (per SLjavad) —
     bumps the daily request cap from 50 to 1,000, which comfortably
     absorbs iterative re-runs while tuning chunking regardless of the
     actual (unverified) batch size — see 01-architecture §11.
   - `pg_dump --format=custom` scoped to `doc_chunks`, upload to **Liara
     Object Storage** (never committed to git — dumps of a few thousand
     2048-dim vectors are large and don't belong in git history). Confirm
     whether the bucket is public-read (plain `SEED_DOWNLOAD_URL`) or
     private (needs `OBJECT_STORAGE_ACCESS_KEY`/`_SECRET_KEY`,
     02-technical-spec §8) before implementing the download side.
   - Set `SEED_DOWNLOAD_URL` (and the access keys if private) wherever the
     app runs (local `.env` and Liara env vars, Phase 7).
5. **QA pass** (01-architecture §4 QA step + §11 risk):
   - **Verify the actual returned embedding dimension is 2048** before
     locking in `EMBED_DIM` at migration time (one source suggested 4096,
     likely describing the 8B variant — confirm against what the API
     actually returns for the 1B model before trusting either number).
   - **Verify prefixing is actually wired up**: `IEmbeddingService`
     (02-technical-spec §7a) must prepend `"passage: "` for chunks and
     `"query: "` for queries — this is easy to silently skip since nothing
     errors if it's missing, it just quietly ranks worse. Spot-check a
     known query against a known-relevant chunk with and without prefixing
     to confirm it actually changes/improves the ranking.
   - Spot-check a sample of chunks — do `anchor` links resolve to the right
     in-page section?
   - Cross-lingual smoke test: run a handful of English queries against the
     (mostly Persian) corpus and check top results are sensible — the
     model's published Persian benchmark makes this a confirmation pass,
     not a from-scratch risk.
   - Sanity-check chunk count/size distribution against the ~200–800 token
     estimate; tune chunking if far off.
   - Confirm the seed path actually loads correctly on a fresh empty DB
     (`pg_restore` succeeds, `doc_chunks` populated, `search_docs` returns
     sensible results) before relying on it for Phase 7 deployment.
   - Time one live-crawl fallback run too (even though it's not the primary
     path) — confirm it lands in the ~5–20 min estimate and doesn't error
     out against `docs.liara.ir`'s actual response behavior.

## Phase 3 — Retrieval + Router + `/api/search`

- `Retrieval` service: hybrid pgvector cosine + `pg_trgm` title boost,
  top-k default 5 (02-technical-spec §7). Embeds incoming queries via
  `IEmbeddingService.EmbedQueryAsync` (§7a) — never the raw text directly,
  the `"query: "` prefix is part of correct retrieval, not optional
  polish. Unit-testable independent of the agent.
- `Router` service (02-technical-spec §4): JSON-mode cheap-model call using
  the literal prompt in `04-prompts.md` §2. Refusal/trivial response text
  comes from the literal templates in `04-prompts.md` §4 — do not write new
  copy for these, use them verbatim.
- `POST /api/search` (02-technical-spec §6): router → (if in-scope) decompose → retrieve
  per sub-query → merge/dedupe → response shape per spec.
- Wire for this endpoint first (simpler than chat): NFR1 rate limiting
  (dual-key Redis), NFR8 spend guard, NFR6 response cache.
- `GET /api/categories` (static taxonomy).

## Phase 4 — Agent + `/api/chat`

- Configure Microsoft Agent Framework orchestrator; register `search_docs` and
  `list_categories` tools (02-technical-spec §7).
- System prompt and router prompt: use the literal text in `04-prompts.md`
  §1/§2 verbatim (interpolating the noted placeholders) — do not rewrite or
  re-derive these from the policy descriptions in 01-architecture §7.
- `pending_clarification` state machine (02-technical-spec §5) — **enforced in backend
  code**, not left to the model to self-count triage rounds.
- `POST /api/chat` SSE endpoint: event schema (`meta`/`token`/`sources`/`done`/
  `error`) per 02-technical-spec §6. Router scope-gate runs before the agent is invoked.
- NFR4 resilience: timeout + bounded retry on LLM/embedding calls, graceful
  SSE `error` event instead of a raw failure.
- NFR14 docs-gap logging: write `doc_gap_events` whenever a `search_docs` call
  scores below `RETRIEVAL_GROUNDEDNESS_THRESHOLD`.
- `GET /api/sessions/{id}/messages`, `POST /api/feedback` (message_feedback +
  conditional doc_gap_events on thumbs-down, per 02-technical-spec §6).

## Phase 4b — Practice Mode (`/api/practice/*`, 02-technical-spec §6a)

- Topic scoping: run the message through the same `Router` service (mode
  `practice`) as chat; reuse the triage strategy/state machine from Phase 4 to
  narrow an over-broad topic before planning.
- Exam planning: for each of `PRACTICE_MIN_STEPS`–`PRACTICE_MAX_STEPS` steps,
  call `search_docs` on the sub-topic, then generate the question/options/
  explanation using the literal prompt in `04-prompts.md` §3 (one call,
  batched across all sub-topic chunks). Shuffle option order after
  generation, store citation. Drop `{"skip": true}` sub-topics (log to
  `doc_gap_events`, mode `practice`) rather than guessing — if fewer than
  `PRACTICE_MIN_STEPS` remain after dropping, return `status:
  "insufficient_material"` (02-technical-spec §6a) instead of running a
  short quiz. Persist the full plan to `practice_exams.steps` in one write.
- `POST /api/practice/start`, `POST /api/practice/{examId}/answer` (pure
  code-level grading, no LLM call — NFR16), `GET /api/practice/{examId}/summary`
  per the JSON contracts in 02-technical-spec §6a.
- This phase can reuse the `Retrieval` and `Router` services built in Phase 3
  as-is — no new retrieval logic, only new orchestration around them.

## Phase 5 — Frontend (Next.js + shadcn/ui)

Full plan — routes, components, state, streaming, RTL handling, exact shadcn
component list — is `05-frontend-plan.md`. Build it in this rough order:
app shell + `SessionProvider` + `ModeNav` → API client layer (§3) →
`useChatStream` (§4) → Ask Assistant (§6) → Find in Docs (§7) → Practice Mode
(§8) → responsive/accessibility pass (§10).

## Phase 6 — QA & tuning

- Build the golden set (01-architecture §13) as `backend/tests/golden-set.json`
  (prompt + expected scope/behavior per entry, so it's re-runnable, not just a
  one-off manual checklist): ~15–20 prompts — simple, complex/multi-hop,
  ambiguous/triage-worthy, general-hosting/technical (not literally in the
  docs), and adversarial guardrail probes covering **both** true refusals
  (jailbreak/meta/personal) **and** false-positive checks (technical
  questions that must *not* be refused).
- Run it, tune: groundedness threshold, `MAX_CLARIFYING_ROUNDS`, top-k, router
  prompt wording for the false-positive cases.
- Verify NFR1/NFR8 (rate limit + spend guard trip correctly), NFR12 (CORS),
  NFR15 (output scrub), secrets never reach the frontend, and re-run the
  pre-push grep check (NFR2, 02-technical-spec §9) — confirm nothing real ever
  landed in git history, not just the current working tree.
- Confirm `doc_gap_events` populates as expected (spot-check a few known-thin
  doc areas, across all three modes).
- Run 2–3 full Practice Mode quizzes end to end: verify distractor quality,
  that every question is actually grounded, and the explanation text is
  accurate (01-architecture §13).
- Light unit tests on the pure-logic pieces worth locking down: chunking,
  retrieval scoring/threshold, triage round-cap state transitions — not full
  coverage, just the logic that's easy to silently break while tuning.

## Phase 7 — Deployment to Liara (01-architecture §10)

- Dockerfiles: backend (.NET runtime image), frontend (Next.js).
- Provision Liara Postgres (pgvector) and Redis DBaaS.
- Set env vars per 02-technical-spec §8 config table on both Liara apps,
  **including `SEED_DOWNLOAD_URL`** from the Phase 2 seed generation step —
  this is what makes first boot fast (seconds) instead of a live crawl
  (~5–20 min). No manual ingestion step either way — it's automatic.
- Confirm the seed path actually ran (check startup logs, §2a
  observability) rather than assuming it worked — if `SEED_DOWNLOAD_URL` is
  wrong/unreachable, it silently falls back to the slow live crawl instead
  of failing loudly, so check.
- Re-run the golden set against the deployed instance after confirming
  ingestion has completed either way.

## Phase 8 — Demo prep

**Confirm the seed path worked before treating a deploy as demo-ready** —
with `SEED_DOWNLOAD_URL` configured and confirmed (Phase 7), first boot is
seconds, not minutes. If it silently fell back to a live crawl instead
(check logs), budget the ~5–20 min it takes and redeploy with more lead
time rather than demoing against a partially-ingested index.

Script a run-through covering, in order: a simple question, a complex
multi-hop question, an ambiguous question that triggers triage, a general
hosting/technical question not literally in the docs (FR13), a guardrail
probe that gets refused, a resumed session, find-in-docs mode, a full Practice
Mode quiz (FR14) start to finish, and a thumbs-down that shows up in docs-gap
analytics — this sequence doubles as a walk-through of every grading criterion.

## Demo-readiness checklist (mapped to the 6 grading criteria)

| # | Criterion | Verified by |
|---|---|---|
| 1 | Response quality/accuracy | Golden-set eval (Phase 6), citations present (FR2), groundedness policy (FR10, FR13, FR15) |
| 2 | UI/UX | Phase 5 build + manual pass: streaming, markdown/code rendering, resumed session, responsiveness, Practice Mode stepper |
| 3 | Agentic/personalization | Triage (FR4), multi-hop (FR3), personalization (FR6), next-step (FR7), "explain this error" (FR12), Practice Mode (FR14) |
| 4 | Security/stability/monitoring | NFR1–4, NFR9, NFR12, NFR15 verified in Phase 6 |
| 5 | Liara deployment | Phase 7 complete, golden set re-passes on deployed instance |
| 6 | Cost optimization | NFR6–8, NFR13, NFR16 in place; token-usage logging (NFR3) shows real numbers during demo prep |
