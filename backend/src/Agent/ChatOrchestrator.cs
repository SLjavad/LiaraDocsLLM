using System.Text.Json;
using Microsoft.Agents.AI;
using LiaraDocsAssistant.Data;
using LiaraDocsAssistant.Data.Entities;
using LiaraDocsAssistant.Data.Redis;
using LiaraDocsAssistant.Retrieval;
using LiaraDocsAssistant.Retrieval.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace LiaraDocsAssistant.Agent;

/// <summary>
/// /api/chat orchestration (specs/02-technical-spec.md §5/§6/§7): creates the
/// session row this system needs (nothing before Phase 4 does — see the
/// try/catch around HybridRetrievalService's doc_gap_events write, which
/// exists precisely because /api/search never creates one), reuses
/// IRouterService as the scope-gate, then runs the agent turn.
///
/// Every turn is resolved to completion internally before returning (no true
/// token-by-token passthrough from the model) so the SSE meta.kind — which
/// must precede any token frames — can be determined correctly first; see
/// AgentOrchestrationNotes (specs/04-prompts.md §1a) for how a
/// clarifying-question turn is distinguished from a real answer without
/// touching the verbatim system prompt.
/// </summary>
public sealed class ChatOrchestrator(
    AppDbContext db,
    IRouterService router,
    IRetrievalService retrieval,
    ChatAgentFactory agentFactory,
    ISpendGuard spendGuard,
    string supportChannelUrl,
    int maxClarifyingRounds,
    int maxHistoryMessages,
    ILogger<ChatOrchestrator> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ChatTurnResult> RunTurnAsync(Guid sessionId, string message, CancellationToken ct)
    {
        var locale = RefusalTemplates.DetectLocale(message);
        var (session, recentMessages) = await PrepareSessionAndUserMessageAsync(sessionId, locale, message, ct);

        var routerResult = await router.ClassifyAsync(new RouterRequest(message, "chat", recentMessages), ct);

        if (routerResult.Scope != RouterResult.ScopeInScope)
        {
            var refusalText = RefusalTemplates.ForScope(routerResult.Scope, routerResult.Reason, locale);
            await PersistAssistantMessageAsync(session, refusalText, sources: null, routerResult.Scope, pendingClarificationJson: null, ct);
            return new ChatTurnResult("scope_refusal", routerResult.Reason, null, refusalText, null);
        }

        var pending = ParsePendingClarification(session.PendingClarification);
        var forceEscalation = pending is not null && pending.RoundsAsked >= maxClarifyingRounds;

        var history = await BuildHistoryAsync(sessionId, ct);
        var tools = new ChatTools(retrieval, sessionId);
        IList<AITool> toolList =
        [
            AIFunctionFactory.Create(tools.SearchDocsAsync),
            AIFunctionFactory.Create(tools.ListCategories),
        ];

        var userProfileNote = session.Profile is null or "{}"
            ? AgentSystemPrompt.NothingKnownAboutUser
            : $"Known about this user so far: {session.Profile}";
        var orchestrationNote = forceEscalation
            ? AgentOrchestrationNotes.ClarifyingCapReachedInstruction
            : AgentOrchestrationNotes.ClarifyMarkerInstruction;
        var instructions = AgentSystemPrompt.Render(supportChannelUrl, userProfileNote) + "\n\n" + orchestrationNote;

        var agent = agentFactory.CreateAgent(instructions, toolList);
        var response = await agent.RunAsync(history, session: null, options: null, ct);
        await RecordChatSpendAsync(response, ct);

        var rawText = response.Text ?? string.Empty;
        var trimmedText = rawText.TrimStart();
        var hasClarifyMarker = trimmedText.StartsWith(AgentOrchestrationNotes.ClarifyMarker, StringComparison.Ordinal);
        // Strip the marker whenever present, even in forceEscalation mode — the
        // model was told not to emit it there, but a model that ignores that
        // instruction must never leak the raw orchestration marker to the user.
        var text = hasClarifyMarker
            ? trimmedText[AgentOrchestrationNotes.ClarifyMarker.Length..].TrimStart()
            : rawText;
        var isClarifying = !forceEscalation && hasClarifyMarker;

        var collectedSources = SearchMerger.Merge([("", tools.CollectedSources)])
            .Select(r => new ChatSourceDto(r.Title, r.Url, r.Anchor, Math.Round(r.Score, 4)))
            .ToList();

        if (forceEscalation)
        {
            // Deterministic, never model-generated (same discipline as the
            // router's static refusal templates) — the model was told not to
            // write its own support-channel pointer for this forced case.
            var escalationLine = RefusalTemplates.Pick(locale, RefusalTemplates.EscalationEn, RefusalTemplates.EscalationFa)
                .Replace("{{support_channel_url}}", supportChannelUrl);
            text = $"{text}\n\n{escalationLine}";
        }

        if (isClarifying)
        {
            var nextRound = (pending?.RoundsAsked ?? 0) + 1;
            var originalQuery = pending?.OriginalQuery ?? message;
            var pendingJson = JsonSerializer.Serialize(new PendingClarificationState(originalQuery, nextRound), JsonOptions);
            await PersistAssistantMessageAsync(session, text, sources: null, routerResult.Scope, pendingJson, ct);
            return new ChatTurnResult("triage", null, nextRound, text, null);
        }

        await PersistAssistantMessageAsync(session, text, collectedSources, routerResult.Scope, pendingClarificationJson: null, ct);

        return forceEscalation
            ? new ChatTurnResult("escalation", null, null, text, null)
            : new ChatTurnResult("answer", null, null, text, collectedSources);
    }

    /// <summary>
    /// Reads recentMessages (last 4 prior turns) BEFORE inserting the current
    /// user message, so the message isn't counted twice when it's later passed
    /// separately as RouterRequest.Message. Upserts the session and inserts the
    /// user message in one round trip; on a client-supplied sessionId, two
    /// concurrent first-messages for the same brand-new session (double
    /// submit, two tabs) can race on the session row's primary key — retried
    /// once against whichever request's insert actually won.
    /// </summary>
    private async Task<(Session Session, List<string> RecentMessages)> PrepareSessionAndUserMessageAsync(
        Guid sessionId, string locale, string message, CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        var isNewSession = session is null;
        session ??= new Session { Id = sessionId };

        var recentMessages = await db.Messages.AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(4)
            .Select(m => m.Content)
            .ToListAsync(ct);
        recentMessages.Reverse();

        if (isNewSession)
        {
            db.Sessions.Add(session);
        }
        session.Locale = locale;
        session.LastActiveAt = DateTimeOffset.UtcNow;
        db.Messages.Add(new Message { SessionId = sessionId, Role = "user", Content = message });

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

        return (session, recentMessages);
    }

    private async Task<IReadOnlyList<ChatMessage>> BuildHistoryAsync(Guid sessionId, CancellationToken ct)
    {
        var rows = await db.Messages.AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(maxHistoryMessages)
            .ToListAsync(ct);
        rows.Reverse();

        return [.. rows.Select(m => new ChatMessage(
            m.Role == "assistant" ? ChatRole.Assistant : ChatRole.User,
            m.Content))];
    }

    private async Task PersistAssistantMessageAsync(
        Session session,
        string content,
        IReadOnlyList<ChatSourceDto>? sources,
        string routerScope,
        string? pendingClarificationJson,
        CancellationToken ct)
    {
        session.PendingClarification = pendingClarificationJson;
        db.Messages.Add(new Message
        {
            SessionId = session.Id,
            Role = "assistant",
            Content = content,
            Sources = sources is { Count: > 0 } ? JsonSerializer.Serialize(sources, JsonOptions) : null,
            RouterScope = routerScope,
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task RecordChatSpendAsync(AgentResponse response, CancellationToken ct)
    {
        var totalTokens = response.Usage?.TotalTokenCount;
        if (totalTokens is > 0)
        {
            try
            {
                await spendGuard.RecordAsync("chat", (int)totalTokens.Value, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to record chat spend");
            }
        }
    }

    private static PendingClarificationState? ParsePendingClarification(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<PendingClarificationState>(json, JsonOptions);

    private sealed record PendingClarificationState(string OriginalQuery, int RoundsAsked);
}
