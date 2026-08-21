# AGENTS.md — Liara Docs Assistant

Orientation for any coding agent (OpenCode, Claude Code, etc.) working in this
repo. This file is a pointer and a list of non-negotiables — it is **not**
authoritative over the specs. If anything here conflicts with `specs/`, the
specs win and this file is stale; fix it rather than trusting it.

## Status

Specs are complete. Implementation has not started. Work phase by phase from
`specs/03-plan.md`, starting at Phase 1. Do not skip ahead or improvise
architecture that isn't in the specs — if the specs are silent or wrong on
something you hit, stop and flag it rather than guessing.

## OpenCode agent split

`opencode.json` (repo root) defines two custom primary agents, each pinned to
a different model via the OpenCode Go plan and scoped by edit permission to
its own directory — switch between them with **Tab** or `@backend`/
`@frontend` in the OpenCode TUI:
- **`backend`** — `opencode-go/glm-5.3`, edit access restricted to
  `backend/**`, `docker-compose.yml`, `.env.example`.
- **`frontend`** — `opencode-go/kimi-k3`, edit access restricted to
  `frontend/**`.

Both are denied write access to `specs/**`, `AGENTS.md`, `CLAUDE.md`, and
`.env` — spec changes are a tech-lead edit, never an implementation-agent
one. Model ids confirmed against `https://opencode.ai/zen/go/v1/models`.

## Codebase indexing tool

This project uses [codebase-memory-mcp](https://github.com/DeusData/codebase-memory-mcp)
for code structure navigation (`search_graph`, `query_graph`, `trace_path`,
`get_architecture`, etc.) — installed and configured for both Claude Code and
OpenCode on this machine. Once real source code exists (Phase 1 onward),
prefer these tools over ad-hoc grepping for understanding how existing code
relates before making cross-cutting changes. Not useful yet against just the
specs — its value kicks in once `backend/`/`frontend/` have real code.

## Source of truth (read before writing any code)

- `specs/01-architecture.md` — business context, personas, architecture,
  agent design, functional/non-functional requirements (the *why*).
- `specs/02-technical-spec.md` — DB schema, **pinned tool/package versions**
  (§0 — don't guess or use "latest", they're pinned explicitly), API
  contracts, router/tool signatures, config reference (the exact *what*,
  implementation-level).
- `specs/03-plan.md` — phased execution plan, in order (the *when*).
- `specs/04-prompts.md` — **literal system/router/exam-generation prompt
  text and refusal templates**. Use verbatim, interpolating only the noted
  placeholders — do not paraphrase, rewrite, or re-derive these from the
  policy descriptions in `01-architecture.md` §7.
- `specs/05-frontend-plan.md` — full frontend architecture: routes,
  components, state management, streaming, RTL handling.

## What this is

An LLM-based assistant over Liara's docs (docs.liara.ir, ~1100 pages), three
modes sharing one retrieval index:
1. **Find-in-docs** — retrieval only, ranked doc links, no generation.
2. **Ask-assistant** — full agentic RAG chat: multi-hop retrieval, citations,
   triage/clarifying questions, groundedness policy, guardrails.
3. **Practice Mode** — user names a topic, system plans and administers a
   grounded multiple-choice quiz with immediate feedback and a summary.

## Stack

- Backend: ASP.NET Core (.NET), Microsoft Agent Framework for orchestration.
- Frontend: Next.js + TypeScript + shadcn/ui.
- Data: PostgreSQL + pgvector (doc chunks, sessions, messages, quiz state),
  Redis (cache, rate limiting, spend guard).
- Ingestion: in-process (AngleSharp crawler + chunker + embedder), auto-run
  once on first boot by a hosted background service — no separate worker, no
  human trigger, no Node.js dependency. See Non-negotiable 9 below.
- All LLM calls (chat, router, embeddings) go through an OpenAI-compatible
  client — provider is config, never a code dependency.

## Non-negotiables

These are the things most likely to get silently violated by a plausible-
looking shortcut. Don't.

1. **Black-box model config** (NFR2): model name, base URL, and API key are
   env-var-only, with **zero hardcoded fallback anywhere in source** — not in
   code, not in `appsettings.json`, not as a "sensible default." Missing
   required config throws a startup error; it never silently guesses a model.
2. **Never commit secrets or identity config**: `.env` / `.env.local` are
   gitignored from the first commit. `.env.example` holds variable names
   only, no real values. See `specs/02-technical-spec.md` §9.
3. **Every doc-derived claim is cited** with `{title, url, anchor}` from an
   actual `search_docs` result — in chat answers and in every practice-mode
   question. Never invent a citation or a "correct answer" that isn't grounded
   in a retrieved chunk.
4. **The scope guardrail runs before the expensive model**, on every
   user-facing entry point (chat, search, practice topic). Refusal/trivial
   text is a static template, never model-generated.
5. **Practice Mode grading is a code-level index comparison** — never an LLM
   call. The entire quiz-taking flow after planning must cost nothing beyond
   the initial plan generation.
6. **Triage/clarification round-capping is enforced in backend code** (a
   counter persisted in `sessions.pending_clarification`), never left to the
   model to self-track across turns.
7. **Tool/package versions are pinned, not "latest"** (`02-technical-spec.md`
   §0). If a pinned version is stale by the time you're building, ask/flag
   it — don't silently substitute a newer one.
8. **Prompts are content, not code to author**: system prompt, router
   prompt, exam-generation prompt, and all refusal/trivial templates come
   verbatim from `04-prompts.md`. Writing new prompt wording inline while
   implementing is out of scope for a coding agent here — that's a tech-lead
   edit to `04-prompts.md`.
9. **Ingestion is fully automatic — never require a human to trigger it.**
   A hosted background service checks `doc_chunks` on startup and runs the
   crawl/chunk/embed pipeline itself if empty, then never again once
   populated. No separate console app, no "run this script first" step in
   any README. See `02-technical-spec.md` §2a.
10. **Never assert an unverified external limit/spec as fact.** If a
    provider's rate limit, batch size, or API behavior isn't confirmed from
    their actual docs, code defensively (adaptive batch-splitting on error,
    per §4c) instead of hardcoding a guessed number.
11. **Logging is a first-class part of this system, not an afterthought**
    (NFR3, `01-architecture.md` §9): structured Serilog logging with a
    correlation id per request, the log-level guidance there followed
    consistently, secrets never logged, and a global exception handler that
    logs full context but never leaks it to the client.

## Repo layout (target — see `03-plan.md` Phase 1 for the full scaffold)

```
backend/        ASP.NET Core API + Agent Framework orchestrator + EF Core +
                 in-process ingestion background service (AngleSharp)
frontend/       Next.js + shadcn/ui
specs/          this project's specs (source of truth)
docker-compose.yml   local Postgres+pgvector, Redis
.env.example
```

## Local dev

`docker-compose up` for Postgres+pgvector and Redis (`01-architecture.md`
§10). Same engine locally and on Liara — no separate local-only DB path.

## Before your first commit

Confirm `.gitignore` covers `.env`/`.env.local` and grep the working tree for
anything that looks like a real key, model id, or base URL outside those
gitignored files (`02-technical-spec.md` §9).
