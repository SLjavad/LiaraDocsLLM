namespace LiaraDocsAssistant.Data.Entities;

public sealed class DocGapEvent
{
    public Guid Id { get; set; }
    public string Query { get; set; } = string.Empty;
    public float? BestScore { get; set; }
    public string? CategoryGuess { get; set; }
    public string Mode { get; set; } = string.Empty;
    public Guid? SessionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
