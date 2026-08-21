# Frontend Plan — Liara Docs Assistant

Full frontend architecture: routes, components, state, and how each screen
talks to the API contracts in `02-technical-spec.md`. Pinned tooling is in
`02-technical-spec.md` §0 — don't repeat version decisions here, just use them.

## 1. Routing (Next.js App Router)

```
app/
  layout.tsx           root layout: fonts, ThemeProvider, SessionProvider, ModeNav
  page.tsx              redirects to /chat (default mode)
  chat/page.tsx          Ask Assistant
  search/page.tsx         Find in Docs
  practice/page.tsx        Practice Mode (topic form -> quiz -> summary, all client-state, no per-exam route)
```

No dynamic route for an in-progress practice exam — `examId` is tracked in
client state, not the URL. Resumability (FR5) is a chat-session concept only
(`GET /api/sessions/{id}/messages`); Practice Mode doesn't need to survive a
page reload mid-quiz for this MVP.

## 2. State management

**No external state library** (no Redux/Zustand) — three largely independent
modes, no complex cross-cutting state, React context + hooks is enough and
keeps dependencies minimal.

- `SessionProvider` (React context, mounted in root layout): owns `sessionId`
  (generate a UUID on first mount if `localStorage` has none, persist it,
  send as `X-Session-Id` header everywhere) and `locale` (`'fa' | 'en'`,
  default `'fa'`, user-toggleable — affects UI chrome text only, see §5 on
  per-message direction).
- Each mode page owns its own local state (`useState`/`useReducer`) for its
  conversation/results/quiz — no need to lift this into global state.

## 3. API client layer

`lib/types.ts` — TypeScript types mirroring every JSON shape in
`02-technical-spec.md` §6/§6a exactly (request and response types per
endpoint). `lib/api.ts` — one typed function per endpoint (`postSearch`,
`postFeedback`, `getCategories`, `getSessionMessages`, `postPracticeStart`,
`postPracticeAnswer`, `getPracticeSummary`), all reading the API base URL
from a build-time env var, all attaching `X-Session-Id`. Keep `/api/chat`
separate (see §4) since it's a stream, not a request/response call.

## 4. `/api/chat` streaming (`hooks/useChatStream.ts`)

Custom SSE client — **do not** use Vercel AI SDK's `useChat` (schema
mismatch, see `02-technical-spec.md` §0). Implementation: `fetch` with
`POST`, read `response.body` as a `ReadableStream`, parse SSE frames
(`event:`/`data:` lines) manually, and dispatch on the `event` name per the
table in `02-technical-spec.md` §6:
- `meta` → sets the current turn's kind (`scope_refusal`/`triage`/`answer`/
  `escalation`) in local state, used to pick how `MessageBubble` renders it.
- `token` → append `delta` to the in-progress message's text.
- `sources` → attach to the in-progress message once, before `done`.
- `done` → finalize the message (move from "streaming" to "complete" state).
- `error` → render inline via `ErrorBanner`, offer retry; never throw an
  unhandled exception into the component tree.

## 5. Shared components (`components/shared/`)

- `MarkdownRenderer` — thin wrapper around `streamdown` (GFM + Shiki code
  blocks, safe with partial/streaming input). Used by chat message bubbles
  and practice-mode explanations.
- `CitationList` — renders `{title, url, anchor}[]` as a distinct list of
  links (never inline-only) — shared by chat's sources panel, search
  results, and practice-mode per-step source. This is the concrete
  implementation of FR2's "sources as a distinct structured list."
- `FeedbackButtons` — thumbs up/down, calls `postFeedback`, used on chat
  assistant messages.
- **Direction handling**: content is genuinely bilingual per-message (a
  Persian question can get an English-titled doc citation, etc.), so
  direction is **per-element, not a single page-level RTL toggle**. Message
  bubbles and rendered markdown get `dir="auto"` (native browser
  strong-character detection — no custom script-detection code needed for
  content). The input field also gets `dir="auto"`. Static UI chrome (nav
  labels, buttons, placeholders) follows the `locale` context value from
  `SessionProvider` instead, since that text is authored by us, not detected.
  shadcn's CLI RTL support (physical→logical class conversion, see
  `02-technical-spec.md` §0) handles the layout-mirroring part for whichever
  `dir` ends up active on a given element.
- `LoadingDots`, `ErrorBanner` — standard streaming/error affordances.

## 6. Ask Assistant (`app/chat/`, `components/chat/`)

