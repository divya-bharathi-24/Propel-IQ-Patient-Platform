using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class AddAiPromptAuditLogTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_prompt_audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    session_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    requesting_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    model_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    function_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    redacted_prompt = table.Column<string>(type: "text", nullable: true),
                    response_text = table.Column<string>(type: "text", nullable: true),
                    prompt_token_count = table.Column<int>(type: "integer", nullable: true),
                    completion_token_count = table.Column<int>(type: "integer", nullable: true),
                    content_filter_blocked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_prompt_audit_logs", x => x.id);
                });

            // Composite descending index on (recorded_at DESC, id DESC) — supports keyset
            // cursor pagination for GET /api/admin/ai-audit-logs (AC-4, EP-010/us_049).
            migrationBuilder.CreateIndex(
                name: "ix_ai_prompt_audit_logs_recorded_at_id",
                table: "ai_prompt_audit_logs",
                columns: new[] { "recorded_at", "id" },
                descending: new[] { true, true });

            // Partial index on session_id (IS NOT NULL rows only) — supports sessionId filter queries.
            migrationBuilder.CreateIndex(
                name: "ix_ai_prompt_audit_logs_session_id",
                table: "ai_prompt_audit_logs",
                column: "session_id",
                filter: "session_id IS NOT NULL");

            // Partial index on requesting_user_id (IS NOT NULL rows only) — supports userId filter queries.
            migrationBuilder.CreateIndex(
                name: "ix_ai_prompt_audit_logs_requesting_user_id",
                table: "ai_prompt_audit_logs",
                column: "requesting_user_id",
                filter: "requesting_user_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_prompt_audit_logs");
        }
    }
}
