using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <summary>
    /// Migration: AddCalendarSyncRetryColumns (EP-007, us_037, task_003).
    ///
    /// Changes applied in Up():
    ///   1. Adds <c>retry_at</c> (TIMESTAMPTZ, nullable) to <c>calendar_syncs</c> — stores the
    ///      UTC time after which the US_037 retry processor should re-attempt propagation (AC-3).
    ///      Set to UtcNow + 10 minutes on failure; null when no retry is pending.
    ///   2. Adds <c>last_operation</c> (VARCHAR(10), nullable) to <c>calendar_syncs</c> — records
    ///      which calendar propagation operation failed: "Update" (PATCH) or "Delete" (DELETE) so
    ///      the retry processor knows which method to re-invoke (AC-3, EC-2). Null until set.
    ///   3. Creates partial index <c>ix_calendar_syncs_retry_at_failed</c> on
    ///      <c>calendar_syncs(retry_at)</c> WHERE <c>sync_status = 'Failed'</c> — supports
    ///      efficient <c>GetDueForRetryAsync</c> polling without full-table scans (AC-3, EC-2).
    ///
    /// Down() rollback (per DR-013):
    ///   Drops the partial index first, then drops both columns from <c>calendar_syncs</c>.
    ///
    /// Additive migration — no existing data is modified. Zero-downtime compatible on PostgreSQL 16+.
    /// Existing rows receive NULL for both columns, indicating no retry is pending.
    /// </summary>
    public partial class AddCalendarSyncRetryColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Add retry_at to calendar_syncs ─────────────────────────────────────
            // Nullable TIMESTAMPTZ. Set to (UtcNow + 10 min) by US_037 propagation logic
            // on failure; cleared (set to null) once the retry succeeds or is abandoned.
            migrationBuilder.AddColumn<DateTime>(
                name: "retry_at",
                table: "calendar_syncs",
                type: "timestamp with time zone",
                nullable: true);

            // ── 2. Add last_operation to calendar_syncs ───────────────────────────────
            // Nullable VARCHAR(10). Values: "Update" (PATCH propagation) or "Delete"
            // (DELETE propagation). Null when no US_037 operation has been recorded.
            migrationBuilder.AddColumn<string>(
                name: "last_operation",
                table: "calendar_syncs",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            // ── 3. Partial index for GetDueForRetryAsync ──────────────────────────────
            // Covers only rows with sync_status = 'Failed', minimising index size and
            // avoiding full-table scans on large calendar_syncs datasets (AC-3, EC-2).
            migrationBuilder.Sql(
                """
                CREATE INDEX "ix_calendar_syncs_retry_at_failed"
                    ON calendar_syncs (retry_at)
                    WHERE sync_status = 'Failed';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop partial index before removing columns (DR-013 rollback order)
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "ix_calendar_syncs_retry_at_failed";
                """);

            migrationBuilder.DropColumn(
                name: "last_operation",
                table: "calendar_syncs");

            migrationBuilder.DropColumn(
                name: "retry_at",
                table: "calendar_syncs");
        }
    }
}
