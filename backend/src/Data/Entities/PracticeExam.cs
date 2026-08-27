namespace LiaraDocsAssistant.Data.Entities;

public sealed class PracticeExam
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int CurrentStepIndex { get; set; }
    public string Steps { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public Session? Session { get; set; }
}
