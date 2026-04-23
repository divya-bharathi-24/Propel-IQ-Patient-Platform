using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <summary>
    /// Migration: AddSeverityToNoShowRisks (us_031, task_002 — No-Show Risk Score Calculation).
    ///
    /// Changes applied in Up():
    ///   1. Adds <c>severity</c> VARCHAR(10) NOT NULL DEFAULT 'Medium' column to <c>no_show_risks</c>.
    ///      The column is required by <c>RuleBasedNoShowRiskCalculator</c> to persist the severity
    ///      band (Low / Medium / High) alongside the numeric score (AC-1, DR-018).
    ///
    /// Down() rollback:
    ///   Drops the <c>severity</c> column from <c>no_show_risks</c>.
    /// </summary>
    public partial class AddSeverityToNoShowRisks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "severity",
                table: "no_show_risks",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Medium");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "severity",
                table: "no_show_risks");
        }
    }
}
