using System.Text.Json;
using LiaraDocsAssistant.Data;
using LiaraDocsAssistant.Data.Entities;
using LiaraDocsAssistant.Retrieval;
using LiaraDocsAssistant.Retrieval.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LiaraDocsAssistant.Agent;

/// <summary>
/// Practice Mode orchestration (specs/02-technical-spec.md §6a,
/// specs/03-plan.md Phase 4b): reuses IRouterService (mode "practice") for
/// the scope-gate exactly like chat, reuses sessions.pending_clarification
/// for topic-narrowing rounds (mode "practice" — see PendingClarificationState),
/// plans the whole quiz upfront via IPracticeTopicScopingService +
/// per-sub-topic IRetrievalService groundedness checks + IExamGenerationService
/// in one batched call, then persists the plan. Answering and summarizing are
/// pure reads/index comparisons against the persisted plan — no LLM calls
/// (NFR16).
/// </summary>
public sealed class PracticeService(
    AppDbContext db,
    IRouterService router,
    IRetrievalService retrieval,
    IPracticeTopicScopingService scopingService,
    IExamGenerationService examGeneration,
    int minSteps,
    int maxSteps,
    int maxClarifyingRounds,
    double groundednessThreshold,
    ILogger<PracticeService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PracticeStartResult> StartAsync(Guid sessionId, string description, CancellationToken ct)
    {
        var locale = RefusalTemplates.DetectLocale(description);
        var session = await SessionStore.UpsertAsync(db, sessionId, locale, ct);

        var pending = SessionStore.ParsePendingClarification(session.PendingClarification, PendingClarificationState.ModePractice);
        var recentContext = pending is null ? null : new List<string> { pending.OriginalQuery, description };
        var effectiveTopic = pending is null ? description : $"{pending.OriginalQuery} — {description}";

        var routerResult = await router.ClassifyAsync(new RouterRequest(effectiveTopic, "practice"), ct);
        if (routerResult.Scope != RouterResult.ScopeInScope)
        {
            if (pending is not null)
            {
                session.PendingClarification = null;
                await db.SaveChangesAsync(ct);
            }
            return PracticeStartResult.OutOfScope(
                routerResult.Reason, RefusalTemplates.ForScope(routerResult.Scope, routerResult.Reason, locale));
        }

        var forceScoped = pending is not null && pending.RoundsAsked >= maxClarifyingRounds;
        var scoping = await scopingService.ScopeTopicAsync(effectiveTopic, minSteps, maxSteps, recentContext, forceScoped, ct);

        if (forceScoped && !scoping.Scoped)
        {
            logger.LogWarning("Practice topic-scoping ignored the cap-reached instruction; forcing scoped=true for session {SessionId}", sessionId);
            scoping = scoping with
            {
                Scoped = true,
                RefinedTopic = scoping.RefinedTopic ?? effectiveTopic,
                SubTopics = scoping.SubTopics.Count > 0 ? scoping.SubTopics : [effectiveTopic],
            };
        }

        if (!scoping.Scoped)
        {
            var nextRound = (pending?.RoundsAsked ?? 0) + 1;
            var originalQuery = pending?.OriginalQuery ?? description;
            session.PendingClarification = SessionStore.SerializePendingClarification(
                new PendingClarificationState(PendingClarificationState.ModePractice, originalQuery, nextRound));
            await db.SaveChangesAsync(ct);
            return PracticeStartResult.NeedsClarification(
                scoping.ClarifyingQuestion ?? "Could you narrow the topic down a bit?", nextRound);
        }

        // Cleared in-memory only (not saved) — a session mode owns this slot
        // only if it was the one that set it; rides along with whichever
        // save happens further down this path.
        if (pending is not null)
        {
            session.PendingClarification = null;
        }

        var topic = scoping.RefinedTopic ?? effectiveTopic;
        var subTopics = scoping.SubTopics.Take(maxSteps).ToList();

        var groundedChunks = new List<ExamGenerationChunk>();
        foreach (var subTopic in subTopics)
        {
            var results = await retrieval.SearchAsync(subTopic, mode: "practice", sessionId: sessionId, ct: ct);
            // Below-threshold drops are already logged to doc_gap_events by
            // IRetrievalService itself (mode "practice") — nothing extra to log here.
            if (results.Count == 0 || results[0].Score < groundednessThreshold)
            {
                continue;
            }
            var best = results[0];
            groundedChunks.Add(new ExamGenerationChunk(groundedChunks.Count, subTopic, best.Title, best.Url, best.Anchor, best.Body));
        }

        if (groundedChunks.Count < minSteps)
        {
            await db.SaveChangesAsync(ct); // flushes the pending-clarification clear above, if any
            return PracticeStartResult.InsufficientMaterial(
                "This topic doesn't have enough documented material yet for a good quiz — try Find-in-docs, or a narrower topic.");
        }

        var generated = await examGeneration.GenerateAsync(topic, groundedChunks, ct);

        var finalSteps = new List<StoredPracticeStep>();
        foreach (var stepPayload in generated)
        {
            var chunk = groundedChunks.FirstOrDefault(c => c.Index == stepPayload.SourceChunkIndex);
            if (chunk is null)
            {
                // Defensive: model referenced a chunk index we never sent
                // (hallucinated index). Still worth a doc_gap_events row —
                // otherwise a grounded, above-threshold chunk silently
                // produces no question with no diagnostic trail at all.
                db.DocGapEvents.Add(new DocGapEvent
                {
                    Query = $"(exam-generation referenced unknown sourceChunkIndex {stepPayload.SourceChunkIndex} for topic \"{topic}\")",
                    Mode = "practice",
                    SessionId = sessionId,
                });
                continue;
            }

            var isUsable = !stepPayload.Skip
                && !string.IsNullOrWhiteSpace(stepPayload.Question)
                && stepPayload.Options is { Count: 3 }
                && stepPayload.Options.All(o => !string.IsNullOrWhiteSpace(o))
                && stepPayload.CorrectIndex is >= 0 and <= 2
                && !string.IsNullOrWhiteSpace(stepPayload.Explanation);

            if (!isUsable)
            {
                db.DocGapEvents.Add(new DocGapEvent
                {
                    Query = chunk.SubTopic,
                    Mode = "practice",
                    SessionId = sessionId,
                });
                continue;
            }

            var (shuffledOptions, shuffledCorrectIndex) = ShuffleOptions(stepPayload.Options!, stepPayload.CorrectIndex!.Value);
            finalSteps.Add(new StoredPracticeStep(
                finalSteps.Count,
                stepPayload.Question!,
                shuffledOptions,
                shuffledCorrectIndex,
                stepPayload.Explanation!,
                new PracticeSourceDto(chunk.Title, chunk.Url, chunk.Anchor)));
        }

        if (finalSteps.Count < minSteps)
        {
            await db.SaveChangesAsync(ct); // persist any doc_gap_events recorded above even though we're bailing out
            return PracticeStartResult.InsufficientMaterial(
                "This topic doesn't have enough documented material yet for a good quiz — try Find-in-docs, or a narrower topic.");
        }

        var exam = new PracticeExam
        {
            SessionId = sessionId,
            Topic = topic,
            Status = "planned",
            CurrentStepIndex = 0,
            Steps = JsonSerializer.Serialize(finalSteps, JsonOptions),
        };
        db.PracticeExams.Add(exam);
        await db.SaveChangesAsync(ct);

        var firstStep = finalSteps[0];
        return PracticeStartResult.Ready(
            exam.Id, topic, finalSteps.Count, new PracticeStepPublicDto(firstStep.Index, firstStep.Question, firstStep.Options));
    }

    public async Task<PracticeAnswerOutcome> AnswerAsync(Guid examId, int stepIndex, int selectedIndex, CancellationToken ct)
    {
        var exam = await db.PracticeExams.FirstOrDefaultAsync(e => e.Id == examId, ct);
        if (exam is null)
        {
            return new PracticeAnswerOutcome(PracticeAnswerOutcome.StatusNotFound, null);
        }
        if (stepIndex != exam.CurrentStepIndex)
        {
            return new PracticeAnswerOutcome(PracticeAnswerOutcome.StatusWrongStep, null);
        }

        var alreadyAnswered = await db.PracticeExamAnswers.AnyAsync(a => a.ExamId == examId && a.StepIndex == stepIndex, ct);
        if (alreadyAnswered)
        {
            return new PracticeAnswerOutcome(PracticeAnswerOutcome.StatusAlreadyAnswered, null);
        }

        var steps = JsonSerializer.Deserialize<List<StoredPracticeStep>>(exam.Steps, JsonOptions)!;
        var step = steps[stepIndex];
        var isCorrect = selectedIndex == step.CorrectIndex;

        db.PracticeExamAnswers.Add(new PracticeExamAnswer
        {
            ExamId = examId,
            StepIndex = stepIndex,
            SelectedIndex = selectedIndex,
            IsCorrect = isCorrect,
        });

        exam.CurrentStepIndex = stepIndex + 1;
        var isLastStep = exam.CurrentStepIndex >= steps.Count;
        exam.Status = isLastStep ? "completed" : "in_progress";
        if (isLastStep)
        {
            exam.CompletedAt = DateTimeOffset.UtcNow;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (!ct.IsCancellationRequested)
        {
            // A concurrent duplicate submission for the same (examId, stepIndex)
            // raced past the alreadyAnswered check above and lost the DB's
            // unique-constraint race — same outcome as if we'd caught it earlier.
            return new PracticeAnswerOutcome(PracticeAnswerOutcome.StatusAlreadyAnswered, null);
        }

        var next = isLastStep
            ? null
            : new PracticeStepPublicDto(steps[exam.CurrentStepIndex].Index, steps[exam.CurrentStepIndex].Question, steps[exam.CurrentStepIndex].Options);

        return new PracticeAnswerOutcome(
            PracticeAnswerOutcome.StatusOk,
            new PracticeAnswerResult(isCorrect, step.CorrectIndex, step.Explanation, step.Source, next));
    }

    public async Task<PracticeSummaryResult?> GetSummaryAsync(Guid examId, CancellationToken ct)
    {
        var exam = await db.PracticeExams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId, ct);
        if (exam is null)
        {
            return null;
        }

        var steps = JsonSerializer.Deserialize<List<StoredPracticeStep>>(exam.Steps, JsonOptions)!;
        var answers = await db.PracticeExamAnswers.AsNoTracking()
            .Where(a => a.ExamId == examId)
            .ToListAsync(ct);
        var answersByStep = answers.ToDictionary(a => a.StepIndex);

        var summarySteps = steps.Select(s =>
        {
            answersByStep.TryGetValue(s.Index, out var answer);
            return new PracticeSummaryStepDto(s.Index, s.Question, answer?.SelectedIndex, s.CorrectIndex, answer?.IsCorrect ?? false, s.Explanation, s.Source);
        }).ToList();

        return new PracticeSummaryResult(exam.Topic, answers.Count(a => a.IsCorrect), steps.Count, summarySteps);
    }

    private static (List<string> Options, int CorrectIndex) ShuffleOptions(IReadOnlyList<string> options, int correctIndex)
    {
        var order = Enumerable.Range(0, options.Count).ToList();
        for (var i = order.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        return ([.. order.Select(i => options[i])], order.IndexOf(correctIndex));
    }
}
