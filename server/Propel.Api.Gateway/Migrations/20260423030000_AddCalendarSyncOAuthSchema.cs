using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <summary>
    /// Migration: AddCalendarSyncOAuthSchema (EP-007, us_035, task_003).
    ///
    /// Changes applied in Up():
    ///   1. Creates <c>patient_oauth_tokens</c> table — encrypted OAuth 2.0 token storage per patient
    ///      per provider (AC-2, NFR-004). Tokens are pre-encrypted by the handler using ASP.NET Core
    ///      Data Protection API (AES-256-GCM; Base64 output stored as VARCHAR(4000)).
    ///      Unique index on (patient_id, provider) enforces one record per patient per provider —
    ///      upsert semantics handled at the application layer (task_002 handler).
    ///      FK to patients uses Restrict (DR-009 — no silent cascade delete of PHI-adjacent data).
    ///   2. Adds <c>event_link</c> (VARCHAR 2000, nullable) to <c>calendar_syncs</c> — Google
    ///      Calendar event URL surfaced to the patient after a successful sync (AC-2).
    ///   3. Adds <c>retry_scheduled_at</c> (TIMESTAMPTZ, nullable) to <c>calendar_syncs</c> — when
    ///      <c>CalendarSyncRetryBackgroundService</c> should next attempt re-sync (AC-4).
    ///      Populated on first failure; cleared on success.
    ///   4. Adds <c>retry_count</c> (INTEGER NOT NULL DEFAULT 0) to <c>calendar_syncs</c> — counts
    ///      retry attempts; background service writes SyncStatus = 'PermanentFailed' after 3 (AC-4).
    ///
    /// Down() rollback (reverse dependency order per DR-013):
    ///   Removes the three calendar_syncs columns, then drops the patient_oauth_tokens table
    ///   (its unique index is automatically removed with the table).
    ///
    /// Additive migration — no existing data is modified. Zero-downtime compatible on PostgreSQL 16+.
    /// Encrypted token columns never store plaintext (OWASP A02, NFR-004).
    /// </summary>
    public partial class AddCalendarSyncOAuthSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Create patient_oauth_tokens table ──────────────────────────────────
            // One row per (patient_id, provider). encrypted_access_token and
            // encrypted_refresh_token hold Base64 Data Protection API ciphertext — never
            // raw token values (OWASP A02:2021, NFR-004).
            // FK uses Restrict, not Cascade, per DR-009: we never silently delete
            // PHI-adjacent rows from a Patient hard-delete path.
            migrationBuilder.CreateTable(
                name: "patient_oauth_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    encrypted_access_token = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    encrypted_refresh_token = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_oauth_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_patient_oauth_tokens_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Unique index: one token row per patient per provider (upsert semantics, AC-2).
            migrationBuilder.CreateIndex(
                name: "ix_patient_oauth_tokens_patient_provider",
                table: "patient_oauth_tokens",
                columns: new[] { "patient_id", "provider" },
                unique: true);

            // ── 2. Add event_link to calendar_syncs ───────────────────────────────────
            // Nullable VARCHAR(2000). Populated after a successful Google Calendar sync;
            // shown to the patient as a deep link to the created calendar event (AC-2).
            migrationBuilder.AddColumn<string>(
                name: "event_link",
                table: "calendar_syncs",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            // ── 3. Add retry_scheduled_at to calendar_syncs ───────────────────────────
            // Nullable TIMESTAMPTZ. Set to (UtcNow + 10 min) on first/subsequent failures
            // by CalendarSyncRetryBackgroundService; cleared on success (AC-4).
            migrationBuilder.AddColumn<DateTime>(
                name: "retry_scheduled_at",
                table: "calendar_syncs",
                type: "timestamp with time zone",
                nullable: true);

            // ── 4. Add retry_count to calendar_syncs ──────────────────────────────────
            // NOT NULL, DEFAULT 0. Incremented on each retry attempt. Background service
            // transitions SyncStatus to 'PermanentFailed' once retry_count reaches 3 (AC-4).
            migrationBuilder.AddColumn<int>(
                name: "retry_count",
                table: "calendar_syncs",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove calendar_syncs additions (reverse order for clarity)
            migrationBuilder.DropColumn(
                name: "retry_count",
                table: "calendar_syncs");

            migrationBuilder.DropColumn(
                name: "retry_scheduled_at",
                table: "calendar_syncs");

            migrationBuilder.DropColumn(
                name: "event_link",
                table: "calendar_syncs");

            // Drop patient_oauth_tokens (unique index dropped automatically with the table)
            migrationBuilder.DropTable(
                name: "patient_oauth_tokens");
        }
    }
}
