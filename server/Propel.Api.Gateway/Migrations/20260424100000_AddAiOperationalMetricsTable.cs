using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class AddAiOperationalMetricsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiOperationalMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetricType = table.Column<int>(type: "integer", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModelVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ValueA = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    ValueB = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    Metadata = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiOperationalMetrics", x => x.Id);
                });

            // Composite index: MetricType ASC, RecordedAt DESC — primary rolling-window access pattern.
            migrationBuilder.CreateIndex(
                name: "IX_AiOperationalMetrics_MetricType_RecordedAt",
                table: "AiOperationalMetrics",
                columns: new[] { "MetricType", "RecordedAt" },
                descending: new[] { false, true });

            // RecordedAt DESC index — supports time-window count queries across all metric types.
            migrationBuilder.CreateIndex(
                name: "IX_AiOperationalMetrics_RecordedAt",
                table: "AiOperationalMetrics",
                column: "RecordedAt",
                descending: new[] { true });

            // Partial index on SessionId IS NOT NULL — skips CB trip rows (null SessionId).
            migrationBuilder.CreateIndex(
                name: "IX_AiOperationalMetrics_SessionId",
                table: "AiOperationalMetrics",
                column: "SessionId",
                filter: "\"SessionId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AiOperationalMetrics");
        }
    }
}
