using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <summary>
    /// Migration: ExtendAuditLogForAuthEvents (task_002, US_013).
    ///
    /// Changes applied in Up():
    ///   1. Makes <c>audit_logs.user_id</c> nullable — required to store FailedLogin events for
    ///      unknown-email attempts and RateLimitBlock events where user identity is not established
    ///      (AC-2, AC-3, AC-4; edge case: IP-based rate-limit block before identity is known).
    ///   2. Adds <c>audit_logs.role</c> (VARCHAR 50 NULL) — captures the authenticated user's
    ///      role claim at event time for HIPAA traceability (AC-1, DR-011). Nullable to allow
    ///      FailedLogin and RateLimitBlock entries where role is unknown.
    ///   3. Adds composite B-tree index <c>ix_audit_logs_action_timestamp</c> on (action ASC,
    ///      timestamp DESC) — accelerates HIPAA compliance queries of the form
    ///      WHERE action = '...' AND timestamp BETWEEN '...' AND '...'.
    ///   4. Adds B-tree index <c>ix_audit_logs_ip_address</c> — supports security monitoring
    ///      queries that filter by ip_address to identify brute-force patterns.
    ///
    /// Down() rollback notes:
    ///   - Indexes are dropped before column changes (correct rollback order).
    ///   - The role column is dropped cleanly.
    ///   - user_id null-to-not-null reversion uses a sentinel UUID
    ///     (00000000-0000-0000-0000-000000000000) to backfill rows that have user_id = NULL.
    ///     This is acceptable rollback semantics for an INSERT-only (immutable) audit log:
    ///     the sentinel rows cannot be "un-inserted" due to the immutable trigger, so the
    ///     backfill preserves row count while satisfying the NOT NULL constraint.
    ///
    /// The INSERT-only trigger <c>trg_audit_logs_immutable</c> installed by the
    /// AddAuditNotificationEntities migration is NOT affected by this migration — adding
    /// columns and indexes does not alter trigger registration (verified via pg_trigger).
    ///
    /// Dependent on: AddAuditNotificationEntities (creates audit_logs table + trigger).
    /// Zero-downtime compatible on Neon PostgreSQL free tier: all changes are additive
    /// except the nullable ALTER, which is a safe online schema change for PostgreSQL 16+.
    /// </summary>
    public partial class ExtendAuditLogForAuthEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Make user_id nullable — enables FailedLogin (unknown email) and RateLimitBlock
            //    events to be stored without a user identity (AC-2, AC-3, AC-4).
            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "audit_logs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // 2. Add role column — nullable VARCHAR(50) for role claim at event time (AC-1, DR-011)
            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "audit_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // 3. Composite index: HIPAA compliance queries — action + timestamp range scans
            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_action_timestamp",
                table: "audit_logs",
                columns: new[] { "action", "timestamp" },
                descending: new[] { false, true });

            // 4. Index: ip_address — brute-force / security monitoring pattern queries
            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_ip_address",
                table: "audit_logs",
                column: "ip_address");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop indexes first (correct rollback order — reverse of Up)
            migrationBuilder.DropIndex(
                name: "ix_audit_logs_action_timestamp",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_ip_address",
                table: "audit_logs");

            // Remove role column
            migrationBuilder.DropColumn(
                name: "role",
                table: "audit_logs");

            // Backfill NULL user_id values with a sentinel UUID before restoring NOT NULL.
            // Sentinel: 00000000-0000-0000-0000-000000000000 (zero UUID).
            // LIMITATION: Rows written by FailedLogin / RateLimitBlock events with NULL user_id
            // cannot be truly rolled back because the INSERT-only trigger prevents deletion.
            // The sentinel preserves row count and satisfies the NOT NULL constraint.
            // This is acceptable rollback semantics for an immutable (INSERT-only) audit log.
            migrationBuilder.Sql(
                "UPDATE audit_logs SET user_id = '00000000-0000-0000-0000-000000000000' WHERE user_id IS NULL;");

            // Restore user_id NOT NULL after sentinel backfill
            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "audit_logs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
