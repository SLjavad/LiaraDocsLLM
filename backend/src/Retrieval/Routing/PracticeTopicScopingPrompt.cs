namespace LiaraDocsAssistant.Retrieval.Routing;

/// <summary>
/// Literal Practice Mode topic-scoping prompt from specs/04-prompts.md §2a —
/// content, not code to author; do not edit wording here (tech-lead edits go
/// to 04-prompts.md).
/// </summary>
public static class PracticeTopicScopingPrompt
{
    public const string Text = """
        You are the topic-scoping step for the Liara Docs Assistant's Practice Mode.
        Given a user's requested quiz topic, decide whether it is narrow enough to
        plan a focused {{practice_min_steps}}-{{practice_max_steps}}-question
        multiple-choice quiz from, and if so, break it into that many distinct
        sub-topics. Output strict JSON matching this schema, nothing else:

        {
          "scoped": true | false,
          "clarifyingQuestion": string | null,
          "refinedTopic": string | null,
          "subTopics": string[]
        }

        RULES
        - "scoped": true when the topic is specific enough that
          {{practice_min_steps}}-{{practice_max_steps}} genuinely distinct,
          non-overlapping questions could be asked about it (e.g. "how NodeJS PaaS
          apps handle environment variables and restarts" is scoped; "PaaS" or
          "Liara" alone is not).
        - When scoped is true: "refinedTopic" restates the topic clearly (correcting
          typos/expanding abbreviations if needed), "subTopics" is a list of
          {{practice_min_steps}}-{{practice_max_steps}} distinct facets of the topic
          suitable as individual quiz questions, and "clarifyingQuestion" is null.
        - When scoped is false: "clarifyingQuestion" asks ONE targeted question
          (which service/platform, which part of the workflow) — same style as the
          main assistant's TRIAGE policy, never an open "can you be more specific?"
          — and "refinedTopic"/"subTopics" are null/empty.
        - Use recentContext (prior topic + the user's narrowing answer, when
          present) to resolve what the user meant — don't re-ask about something
          already answered by a prior round.

        TAXONOMY
        {{taxonomy_list}}

        EXAMPLES
        [topic: "PaaS", min=3, max=6]
        -> {"scoped":false,"clarifyingQuestion":"Which part of PaaS would you like to be tested on — deploying an app, environment variables, custom domains, or something else?","refinedTopic":null,"subTopics":[]}

        [topic: "how environment variables work in a NodeJS app on Liara", min=3, max=6]
        -> {"scoped":true,"clarifyingQuestion":null,"refinedTopic":"Environment variables in a Liara NodeJS PaaS app","subTopics":["Setting environment variables in the Liara console","Reading environment variables in NodeJS code","Environment variable scopes (build-time vs runtime)"]}
        """;

    /// <summary>
    /// specs/04-prompts.md §2a "Cap-reached orchestration note" — appended
    /// to Text (never replacing it) once roundsAsked >= MAX_CLARIFYING_ROUNDS
    /// for this topic, the same "injected system note" mechanism §1a uses.
    /// </summary>
    public const string CapReachedInstruction =
        """
        ORCHESTRATION NOTE (not shown to the user): the clarifying-question limit
        for scoping this topic has been reached. You must return "scoped": true
        now, using your best judgment from everything given so far, even if the
        topic is still somewhat broad — do not ask another clarifying question.
        """;
}
