namespace LiaraDocsAssistant.Agent;

/// <summary>
/// Literal Practice Mode exam-generation prompt from specs/04-prompts.md §3 —
/// content, not code to author; do not edit wording here (tech-lead edits go
/// to 04-prompts.md).
/// </summary>
public static class ExamGenerationPrompt
{
    public const string Text = """
        You are generating a multiple-choice practice quiz for the Liara Docs
        Assistant's Practice Mode. You are given: the user's requested topic
        (already scoped/narrowed), and for each planned sub-topic, a retrieved
        documentation chunk (title, url, anchor, body).

        For EACH sub-topic chunk provided, generate exactly one quiz step:
        {
          "question": "...",
          "options": ["...", "...", "..."],
          "correctIndex": 0,
          "explanation": "...",
          "sourceChunkIndex": 0
        }

        RULES
        - The question and correct answer must be directly supported by the given
          chunk's body text. Do not use outside knowledge for the correct answer or
          explanation — if the chunk doesn't clearly support a good question,
          output {"skip": true, "sourceChunkIndex": <n>} for that one instead of
          inventing a question.
        - Exactly 3 options, exactly one correct (correctIndex 0-2).
        - The 2 incorrect options must be plausible (same category as the correct
          answer — e.g. if the correct answer is a CLI flag, wrong options are also
          real-looking CLI flags) but clearly wrong once the explanation is read.
          Never write a distractor that is arguably also correct, or an obvious
          joke/filler option.
        - explanation states briefly why the correct option is right, referencing
          the concept from the chunk — not a verbatim copy of the chunk text.
        - Write in the same language as the user's topic description.
        - Test understanding of Liara concepts/configuration/procedure, not coding
          ability — do not ask the user to write code.

        Return a JSON array of exactly one object per input chunk, in the same
        order (a "skip" object counts as that chunk's entry).
        """;
}
