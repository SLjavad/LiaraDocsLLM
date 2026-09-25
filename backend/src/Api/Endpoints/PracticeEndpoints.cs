using System.Text.Json.Serialization;
using LiaraDocsAssistant.Agent;

namespace LiaraDocsAssistant.Api.Endpoints;

public sealed record PracticeStartRequest(
    [property: JsonPropertyName("sessionId")] Guid? SessionId,
    [property: JsonPropertyName("description")] string? Description);

public sealed record PracticeAnswerRequest(
    [property: JsonPropertyName("stepIndex")] int StepIndex,
    [property: JsonPropertyName("selectedIndex")] int SelectedIndex);

public static class PracticeEndpoints
{
    public static void MapPracticeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/practice/start", async (
            PracticeStartRequest request, PracticeService practice, CancellationToken ct) =>
        {
            if (request.SessionId is not { } sessionId || sessionId == Guid.Empty)
            {
                return Results.Json(new { error = "sessionId is required" }, statusCode: StatusCodes.Status400BadRequest);
            }
            if (string.IsNullOrWhiteSpace(request.Description))
            {
                return Results.Json(new { error = "description is required" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await practice.StartAsync(sessionId, request.Description.Trim(), ct);

            object body = result.Status switch
            {
                "out_of_scope" => new { status = result.Status, reason = result.Reason, message = result.Message },
                "needs_clarification" => new { status = result.Status, question = result.Message, triageRound = result.TriageRound },
                "insufficient_material" => new { status = result.Status, message = result.Message },
                _ => new
                {
                    status = result.Status,
                    examId = result.ExamId,
                    topic = result.Topic,
                    stepCount = result.StepCount,
                    step = new { index = result.Step!.Index, question = result.Step.Question, options = result.Step.Options },
                },
            };
            return Results.Ok(body);
        });

        app.MapPost("/api/practice/{examId:guid}/answer", async (
            Guid examId, PracticeAnswerRequest request, PracticeService practice, CancellationToken ct) =>
        {
            var outcome = await practice.AnswerAsync(examId, request.StepIndex, request.SelectedIndex, ct);

            return outcome.Status switch
            {
                PracticeAnswerOutcome.StatusNotFound =>
                    Results.Json(new { error = "examId not found" }, statusCode: StatusCodes.Status404NotFound),
                PracticeAnswerOutcome.StatusWrongStep =>
                    Results.Json(new { error = "stepIndex does not match the exam's current step" }, statusCode: StatusCodes.Status400BadRequest),
                PracticeAnswerOutcome.StatusAlreadyAnswered =>
                    Results.Json(new { error = "this step has already been answered" }, statusCode: StatusCodes.Status400BadRequest),
                _ => Results.Ok(new
                {
                    isCorrect = outcome.Result!.IsCorrect,
                    correctIndex = outcome.Result.CorrectIndex,
                    explanation = outcome.Result.Explanation,
                    source = outcome.Result.Source,
                    next = outcome.Result.Next is { } next ? new { index = next.Index, question = next.Question, options = next.Options } : null,
                }),
            };
        });

        app.MapGet("/api/practice/{examId:guid}/summary", async (
            Guid examId, PracticeService practice, CancellationToken ct) =>
        {
            var summary = await practice.GetSummaryAsync(examId, ct);
            if (summary is null)
            {
                return Results.Json(new { error = "examId not found" }, statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Ok(new
            {
                topic = summary.Topic,
                score = new { correct = summary.Correct, total = summary.Total },
                steps = summary.Steps.Select(s => new
                {
                    index = s.Index,
                    question = s.Question,
                    selectedIndex = s.SelectedIndex,
                    correctIndex = s.CorrectIndex,
                    isCorrect = s.IsCorrect,
                    explanation = s.Explanation,
                    source = s.Source,
                }),
            });
        });
    }
}