- `ChatView` — page-level: owns the message list state, wires
  `useChatStream`, renders `MessageList` + `ChatInput`.
- `MessageList` / `MessageBubble` — `MessageBubble` renders differently by
  the message's `meta.kind`: a plain answer (markdown + `CitationList` below
  it + `FeedbackButtons`), a triage question (visually distinct — e.g. a
  left accent bar or badge, per `meta.kind === "triage"`, showing
  `triageRound`), a scope refusal (muted/neutral styling, no feedback
  buttons), or an escalation (answer styling plus a highlighted support-link
  callout).
- `ChatInput` — text input + send button + the **"Explain this error"
  entry point** (FR12): a secondary action (button or a paste-target) that
  seeds the input with a framing prefix and submits, feeding straight into
  the same `/api/chat` flow — no separate endpoint, this is purely a UI
  affordance over the existing chat.
- On mount: load history via `getSessionMessages(sessionId)` so a returning
  session resumes (FR5).

## 7. Find in Docs (`app/search/`, `components/search/`)

- `SearchView` — owns query input state, category filter, and results.
- `CategoryFilter` — dropdown fed by `getCategories()`.
- `ResultsList` / `ResultCard` — renders `{title, url, anchor, snippet,
  score, matchedSubQuery?}[]`; when `matchedSubQuery` is present on results
  (decomposed query), group cards under a small sub-query heading so the
  user sees *why* each result matched (per `02-technical-spec.md` §6).
- Out-of-scope response renders the same refusal styling as chat's scope
  refusal (reuse the visual treatment, not the component itself since the
  shapes differ slightly).

## 8. Practice Mode (`app/practice/`, `components/practice/`)

Linear flow, one screen, state machine in local component state:
`topic-input → (clarification loop, 0-2 rounds) → question(step i) →
feedback(step i) → question(step i+1) → ... → summary`.

- `TopicForm` — free-text topic description, calls `postPracticeStart`.
- `ClarificationPrompt` — rendered when the response is
  `status: "needs_clarification"`; same visual treatment as chat's triage
  question (shared styling, not necessarily shared component); resubmits
  the user's answer as a new `postPracticeStart` call (topic scoping is
  stateless across this loop per `02-technical-spec.md` §6a — the backend
  tracks `triageRound` state the same way chat does).
- `ProgressBar` — shadcn `Progress`, "step X of N", from `stepCount` +
  current index.
- `QuestionCard` — the question text + 3 options as a shadcn `RadioGroup`
  (single-select, per the earlier "multiple choice = one answer" design) +
  submit button, disabled until a choice is made.
- `FeedbackPanel` — shown immediately after `postPracticeAnswer` resolves:
  correct/incorrect styling, the `explanation`, and a `CitationList` with
  the one `source`. A "Next question" action advances to `next` (or to
  `SummaryView` if `next` is `null`).
- `SummaryView` — score (`X/N`), and every step recapped (question, the
  user's answer, the correct answer, explanation, source) via
  `getPracticeSummary` — this is the "step-by-step recap with description"
  the product spec asked for.
- **Never send future steps' `correctIndex`/`explanation` ahead of time** —
  the component only ever holds the *current* step's question/options until
  `postPracticeAnswer` reveals the answer, matching the API contract's
  intentional design (`02-technical-spec.md` §6a).

## 9. shadcn components to install

`button`, `input`, `textarea`, `card`, `badge`, `progress`, `radio-group`,
`tabs` (mode switcher in the root layout), `separator`, `skeleton` (loading
states), `sonner` (toast, for transient errors), `scroll-area` (chat/message
list).

## 10. Responsive & accessibility

- Mobile-first: mode switcher (`tabs`) collapses to a bottom bar under
  ~768px; chat input pinned to viewport bottom; message list in a
  `ScrollArea` that takes remaining height.
- Every interactive element (option buttons, feedback thumbs, mode tabs)
  needs a real focus state and keyboard operability — shadcn primitives
  (built on Radix) give this by default, don't override it away.
- "Responsiveness" is graded explicitly (AC2) — a manual pass at mobile
  width is part of Phase 6 QA (`03-plan.md`), not optional polish.

## 11. What not to do

- No Vercel AI SDK (`ai` package) — custom SSE client instead (§4).
- No global RTL toggle — direction is per-element (§5).
- No Redux/Zustand/Jotai — context + hooks is enough at this scope (§2).
- No dynamic route per practice exam — client state is enough (§1).
