namespace LiaraDocsAssistant.Agent;

public sealed record PracticeSourceDto(string Title, string Url, string? Anchor);

public sealed record PracticeStepPublicDto(int Index, string Question, IReadOnlyList<string> Options);

/// <summary>
/// One /api/practice/start outcome, covering all four statuses in
/// specs/02-technical-spec.md §6a — the endpoint layer just projects
/// whichever fields apply for Status into the response JSON.
/// </summary>
public sealed record PracticeStartResult(
    string Status, // "out_of_scope" | "needs_clarification" | "insufficient_material" | "ready"
    string? Reason,
    string? Message,
    int? TriageRound,
    Guid? ExamId,
    string? Topic,
    int? StepCount,
    PracticeStepPublicDto? Step)
{
    public static PracticeStartResult OutOfScope(string? reason, string message) =>
        new("out_of_scope", reason, message, null, null, null, null, null);

    public static PracticeStartResult NeedsClarification(string question, int triageRound) =>
        new("needs_clarification", null, question, triageRound, null, null, null, null);

    public static PracticeStartResult InsufficientMaterial(string message) =>
        new("insufficient_material", null, message, null, null, null, null, null);

    public static PracticeStartResult Ready(Guid examId, string topic, int stepCount, PracticeStepPublicDto step) =>
        new("ready", null, null, null, examId, topic, stepCount, step);
}

public sealed record PracticeAnswerResult(bool IsCorrect, int CorrectIndex, string Explanation, PracticeSourceDto Source, PracticeStepPublicDto? Next);

/// <summary>Discriminates the request-validation outcomes the endpoint maps to 404/400.</summary>
public sealed record PracticeAnswerOutcome(string Status, PracticeAnswerResult? Result)
{
    public const string StatusOk = "ok";
    public const string StatusNotFound = "not_found";
    public const string StatusWrongStep = "wrong_step";
    public const string StatusAlreadyAnswered = "already_answered";
}

public sealed record PracticeSummaryStepDto(
    int Index, string Question, int? SelectedIndex, int CorrectIndex, bool IsCorrect, string Explanation, PracticeSourceDto Source);

public sealed record PracticeSummaryResult(string Topic, int Correct, int Total, IReadOnlyList<PracticeSummaryStepDto> Steps);

/// <summary>Internal practice_exams.steps shape — never sent to the client whole (correctIndex/explanation/source for future steps must stay server-side).</summary>
public sealed record StoredPracticeStep(
    int Index, string Question, IReadOnlyList<string> Options, int CorrectIndex, string Explanation, PracticeSourceDto Source);
