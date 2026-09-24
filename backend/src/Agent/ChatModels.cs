namespace LiaraDocsAssistant.Agent;

public sealed record ChatSourceDto(string Title, string Url, string? Anchor, double Score);

/// <summary>
/// One /api/chat turn's outcome, already fully resolved (see the
/// buffer-then-simulate-stream note on ChatOrchestrator) — the endpoint layer
/// only has to translate this into the SSE event sequence from
/// specs/02-technical-spec.md §6, it makes no further decisions.
/// </summary>
public sealed record ChatTurnResult(
    string Kind, // "scope_refusal" | "triage" | "answer" | "escalation"
    string? Reason, // scope_refusal only
    int? TriageRound, // triage only
    string Text,
    IReadOnlyList<ChatSourceDto>? Sources); // non-null only when Kind == "answer"
