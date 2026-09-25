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
    int maxSourcesInAnswer,
    ILogger<ChatOrchestrator> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ChatTurnResult> RunTurnAsync(Guid sessionId, string message, CancellationToken ct)
    {
        var locale = RefusalTemplates.DetectLocale(message);
        var session = await SessionStore.UpsertAsync(db, sessionId, locale, ct);

        var recentMessages = await db.Messages.AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(4)
            .Select(m => m.Content)
            .ToListAsync(ct);
        recentMessages.Reverse();

        // A session's pending_clarification is shared with Practice Mode
        // topic-scoping (see PendingClarificationState) — only ever act on it
        // here if it's actually ours; a stray practice-mode round must never
        // be read, cleared, or clobbered by the chat flow.
        var pending = SessionStore.ParsePendingClarification(session.PendingClarification, PendingClarificationState.ModeChat);

        // Tracked now, not saved yet — rides along with whichever save below
        // ends up persisting this turn's outcome. SaveChangesAsync with no
        // pending changes is a free no-op, so every exit path below can just
        // call it once without having to reason about who "owns" the flush.
        db.Messages.Add(new Message { SessionId = sessionId, Role = "user", Content = message });

        var routerResult = await router.ClassifyAsync(new RouterRequest(message, "chat", recentMessages), ct);

        if (routerResult.Scope != RouterResult.ScopeInScope)
        {
            var refusalText = RefusalTemplates.ForScope(routerResult.Scope, routerResult.Reason, locale);
            if (pending is not null)
            {
                session.PendingClarification = null;
            }
            await PersistAssistantMessageAsync(session, refusalText, sources: null, routerResult.Scope, ct);
            return new ChatTurnResult("scope_refusal", routerResult.Reason, null, refusalText, null);
        }

        var forceEscalation = pending is not null && pending.RoundsAsked >= maxClarifyingRounds;

        // BuildHistoryAsync reads only what's already persisted (prior turns);
        // the current message is appended in-memory since it hasn't been
        // saved yet (see the deferred Add above).
        var history = new List<ChatMessage>(await BuildHistoryAsync(sessionId, ct))
        {
            new(ChatRole.User, message),
        };
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

        // AgentResponse.Text concatenates text from EVERY message the run
        // produced, including intermediate tool-planning narration between
        // search_docs calls ("Let me try a couple more phrasings...") — only
        // the final assistant message is the actual answer to show the user.
        var rawText = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant)?.Text ?? string.Empty;
        var trimmedText = rawText.TrimStart();
        var hasClarifyMarker = trimmedText.StartsWith(AgentOrchestrationNotes.ClarifyMarker, StringComparison.Ordinal);
        // Strip the marker whenever present, even in forceEscalation mode — the
        // model was told not to emit it there, but a model that ignores that
        // instruction must never leak the raw orchestration marker to the user.
        var text = hasClarifyMarker
            ? trimmedText[AgentOrchestrationNotes.ClarifyMarker.Length..].TrimStart()
            : rawText;
        var isClarifying = !forceEscalation && hasClarifyMarker;

        // A multi-hop turn can call search_docs many times over several
        // rephrasings before landing on a good answer; without a cap the
        // merged, deduped set can still run into dozens of low-relevance
        // results. Capped to the same top-K the spec already uses for one
        // retrieval call, not a new magic number.
        var collectedSources = SearchMerger.Merge([("", tools.CollectedSources)])
            .Take(maxSourcesInAnswer)
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
            session.PendingClarification = SessionStore.SerializePendingClarification(
                new PendingClarificationState(PendingClarificationState.ModeChat, originalQuery, nextRound));
            await PersistAssistantMessageAsync(session, text, sources: null, routerResult.Scope, ct);
            return new ChatTurnResult("triage", null, nextRound, text, null);
        }

        if (pending is not null)
        {
            session.PendingClarification = null;
        }
        await PersistAssistantMessageAsync(session, text, collectedSources, routerResult.Scope, ct);

        return forceEscalation
            ? new ChatTurnResult("escalation", null, null, text, null)
            : new ChatTurnResult("answer", null, null, text, collectedSources);
    }

    private async Task<IReadOnlyList<ChatMessage>> BuildHistoryAsync(Guid sessionId, CancellationToken ct)
    {
        // maxHistoryMessages - 1: the caller appends the current (not yet
        // persisted) user message on top of this, keeping the total bound
        // the same as before.
        var take = Math.Max(0, maxHistoryMessages - 1);
        var rows = await db.Messages.AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(take)
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
        CancellationToken ct)
    {
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
}
