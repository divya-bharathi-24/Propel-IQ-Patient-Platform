using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Propel.Api.Gateway.Data;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <summary>
    /// Migration: AddSystemSettingsAndNotificationColumns (US_033, task_005 — Automated Multi-Channel Reminders).
    ///
    /// Changes applied in Up():
    ///   1. Creates <c>system_settings</c> table — a key-value store for runtime-configurable
    ///      settings (AC-3). <c>Key</c> is the natural PK (VARCHAR 100); <c>Value</c> is TEXT
    ///      to hold JSON arrays; <c>updated_at</c> defaults to <c>now()</c>; <c>updated_by_user_id</c>
    ///      is a nullable FK to <c>users</c> for audit purposes.
    ///   2. Seeds default reminder intervals: <c>reminder_interval_hours = '[48,24,2]'</c> —
    ///      a single JSON-array value preferred over three separate rows for simpler
    ///      <c>GetReminderIntervalsAsync</c> deserialization (AC-1, FR-031).
    ///   3. Adds <c>last_retry_at</c> (nullable timestamptz) to <c>notifications</c> — records
    ///      the UTC timestamp of the most recent failed delivery retry attempt.
    ///   4. Adds <c>scheduled_at</c> (nullable timestamptz) to <c>notifications</c> — stores the
    ///      target dispatch window (48h / 24h / 2h before appointment) for reminder jobs (AC-1).
    ///      Non-null when the record represents a scheduled reminder; null for ad-hoc dispatches.
    ///   5. Adds <c>suppressed_at</c> (nullable timestamptz) to <c>notifications</c> — populated
    ///      when the reminder is suppressed due to appointment cancellation (AC-4).
    ///   6. Creates composite index <c>ix_notifications_appt_template_scheduled</c> on
    ///      <c>(appointment_id, template_type, scheduled_at)</c> — optimises idempotency check
    ///      queries in the scheduler (EXISTS query before creating duplicate reminders, AC-1).
    ///
    /// Down() rollback:
    ///   Drops the composite index, removes the three Notification columns, and drops the
    ///   <c>system_settings</c> table (reverse dependency order per DR-013).
    ///
    /// Additive migration — no existing data is modified. Zero-downtime compatible on PostgreSQL 16+.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260423000000_AddSystemSettingsAndNotificationColumns")]
    public partial class AddSystemSettingsAndNotificationColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Create system_settings table ─────────────────────────────────────
            // Natural string PK (key) — no surrogate needed for a settings store.
            // Value is TEXT to hold JSON arrays (e.g. "[48,24,2]") or plain scalars.
            // updated_by_user_id is a raw nullable FK (write-only audit trail, no nav property).
            migrationBuilder.CreateTable(
                name: "system_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_settings", x => x.key);
                    table.ForeignKey(
                        name: "fk_system_settings_users_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            // ── 2. Seed default reminder intervals ───────────────────────────────────
            // Single JSON-array row preferred over three separate rows for simpler
            // GetReminderIntervalsAsync deserialization (task_005 design note).
            migrationBuilder.InsertData(
                table: "system_settings",
                columns: new[] { "key", "value", "updated_at" },
                values: new object[] { "reminder_interval_hours", "[48,24,2]", new DateTime(2026, 4, 22, 0, 0, 0, DateTimeKind.Utc) });

            // ── 3. Add last_retry_at column to notifications ─────────────────────────
            migrationBuilder.AddColumn<DateTime>(
                name: "last_retry_at",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            // ── 4. Add scheduled_at column to notifications ──────────────────────────
            // Non-null when this row represents a pending scheduled reminder (AC-1).
            migrationBuilder.AddColumn<DateTime>(
                name: "scheduled_at",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            // ── 5. Add suppressed_at column to notifications ─────────────────────────
            // Populated when the reminder is suppressed due to appointment cancellation (AC-4).
            migrationBuilder.AddColumn<DateTime>(
                name: "suppressed_at",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            // ── 6. Composite index for scheduler idempotency check ───────────────────
            // Supports EXISTS query: "does a Pending reminder for this appointment+template+window exist?"
            // before creating a duplicate reminder Notification record (US_033, AC-1).
            migrationBuilder.CreateIndex(
                name: "ix_notifications_appt_template_scheduled",
                table: "notifications",
                columns: new[] { "appointment_id", "template_type", "scheduled_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop composite index before removing the columns it references
            migrationBuilder.DropIndex(
                name: "ix_notifications_appt_template_scheduled",
                table: "notifications");

            // Remove the three Notification columns (US_033, task_005 rollback)
            migrationBuilder.DropColumn(
                name: "suppressed_at",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "scheduled_at",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "last_retry_at",
                table: "notifications");

            // Drop system_settings table — removes all seed data implicitly
            migrationBuilder.DropTable(name: "system_settings");
        }
    }
}
