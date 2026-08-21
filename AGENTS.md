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

## Source of truth (read before writing any code)

- `specs/01-architecture.md` — business context, personas, architecture,
  agent design, functional/non-functional requirements (the *why*).
- `specs/02-technical-spec.md` — DB schema, API contracts, router/tool
  signatures, config reference (the exact *what*, implementation-level).
- `specs/03-plan.md` — phased execution plan, in order (the *when*).

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
- Ingestion: existing Node crawler (`indexer/`, reused/adapted) → .NET worker
  (chunk, embed, upsert).
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

## Repo layout (target — see `03-plan.md` Phase 1 for the full scaffold)

```
backend/        ASP.NET Core API, Agent Framework orchestrator, EF Core
frontend/       Next.js + shadcn/ui
ingestion/      crawler (Node) + Ingestion.Worker (.NET)
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
