namespace LiaraDocsAssistant.Data.Entities;

public sealed class MessageFeedback
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public string Vote { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public Message? Message { get; set; }
}
