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
- ~~Liara account access to verify the `pgvector` extension~~ — **confirmed
  available on Liara's managed Postgres.**
- Still needed before Phase 7 (deployment) at the latest: real API key(s) for
  whichever model(s) are finalized, and the Liara support channel URL for the
  escalation fallback (`SUPPORT_CHANNEL_URL`).

## Phase 1 — Repo scaffolding & infra plumbing

Target structure:
```
LiaraChallenge/
  specs/                     (done)
  backend/
    LiaraDocsAssistant.sln
    src/
      Api/                   ASP.NET Core Web API — endpoints, SSE, middleware
      Agent/                 Microsoft Agent Framework orchestrator, tools, prompts
      Retrieval/             search_docs hybrid retrieval service
      Data/                  EF Core DbContext, entities, migrations
      Ingestion.Worker/      .NET console Ingestor (Phase 2 step 2)
    tests/
  frontend/                  Next.js + shadcn/ui
  ingestion/
    crawler/                 adapted Node indexer (Phase 2 step 1)
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

## Phase 2 — Ingestion pipeline (01-architecture §4, 02-technical-spec §1–2)

1. **Node crawler** (`ingestion/crawler/`, adapted from `indexer/`): switch the
   existing (currently dead/commented) JSON-dump step on instead of pushing to
   Meilisearch. Add category tagging from the URL path segment (02-technical-spec §2
   taxonomy table). Run against `docs.liara.ir` → `crawl-doc-data.json`.
2. **.NET `Ingestion.Worker`**: read the JSON dump, normalize text, chunk
   (merge tiny/split oversized sections), call the embedding endpoint
   (batched), upsert into `doc_chunks` keyed by `(url, anchor)` with
   `content_hash` idempotency (02-technical-spec §1).
3. **QA pass** (01-architecture §4 QA step + §11 risk):
   - Spot-check a sample of chunks — do `anchor` links resolve to the right
     in-page section?
   - **Rate-limit check (do this first, before running the full corpus)**:
     embed a small batch (~20 chunks) via OpenRouter's free-tier
     `nemotron-3-embed-1b` and confirm it can sustain the full ~3–6k chunk
     ingestion without excessive throttling. If not, switch
     `EMBEDDING_MODEL_NAME`/`EMBEDDING_BASE_URL`/`EMBED_DIM` to the
     `text-embedding-3-small` fallback (01-architecture §11) and proceed —
     config-only change.
   - Cross-lingual smoke test: run a handful of English queries against the
     (mostly Persian) corpus and check top results are sensible — the model's
     published Persian benchmark makes this a confirmation pass, not a
     from-scratch risk.
   - Sanity-check chunk count/size distribution against the ~200–800 token
     estimate; tune chunking if far off.

## Phase 3 — Retrieval + Router + `/api/search`

- `Retrieval` service: hybrid pgvector cosine + `pg_trgm` title boost,
  top-k default 5 (02-technical-spec §7). Unit-testable independent of the agent.
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
  `doc_gap_events`, mode `practice`) rather than guessing. Persist the full
  plan to `practice_exams.steps` in one write.
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

- Build the golden set (01-architecture §13): ~15–20 prompts — simple,
  complex/multi-hop, ambiguous/triage-worthy, general-hosting/technical (not
  literally in the docs), and adversarial guardrail probes covering **both**
  true refusals (jailbreak/meta/personal) **and** false-positive checks
  (technical questions that must *not* be refused).
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
- Provision Liara Postgres (pgvector) and Redis DBaaS — or the self-hosted
  Postgres+pgvector fallback if Phase 0 found extensions unavailable.
- Set env vars per 02-technical-spec §8 config table on both Liara apps.
- Run ingestion once against the deployed DB (point `Ingestion.Worker` at the
  prod connection string, or re-run the whole pipeline).
- Re-run the golden set against the deployed instance before calling it done.

## Phase 8 — Demo prep

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
