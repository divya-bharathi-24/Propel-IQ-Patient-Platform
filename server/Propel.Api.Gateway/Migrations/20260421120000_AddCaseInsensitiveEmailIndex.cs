using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <summary>
    /// Migration: AddCaseInsensitiveEmailIndex (task_003, US_010).
    ///
    /// Changes applied:
    ///   1. Drops the case-sensitive unique index <c>ix_patients_email</c> and replaces it with a
    ///      PostgreSQL functional unique index <c>uq_patients_email_lower ON patients (lower(email))</c>.
    ///      This enforces AC-3 — email uniqueness is case-insensitive at the DB level, so an
    ///      attempt to register <c>User@Example.com</c> when <c>user@example.com</c> already exists
    ///      raises a unique constraint violation without application-layer normalisation.
    ///   2. Adds the composite index <c>ix_audit_logs_entity_type_entity_id</c> on
    ///      <c>audit_logs(entity_type, entity_id)</c> to support efficient audit queries by entity
    ///      (task_003, DR-009).
    ///
    /// EF Core cannot represent expression/functional indexes via the HasIndex() fluent API, so
    /// <see cref="uq_patients_email_lower"/> is managed exclusively through <see cref="MigrationBuilder.Sql"/>.
    /// </summary>
    public partial class AddCaseInsensitiveEmailIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Replace case-sensitive unique index with case-insensitive functional unique index (AC-3)
            migrationBuilder.DropIndex(
                name: "ix_patients_email",
                table: "patients");

            // PostgreSQL expression index — enforces uniqueness regardless of email casing (AC-3)
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX uq_patients_email_lower ON patients (lower(email));");

            // 2. Composite index for efficient audit queries by entity type + entity id (task_003, DR-009)
            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_type_entity_id",
                table: "audit_logs",
                columns: new[] { "entity_type", "entity_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse order: remove new indexes first, then restore original
            migrationBuilder.DropIndex(
                name: "ix_audit_logs_entity_type_entity_id",
                table: "audit_logs");

            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS uq_patients_email_lower;");

            // Restore original case-sensitive unique index
            migrationBuilder.CreateIndex(
                name: "ix_patients_email",
                table: "patients",
                column: "email",
                unique: true);
        }
    }
}
