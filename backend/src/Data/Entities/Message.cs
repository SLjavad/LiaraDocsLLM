namespace LiaraDocsAssistant.Data.Entities;

public sealed class Message
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Sources { get; set; }
    public string? RouterScope { get; set; }
    public int? TokensIn { get; set; }
    public int? TokensOut { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Session? Session { get; set; }
}
