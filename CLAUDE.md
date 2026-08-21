# CLAUDE.md

This repo is spec-first. Read [AGENTS.md](AGENTS.md) in full before doing
anything else here — it has the project orientation and the non-negotiable
rules, and points to the full specs under `specs/`.

Role split: architecture and spec decisions are made in planning sessions
(human tech lead + Claude, working directly in `specs/*.md`, no code written
there). Implementation in this repo follows those specs — if you're
implementing here, work `specs/03-plan.md` phase by phase and treat
`specs/01-architecture.md` and `specs/02-technical-spec.md` as authoritative
over any inference you'd otherwise make.
