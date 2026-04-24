using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class AddAiQualityMetricsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiQualityMetrics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    metric_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    field_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_agreement = table.Column<bool>(type: "boolean", nullable: true),
                    is_hallucination = table.Column<bool>(type: "boolean", nullable: true),
                    is_schema_valid = table.Column<bool>(type: "boolean", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_quality_metrics", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiQualityMetrics_MetricType_RecordedAt",
                table: "AiQualityMetrics",
                columns: new[] { "metric_type", "recorded_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AiQualityMetrics_SessionId",
                table: "AiQualityMetrics",
                column: "session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiQualityMetrics");
        }
    }
}
