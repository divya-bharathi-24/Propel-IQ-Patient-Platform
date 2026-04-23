using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <summary>
    /// Migration: AddPatientPendingAlerts (US_025 / TASK_001, dual-failure edge case).
    ///
    /// Changes applied in Up():
    ///   1. Adds <c>pending_alerts_json</c> (JSONB NULL) to <c>patients</c>.
    ///      Stores a JSON array of pending alert objects surfaced on next patient login.
    ///      Each element has the shape: <c>{ alertType, appointmentId, bookingReference, createdAt }</c>.
    ///      Written only when both email and SMS notification dispatch fail for a slot swap (AC-dual-failure).
    ///      Non-PHI; not encrypted.
    ///
    /// Down() rollback:
    ///   1. Drops column <c>pending_alerts_json</c> from <c>patients</c>.
    ///
    /// Zero-downtime compatible on PostgreSQL 16+:
    ///   - ADD COLUMN ... NULL acquires a brief ACCESS EXCLUSIVE lock (milliseconds, DR-013).
    ///   - No existing rows are affected — all pre-existing patients get NULL (no alerts).
    /// </summary>
    public partial class AddPatientPendingAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pending_alerts_json",
                table: "patients",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pending_alerts_json",
                table: "patients");
        }
    }
}
