using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <summary>
    /// Migration: AddRefreshTokensTable (task_003, US_011).
    ///
    /// Changes applied:
    ///   1. Creates the <c>refresh_tokens</c> table with all columns required by the
    ///      token-rotation and reuse-detection flows (AC-1, AC-3, AC-4).
    ///   2. Adds a unique B-tree index on <c>token_hash</c> — primary access pattern on
    ///      every /refresh call (O(1) lookup, prevents hash collision attacks).
    ///   3. Adds a composite B-tree index on <c>(user_id, family_id)</c> — enables a single
    ///      UPDATE to revoke all tokens in a family when reuse is detected (edge case: stolen token).
    ///
    /// Security notes:
    ///   - The raw refresh token is NEVER stored; only its SHA-256 hex digest is persisted (OWASP A02).
    ///   - ON DELETE CASCADE on the user_id FK ensures orphaned tokens are purged on account removal.
    /// </summary>
    public partial class AddRefreshTokensTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Unique index: O(1) token lookup on every /refresh call; prevents hash collision (AC-1)
            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            // Composite index: enables atomic family-wide revocation in a single UPDATE (AC-3)
            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id_family_id",
                table: "refresh_tokens",
                columns: new[] { "user_id", "family_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop indexes first (correct rollback order), then drop table
            migrationBuilder.DropTable(
                name: "refresh_tokens");
        }
    }
}
