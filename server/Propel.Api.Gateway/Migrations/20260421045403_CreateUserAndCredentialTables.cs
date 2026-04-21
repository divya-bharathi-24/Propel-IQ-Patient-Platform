using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <summary>
    /// Migration: CreateUserAndCredentialTables (task_003, US_012).
    ///
    /// Changes applied in Up():
    ///   1. Drops the case-sensitive unique index <c>ix_users_email</c> on <c>users</c> and
    ///      replaces it with a PostgreSQL functional unique index
    ///      <c>uq_users_email_lower ON users (lower(email))</c> to enforce case-insensitive
    ///      email uniqueness across Staff and Admin accounts (AC-3).
    ///   2. Makes <c>users.password_hash</c> nullable to support the admin invite flow:
    ///      accounts are created before credentials are set (AC-2).
    ///   3. Adds <c>users.name</c> (nullable VARCHAR 200) and
    ///      <c>users.credential_email_status</c> (VARCHAR 20, NOT NULL, DEFAULT 'Pending') (AC-1).
    ///   4. Adds DB-level CHECK constraints for <c>role</c>, <c>status</c>, and
    ///      <c>credential_email_status</c> to prevent free-text values (AC-4, DR-010).
    ///   5. Creates the <c>credential_setup_tokens</c> table with FK → users(id) CASCADE DELETE,
    ///      a unique index on <c>token_hash</c> (O(1) lookup), and an index on <c>user_id</c>
    ///      (resend invite lookups) (AC-2).
    ///
    /// NOTE: The <c>audit_logs</c> table is NOT touched by this migration (already created by
    /// the <c>AddAuditNotificationEntities</c> migration, US_010 task_003).
    /// Zero-downtime compatible: only new columns and new table (DR-013).
    /// </summary>
    public partial class CreateUserAndCredentialTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_email",
                table: "users");

            migrationBuilder.AlterColumn<string>(
                name: "password_hash",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AddColumn<string>(
                name: "credential_email_status",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "credential_setup_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credential_setup_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_credential_setup_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_credential_email_status",
                table: "users",
                sql: "credential_email_status IN ('Pending','Sent','Failed','Bounced')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_role",
                table: "users",
                sql: "role IN ('Patient','Staff','Admin')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_status",
                table: "users",
                sql: "status IN ('Active','Deactivated')");

            migrationBuilder.CreateIndex(
                name: "ix_credential_setup_tokens_token_hash",
                table: "credential_setup_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credential_setup_tokens_user_id",
                table: "credential_setup_tokens",
                column: "user_id");

            // PostgreSQL expression index — enforces users.email uniqueness regardless of casing (AC-3).
            // EF Core does not support functional indexes via HasIndex(), so managed via raw SQL.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX uq_users_email_lower ON users (lower(email));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop case-insensitive email index before dropping/restoring other objects
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS uq_users_email_lower;");

            migrationBuilder.DropTable(
                name: "credential_setup_tokens");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_credential_email_status",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_role",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_status",
                table: "users");

            migrationBuilder.DropColumn(
                name: "credential_email_status",
                table: "users");

            migrationBuilder.DropColumn(
                name: "name",
                table: "users");

            migrationBuilder.AlterColumn<string>(
                name: "password_hash",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);
        }
    }
}
