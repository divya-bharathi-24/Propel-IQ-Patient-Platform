using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <summary>
    /// Migration: ConvertDateOfBirthToTextForEncryption (bugfix for DbUpdateException).
    ///
    /// Changes applied in Up():
    ///   1. Alters <c>date_of_birth</c> column from <c>date</c> type to <c>text</c> type.
    ///      This is required because the Patient.DateOfBirth property is encrypted via
    ///      AES-256 PHI encryption (NFR-004, NFR-013) in AppDbContext.OnModelCreating.
    ///      The value converter serializes DateOnly as "yyyy-MM-dd" string, encrypts it,
    ///      and stores the Base64-encoded ciphertext, which requires TEXT storage.
    ///
    ///   2. Existing date values are converted to text format during migration.
    ///      PostgreSQL USING clause handles the type conversion: date::text.
    ///
    /// Down() rollback: converts <c>date_of_birth</c> back to <c>date</c> type.
    /// WARNING: Down() will fail if encrypted data exists in the column.
    /// This migration should only be rolled back immediately after applying Up()
    /// and before any encrypted data has been written.
    ///
    /// Fixes error: "column 'date_of_birth' is of type date but expression is of type text"
    /// </summary>
    public partial class ConvertDateOfBirthToTextForEncryption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Convert existing date values to text format and change column type
            // USING clause ensures existing dates are converted to text representation
            migrationBuilder.Sql(
                @"ALTER TABLE patients 
                  ALTER COLUMN date_of_birth TYPE text 
                  USING date_of_birth::text;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WARNING: This rollback will fail if encrypted data exists in the column
            // Only execute Down() if you're certain all values are still in "yyyy-MM-dd" format
            migrationBuilder.Sql(
                @"ALTER TABLE patients 
                  ALTER COLUMN date_of_birth TYPE date 
                  USING date_of_birth::date;");
        }
    }
}
