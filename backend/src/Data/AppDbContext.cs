using LiaraDocsAssistant.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LiaraDocsAssistant.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, DbConfig config) : DbContext(options)
{
    public DbSet<DocChunk> DocChunks => Set<DocChunk>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageFeedback> MessageFeedback => Set<MessageFeedback>();
    public DbSet<DocGapEvent> DocGapEvents => Set<DocGapEvent>();
    public DbSet<PracticeExam> PracticeExams => Set<PracticeExam>();
    public DbSet<PracticeExamAnswer> PracticeExamAnswers => Set<PracticeExamAnswer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocChunk>(e =>
        {
            e.ToTable("doc_chunks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Url).HasColumnName("url");
            e.Property(x => x.Anchor).HasColumnName("anchor");
            e.Property(x => x.Title).HasColumnName("title");
            e.Property(x => x.Category).HasColumnName("category");
            e.Property(x => x.Platform).HasColumnName("platform");
            e.Property(x => x.Body).HasColumnName("body");
            e.Property(x => x.TokenCount).HasColumnName("token_count");
            e.Property(x => x.Embedding).HasColumnName("embedding").HasColumnType($"vector({config.EmbedDim})");
            e.Property(x => x.ContentHash).HasColumnName("content_hash");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            e.HasIndex(x => new { x.Url, x.Anchor }).IsUnique();
            e.HasIndex(x => x.Category).HasDatabaseName("doc_chunks_category_idx");
        });

        modelBuilder.Entity<Session>(e =>
        {
            e.ToTable("sessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Locale).HasColumnName("locale");
            e.Property(x => x.Profile).HasColumnName("profile").HasColumnType("jsonb").HasDefaultValue("{}");
            e.Property(x => x.PendingClarification).HasColumnName("pending_clarification").HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.LastActiveAt).HasColumnName("last_active_at").HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<Message>(e =>
        {
            e.ToTable("messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.SessionId).HasColumnName("session_id");
            e.Property(x => x.Role).HasColumnName("role");
            e.Property(x => x.Content).HasColumnName("content");
            e.Property(x => x.Sources).HasColumnName("sources").HasColumnType("jsonb");
            e.Property(x => x.RouterScope).HasColumnName("router_scope");
            e.Property(x => x.TokensIn).HasColumnName("tokens_in");
            e.Property(x => x.TokensOut).HasColumnName("tokens_out");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId);
            e.HasIndex(x => new { x.SessionId, x.CreatedAt }).HasDatabaseName("messages_session_idx");
        });

        modelBuilder.Entity<MessageFeedback>(e =>
        {
            e.ToTable("message_feedback");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.MessageId).HasColumnName("message_id");
            e.Property(x => x.Vote).HasColumnName("vote");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.HasOne(x => x.Message).WithMany().HasForeignKey(x => x.MessageId);
        });

        modelBuilder.Entity<DocGapEvent>(e =>
        {
            e.ToTable("doc_gap_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Query).HasColumnName("query");
            e.Property(x => x.BestScore).HasColumnName("best_score");
            e.Property(x => x.CategoryGuess).HasColumnName("category_guess");
            e.Property(x => x.Mode).HasColumnName("mode");
            e.Property(x => x.SessionId).HasColumnName("session_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId);
        });

        modelBuilder.Entity<PracticeExam>(e =>
        {
            e.ToTable("practice_exams");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.SessionId).HasColumnName("session_id");
            e.Property(x => x.Topic).HasColumnName("topic");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.CurrentStepIndex).HasColumnName("current_step_index");
            e.Property(x => x.Steps).HasColumnName("steps").HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
            e.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId);
        });

        modelBuilder.Entity<PracticeExamAnswer>(e =>
        {
            e.ToTable("practice_exam_answers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.ExamId).HasColumnName("exam_id");
            e.Property(x => x.StepIndex).HasColumnName("step_index");
            e.Property(x => x.SelectedIndex).HasColumnName("selected_index");
            e.Property(x => x.IsCorrect).HasColumnName("is_correct");
            e.Property(x => x.AnsweredAt).HasColumnName("answered_at").HasDefaultValueSql("now()");
            e.HasOne(x => x.Exam).WithMany().HasForeignKey(x => x.ExamId);
            e.HasIndex(x => new { x.ExamId, x.StepIndex }).IsUnique();
        });
    }
}
