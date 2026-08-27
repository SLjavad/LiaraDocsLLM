namespace LiaraDocsAssistant.Data.Entities;

public sealed class Session
{
    public Guid Id { get; set; }
    public string? Locale { get; set; }
    public string Profile { get; set; } = "{}";
    public string? PendingClarification { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastActiveAt { get; set; }
}
