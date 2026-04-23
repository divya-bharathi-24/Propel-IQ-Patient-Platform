using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <summary>
    /// Migration: AddPatientViewVerifiedAt (US_016 / TASK_003, AC-4).
    ///
    /// Changes applied in Up():
    ///   1. Adds <c>view_verified_at</c> (TIMESTAMPTZ NULL) to <c>patients</c>.
    ///      NULL = not yet verified. Staff sets this column via FR-047 (separate story).
    ///      The dashboard query derives <c>viewVerified = view_verified_at IS NOT NULL</c> (AC-4).
    ///   2. Creates partial index <c>IX_patients_verified</c> ON patients (id)
    ///      WHERE view_verified_at IS NOT NULL — supports efficient "all verified patients" queries.
    ///
    /// Down() rollback:
    ///   1. Drops partial index <c>IX_patients_verified</c>.
    ///   2. Drops column <c>view_verified_at</c> from <c>patients</c>.
    ///
    /// Zero-downtime compatible on PostgreSQL 16+ / Neon free tier:
    ///   - ADD COLUMN ... NULL acquires a brief ACCESS EXCLUSIVE lock (milliseconds, DR-013).
    ///   - CREATE INDEX is non-blocking on a small partial index.
    ///   - No existing rows are affected — all pre-existing patients get view_verified_at = NULL.
    /// </summary>
    public partial class AddPatientViewVerifiedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add view_verified_at (TIMESTAMPTZ NULL) — no default; existing rows → NULL (correct).
            migrationBuilder.AddColumn<DateTime>(
                name: "view_verified_at",
                table: "patients",
                type: "timestamptz",
                nullable: true);

            // 2. Partial index on verified patients — skips unverified rows (the majority).
            migrationBuilder.Sql(
                """
                CREATE INDEX "IX_patients_verified"
                    ON patients (id)
                    WHERE view_verified_at IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1. Drop partial index first (column dependency).
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_patients_verified";""");

            // 2. Drop column.
            migrationBuilder.DropColumn(
                name: "view_verified_at",
                table: "patients");
        }
    }
}
