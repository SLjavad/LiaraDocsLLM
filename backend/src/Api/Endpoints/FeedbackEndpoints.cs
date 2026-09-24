using System.Text.Json.Serialization;
using LiaraDocsAssistant.Data;
using LiaraDocsAssistant.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LiaraDocsAssistant.Api.Endpoints;

public sealed record FeedbackRequest(
    [property: JsonPropertyName("messageId")] Guid MessageId,
    [property: JsonPropertyName("vote")] string? Vote);

public static class FeedbackEndpoints
{
    public static void MapFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/feedback", async (FeedbackRequest request, AppDbContext db, CancellationToken ct) =>
        {
            if (request.Vote is not ("up" or "down"))
            {
                return Results.Json(new { error = "vote must be 'up' or 'down'" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == request.MessageId, ct);
            if (message is null)
            {
                return Results.Json(new { error = "messageId not found" }, statusCode: StatusCodes.Status404NotFound);
            }
            if (message.Role != "assistant")
            {
                return Results.Json(new { error = "feedback can only be given on an assistant message" }, statusCode: StatusCodes.Status400BadRequest);
            }

            db.MessageFeedback.Add(new MessageFeedback { MessageId = request.MessageId, Vote = request.Vote });

            if (request.Vote == "down")
            {
                var precedingQuestion = await db.Messages.AsNoTracking()
                    .Where(m => m.SessionId == message.SessionId && m.Role == "user" && m.CreatedAt < message.CreatedAt)
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => m.Content)
                    .FirstOrDefaultAsync(ct);

                db.DocGapEvents.Add(new DocGapEvent
                {
                    Query = precedingQuestion ?? message.Content,
                    Mode = "chat",
                    SessionId = message.SessionId,
                });
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        });
    }
}
