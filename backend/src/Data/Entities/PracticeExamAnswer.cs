namespace LiaraDocsAssistant.Data.Entities;

public sealed class PracticeExamAnswer
{
    public Guid Id { get; set; }
    public Guid ExamId { get; set; }
    public int StepIndex { get; set; }
    public int SelectedIndex { get; set; }
    public bool IsCorrect { get; set; }
    public DateTimeOffset AnsweredAt { get; set; }

    public PracticeExam? Exam { get; set; }
}
