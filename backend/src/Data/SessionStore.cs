using System.Text.Json;
using LiaraDocsAssistant.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LiaraDocsAssistant.Data;

/// <summary>
/// sessions.pending_clarification shape, shared by chat triage
/// (specs/02-technical-spec.md §5) and Practice Mode topic-scoping
/// (03-plan.md Phase 4b, "reuse the triage strategy... same 2-round cap") —
/// both flows share the one column on the same session row, so Mode
/// disambiguates which flow a stored round belongs to; a chat session
/// mid-triage shouldn't be misread as a practice narrowing round or vice
/// versa if a session interleaves both in the same window.
/// </summary>
public sealed record PendingClarificationState(string Mode, string OriginalQuery, int RoundsAsked)
{
    public const string ModeChat = "chat";
    public const string ModePractice = "practice";
}

/// <summary>
/// Session upsert shared by /api/chat and /api/practice/start — both create
/// the session row this system needs (nothing before Phase 4 did). Session
/// ids are client-supplied, not DB-generated, so two concurrent first-turns
/// for the same brand-new session (double submit, two tabs) can race on the
/// primary key; retried once against whichever request's insert won.
/// </summary>
public static class SessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<Session> UpsertAsync(AppDbContext db, Guid sessionId, string locale, CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        var isNewSession = session is null;
        session ??= new Session { Id = sessionId };

        if (isNewSession)
        {
            db.Sessions.Add(session);
        }
        session.Locale = locale;
        session.LastActiveAt = DateTimeOffset.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (isNewSession && !ct.IsCancellationRequested)
        {
            db.Entry(session).State = EntityState.Detached;
            session = await db.Sessions.SingleAsync(s => s.Id == sessionId, ct);
            session.Locale = locale;
            session.LastActiveAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return session;
    }

    public static PendingClarificationState? ParsePendingClarification(string? json, string mode)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        var state = JsonSerializer.Deserialize<PendingClarificationState>(json, JsonOptions);
        return state?.Mode == mode ? state : null;
    }

    public static string SerializePendingClarification(PendingClarificationState state) =>
        JsonSerializer.Serialize(state, JsonOptions);
}
