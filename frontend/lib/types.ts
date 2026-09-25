// Mirrors the JSON contracts in specs/02-technical-spec.md §6/§6a exactly.

export type Locale = "fa" | "en";

export type Citation = {
  title: string;
  url: string;
  anchor: string | null;
};

// ---------- /api/search ----------

export type SearchResult = Citation & {
  snippet: string;
  score: number;
  category: string;
  matchedSubQuery: string | null;
};

export type SearchResponse =
  | {
      scope: "in_scope";
      subQueries: string[];
      results: SearchResult[];
      tookMs: number;
    }
  | {
      scope: "out_of_scope" | "trivial";
      reason: string;
      message: string;
      results: [];
    };

// ---------- /api/chat (SSE) ----------

export type ChatMetaEvent =
  | { kind: "scope_refusal"; reason: string }
  | { kind: "triage"; triageRound: number }
  | { kind: "answer" }
  | { kind: "escalation" };

export type ChatSource = Citation & { score: number };

export type ChatErrorEvent = { message: string; retryable: boolean };

export type SessionMessage = {
  id: string;
  role: "user" | "assistant";
  content: string;
  sources: ChatSource[] | null;
  createdAt: string;
};

// ---------- Practice Mode (§6a) ----------

export type PracticeStep = {
  index: number;
  question: string;
  options: string[];
};

export type PracticeStartResponse =
  | { status: "out_of_scope"; reason: string; message: string }
  | { status: "needs_clarification"; question: string; triageRound: number }
  | { status: "insufficient_material"; message: string }
  | {
      status: "ready";
      examId: string;
      topic: string;
      stepCount: number;
      step: PracticeStep;
    };

export type PracticeAnswerResponse = {
  isCorrect: boolean;
  correctIndex: number;
  explanation: string;
  source: Citation;
  next: PracticeStep | null;
};

export type PracticeSummaryStep = {
  index: number;
  question: string;
  selectedIndex: number | null;
  correctIndex: number;
  isCorrect: boolean;
  explanation: string;
  source: Citation;
};

export type PracticeSummaryResponse = {
  topic: string;
  score: { correct: number; total: number };
  steps: PracticeSummaryStep[];
};

// ---------- Misc ----------

export type Category = {
  id: string;
  labelFa: string;
  labelEn: string;
};

export type FeedbackVote = "up" | "down";

export type ApiErrorBody = { error: string };
