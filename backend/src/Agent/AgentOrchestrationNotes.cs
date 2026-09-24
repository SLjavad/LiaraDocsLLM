namespace LiaraDocsAssistant.Agent;

/// <summary>
/// Literal chat orchestration notes from specs/04-prompts.md §1a — content,
/// not code to author; do not edit wording here (tech-lead edits go to
/// 04-prompts.md). Appended to the verbatim AgentSystemPrompt, never in place
/// of it; exactly one of the two is appended per turn.
/// </summary>
public static class AgentOrchestrationNotes
{
    public const string ClarifyMarker = "[[CLARIFY]]";

    public const string ClarifyMarkerInstruction =
        """
        ORCHESTRATION NOTE (not shown to the user): if your reply to this turn is
        a clarifying question per the TRIAGE policy above (not a substantive
        answer), begin your reply with the exact literal text "[[CLARIFY]]"
        and nothing before it. Do not include this marker for any other kind of
        reply.
        """;

    public const string ClarifyingCapReachedInstruction =
        """
        ORCHESTRATION NOTE (not shown to the user): the clarifying-question limit
        for this issue has been reached. Do not ask another clarifying question.
        Give your best-effort answer using whatever is already known, and clearly
        label any assumptions you had to make. Do not write your own pointer to
        Liara's support channel — the backend appends the standard escalation
        line after your answer automatically.
        """;
}
