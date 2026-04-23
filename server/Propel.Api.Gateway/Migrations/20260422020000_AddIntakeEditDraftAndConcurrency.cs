using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <summary>
    /// Migration: AddIntakeEditDraftAndConcurrency (US_017 / TASK_003).
    ///
    /// Changes applied in Up():
    ///   1. Adds <c>draft_data</c> (jsonb NULL) to <c>intake_records</c>.
    ///      Stores partial autosave snapshots during self-edit flow (AC-3, AC-4).
    ///      Set to NULL after a successful full save; non-null signals an in-progress draft.
    ///   2. Adds <c>last_modified_at</c> (timestamptz NULL) to <c>intake_records</c>.
    ///      Updated on every draft or full save; drives autosave timestamp in the UI.
    ///   3. Creates unique composite index <c>uq_intake_records_patient_appointment</c>
    ///      on (patient_id, appointment_id). Enforces the no-duplicate-IntakeRecord
    ///      guarantee (FR-010 / FR-019 / DR-004) at the database level, preventing
    ///      duplicate rows even under concurrent write scenarios.
    ///
    /// NOTE: <c>xmin</c> system column (optimistic concurrency token, RowVersion property)
    /// requires no DDL change. PostgreSQL maintains <c>xmin</c> automatically on every
    /// table row. EF Core's <c>IsRowVersion()</c> mapping instructs Npgsql to read
    /// <c>xmin</c> as a concurrency token at runtime — no ALTER TABLE statement is needed.
    ///
    /// Down() rollback:
    ///   1. Drops unique composite index <c>uq_intake_records_patient_appointment</c>.
    ///   2. Drops <c>last_modified_at</c> column.
    ///   3. Drops <c>draft_data</c> column.
    ///
    /// Zero-downtime compatible on PostgreSQL 16+ / Neon free tier:
    ///   - ADD COLUMN … NULL acquires a brief ACCESS EXCLUSIVE lock (milliseconds, DR-013).
    ///   - CREATE UNIQUE INDEX is non-blocking and safe for low-traffic tables.
    ///   - No existing rows are affected — all pre-existing intake records get NULL values.
    /// </summary>
    public partial class AddIntakeEditDraftAndConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. draft_data (jsonb NULL) — partial autosave snapshot (AC-3, AC-4).
            //    Cleared to NULL after a successful full save.
            migrationBuilder.AddColumn<JsonDocument>(
                name: "draft_data",
                table: "intake_records",
                type: "jsonb",
                nullable: true);

            // 2. last_modified_at (timestamptz NULL) — tracks most recent write (draft or full save).
            migrationBuilder.AddColumn<DateTime>(
                name: "last_modified_at",
                table: "intake_records",
                type: "timestamp with time zone",
                nullable: true);

            // 3. Unique composite index — enforces no-duplicate (patient_id, appointment_id) rows.
            //    Prevents a second IntakeRecord for the same appointment from being inserted
            //    even under concurrent staff/patient edit (DR-004, FR-010, FR-019).
            migrationBuilder.CreateIndex(
                name: "uq_intake_records_patient_appointment",
                table: "intake_records",
                columns: new[] { "patient_id", "appointment_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse order — drop index before removing the columns it references.

            // 1. Drop unique composite index.
            migrationBuilder.DropIndex(
                name: "uq_intake_records_patient_appointment",
                table: "intake_records");

            // 2. Drop last_modified_at.
            migrationBuilder.DropColumn(
                name: "last_modified_at",
                table: "intake_records");

            // 3. Drop draft_data.
            migrationBuilder.DropColumn(
                name: "draft_data",
                table: "intake_records");

            // xmin: system column — no drop required.
        }
    }
}
