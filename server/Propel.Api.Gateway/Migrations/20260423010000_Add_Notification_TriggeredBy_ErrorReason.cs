using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Propel.Api.Gateway.Data;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <summary>
    /// Migration: Add_Notification_TriggeredBy_ErrorReason (US_034, task_003 — Manual Ad-Hoc Reminder Trigger and Delivery Logging).
    ///
    /// Changes applied in Up():
    ///   1. Adds <c>triggered_by</c> (uuid nullable) to <c>notifications</c> — FK to <c>users.id</c>
    ///      (ON DELETE SET NULL). Identifies the staff member who manually triggered the reminder.
    ///      NULL for automated (system-scheduled) reminders, preserving backward compatibility with
    ///      US_033 records (AC-2).
    ///   2. Adds <c>error_reason</c> (varchar(1000) nullable) to <c>notifications</c> — stores the
    ///      raw error message or code returned by SendGrid or Twilio when delivery fails.
    ///      NULL for successful deliveries (AC-4).
    ///   3. Adds FK constraint <c>fk_notifications_users_triggered_by</c> on <c>triggered_by</c>
    ///      referencing <c>users.id</c> with SET NULL on user deletion.
    ///   4. Creates composite index <c>ix_notifications_appointment_id_sent_at</c> on
    ///      <c>(appointment_id ASC, sent_at DESC)</c> — supports debounce check and
    ///      last-manual-reminder lookup (AC-2, AC-3).
    ///
    /// Down() rollback (reverse dependency order per DR-013):
    ///   Drops the composite index, drops the FK constraint, then removes both columns.
    ///   Existing Notification rows remain unaffected — <c>triggered_by</c> and <c>error_reason</c>
    ///   were NULL before this migration.
    ///
    /// Additive migration — no existing data is modified. Zero-downtime compatible on PostgreSQL 16+.
    /// Parameterised LINQ in repository implementation ensures no raw SQL injection (OWASP A03).
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260423010000_Add_Notification_TriggeredBy_ErrorReason")]
    public partial class Add_Notification_TriggeredBy_ErrorReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Add triggered_by column ───────────────────────────────────────────
            // Nullable uuid FK to users.id. NULL for automated system-triggered reminders
            // (US_033); non-null only when a staff member manually triggers a reminder (US_034).
            migrationBuilder.AddColumn<Guid>(
                name: "triggered_by",
                table: "notifications",
                type: "uuid",
                nullable: true);

            // ── 2. Add error_reason column ───────────────────────────────────────────
            // Raw error message or provider code from SendGrid/Twilio on delivery failure.
            // NULL for successful deliveries (AC-4).
            migrationBuilder.AddColumn<string>(
                name: "error_reason",
                table: "notifications",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            // ── 3. FK: notifications.triggered_by → users.id (SET NULL on user deletion) ──
            // SET NULL chosen over Restrict/Cascade so that deactivating a staff user does not
            // orphan or delete historical notification audit records (DR-009, AC-2).
            migrationBuilder.AddForeignKey(
                name: "fk_notifications_users_triggered_by",
                table: "notifications",
                column: "triggered_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // ── 4. Composite index (appointment_id ASC, sent_at DESC) ─────────────────
            // Supports debounce query: "was a manual reminder sent for this appointment
            // within the last N minutes?" (US_034, AC-2 — 5-minute cooldown enforcement).
            // Also supports last-manual-reminder lookup for staff appointment detail (AC-3).
            migrationBuilder.CreateIndex(
                name: "ix_notifications_appointment_id_sent_at",
                table: "notifications",
                columns: new[] { "appointment_id", "sent_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop composite index first before removing the column it references
            migrationBuilder.DropIndex(
                name: "ix_notifications_appointment_id_sent_at",
                table: "notifications");

            // Drop FK constraint before removing the column
            migrationBuilder.DropForeignKey(
                name: "fk_notifications_users_triggered_by",
                table: "notifications");

            // Remove the two US_034 columns
            migrationBuilder.DropColumn(
                name: "triggered_by",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "error_reason",
                table: "notifications");
        }
    }
}
