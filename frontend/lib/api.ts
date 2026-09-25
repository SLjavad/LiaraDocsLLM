import type {
  ApiErrorBody,
  Category,
  FeedbackVote,
  Locale,
  PracticeAnswerResponse,
  PracticeStartResponse,
  PracticeSummaryResponse,
  SearchResponse,
  SessionMessage,
} from "./types";

const API_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5080";

export class ApiError extends Error {
  status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

async function request<T>(
  path: string,
  init: RequestInit & { sessionId?: string | null } = {},
): Promise<T> {
  const { sessionId, headers, ...rest } = init;
  const res = await fetch(`${API_BASE_URL}${path}`, {
    ...rest,
    headers: {
      "Content-Type": "application/json",
      ...(sessionId ? { "X-Session-Id": sessionId } : {}),
      ...headers,
    },
  });

  if (!res.ok) {
    const body = (await res.json().catch(() => null)) as ApiErrorBody | null;
    throw new ApiError(res.status, body?.error ?? `Request failed with status ${res.status}`);
  }

  return res.json() as Promise<T>;
}

export function postSearch(
  sessionId: string | null,
  query: string,
  category: string | null,
  locale: Locale,
): Promise<SearchResponse> {
  return request<SearchResponse>("/api/search", {
    method: "POST",
    sessionId,
    body: JSON.stringify({ sessionId, query, category, platform: null, locale }),
  });
}

export function getCategories(): Promise<{ categories: Category[] }> {
  return request("/api/categories");
}

export function getSessionMessages(sessionId: string): Promise<{ messages: SessionMessage[] }> {
  return request(`/api/sessions/${sessionId}/messages`, { sessionId });
}

export function postFeedback(
  sessionId: string,
  messageId: string,
  vote: FeedbackVote,
): Promise<{ ok: boolean }> {
  return request("/api/feedback", {
    method: "POST",
    sessionId,
    body: JSON.stringify({ messageId, vote }),
  });
}

export function postPracticeStart(
  sessionId: string,
  description: string,
): Promise<PracticeStartResponse> {
  return request("/api/practice/start", {
    method: "POST",
    sessionId,
    body: JSON.stringify({ sessionId, description }),
  });
}

export function postPracticeAnswer(
  sessionId: string,
  examId: string,
  stepIndex: number,
  selectedIndex: number,
): Promise<PracticeAnswerResponse> {
  return request(`/api/practice/${examId}/answer`, {
    method: "POST",
    sessionId,
    body: JSON.stringify({ stepIndex, selectedIndex }),
  });
}

export function getPracticeSummary(
  sessionId: string,
  examId: string,
): Promise<PracticeSummaryResponse> {
  return request(`/api/practice/${examId}/summary`, { sessionId });
}

export { API_BASE_URL };
