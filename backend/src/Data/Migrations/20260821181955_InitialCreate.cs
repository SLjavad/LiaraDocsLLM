using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace LiaraDocsAssistant.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("create extension if not exists vector;");
            migrationBuilder.Sql("create extension if not exists pg_trgm;");

            migrationBuilder.CreateTable(
                name: "doc_chunks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    url = table.Column<string>(type: "text", nullable: false),
                    anchor = table.Column<string>(type: "text", nullable: true),
                    title = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    platform = table.Column<string>(type: "text", nullable: true),
                    body = table.Column<string>(type: "text", nullable: false),
                    token_count = table.Column<int>(type: "integer", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(2048)", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_doc_chunks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    locale = table.Column<string>(type: "text", nullable: true),
                    profile = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    pending_clarification = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_active_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "doc_gap_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    query = table.Column<string>(type: "text", nullable: false),
                    best_score = table.Column<float>(type: "real", nullable: true),
                    category_guess = table.Column<string>(type: "text", nullable: true),
                    mode = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_doc_gap_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_doc_gap_events_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    sources = table.Column<string>(type: "jsonb", nullable: true),
                    router_scope = table.Column<string>(type: "text", nullable: true),
                    tokens_in = table.Column<int>(type: "integer", nullable: true),
                    tokens_out = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_messages_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "practice_exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    current_step_index = table.Column<int>(type: "integer", nullable: false),
                    steps = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_practice_exams", x => x.id);
                    table.ForeignKey(
                        name: "FK_practice_exams_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "message_feedback",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vote = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_feedback", x => x.id);
                    table.ForeignKey(
                        name: "FK_message_feedback_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "practice_exam_answers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    exam_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_index = table.Column<int>(type: "integer", nullable: false),
                    selected_index = table.Column<int>(type: "integer", nullable: false),
                    is_correct = table.Column<bool>(type: "boolean", nullable: false),
                    answered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_practice_exam_answers", x => x.id);
                    table.ForeignKey(
                        name: "FK_practice_exam_answers_practice_exams_exam_id",
                        column: x => x.exam_id,
                        principalTable: "practice_exams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_doc_chunks_url_anchor",
                table: "doc_chunks",
                columns: new[] { "url", "anchor" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "doc_chunks_category_idx",
                table: "doc_chunks",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "IX_doc_gap_events_session_id",
                table: "doc_gap_events",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_message_feedback_message_id",
                table: "message_feedback",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "messages_session_idx",
                table: "messages",
                columns: new[] { "session_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_practice_exam_answers_exam_id_step_index",
                table: "practice_exam_answers",
                columns: new[] { "exam_id", "step_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_practice_exams_session_id",
                table: "practice_exams",
                column: "session_id");

            // No HNSW/IVFFlat index on embedding: pgvector caps ANN indexes at 2000
            // dims for the vector type and EMBED_DIM=2048 exceeds it. Retrieval uses
            // an exact cosine-distance scan (<=>) — see specs/02-technical-spec.md §1
            // and specs/01-architecture.md §5/§11.
            migrationBuilder.Sql("create index doc_chunks_title_trgm_idx on doc_chunks using gin (title gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("drop index if exists doc_chunks_title_trgm_idx;");

            migrationBuilder.DropTable(
                name: "doc_chunks");

            migrationBuilder.DropTable(
                name: "doc_gap_events");

            migrationBuilder.DropTable(
                name: "message_feedback");

            migrationBuilder.DropTable(
                name: "practice_exam_answers");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "practice_exams");

            migrationBuilder.DropTable(
                name: "sessions");
        }
    }
}
