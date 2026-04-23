using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class ExtendClinicalDocumentForStaffUpload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add source_type column (VARCHAR 50, NOT NULL, default 'PatientUpload')
            // Backfills existing rows as PatientUpload.
            migrationBuilder.AddColumn<string>(
                name: "source_type",
                table: "clinical_documents",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "PatientUpload");

            // Add uploaded_by_id FK column (nullable UUID → users.id ON DELETE SET NULL)
            migrationBuilder.AddColumn<Guid>(
                name: "uploaded_by_id",
                table: "clinical_documents",
                type: "uuid",
                nullable: true);

            // Add encounter_reference column (VARCHAR 100, nullable)
            migrationBuilder.AddColumn<string>(
                name: "encounter_reference",
                table: "clinical_documents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Add deleted_at column (TIMESTAMPTZ, nullable — soft-delete timestamp)
            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "clinical_documents",
                type: "timestamp with time zone",
                nullable: true);

            // Add deletion_reason column (VARCHAR 500, nullable)
            migrationBuilder.AddColumn<string>(
                name: "deletion_reason",
                table: "clinical_documents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // CHECK constraint: source_type must be a known value
            migrationBuilder.Sql(
                "ALTER TABLE clinical_documents " +
                "ADD CONSTRAINT \"CK_clinical_documents_source_type\" " +
                "CHECK (source_type IN ('PatientUpload', 'StaffUpload'))");

            // FK: uploaded_by_id → users.id ON DELETE SET NULL
            migrationBuilder.AddForeignKey(
                name: "fk_clinical_documents_users_uploaded_by_id",
                table: "clinical_documents",
                column: "uploaded_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // Composite index (patient_id, source_type) WHERE deleted_at IS NULL
            migrationBuilder.Sql(
                "CREATE INDEX ix_clinical_documents_patient_source " +
                "ON clinical_documents (patient_id, source_type) " +
                "WHERE deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop partial index
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_clinical_documents_patient_source");

            // Drop FK
            migrationBuilder.DropForeignKey(
                name: "fk_clinical_documents_users_uploaded_by_id",
                table: "clinical_documents");

            // Drop CHECK constraint
            migrationBuilder.Sql(
                "ALTER TABLE clinical_documents " +
                "DROP CONSTRAINT IF EXISTS \"CK_clinical_documents_source_type\"");

            // Drop columns in reverse order
            migrationBuilder.DropColumn(name: "deletion_reason", table: "clinical_documents");
            migrationBuilder.DropColumn(name: "deleted_at", table: "clinical_documents");
            migrationBuilder.DropColumn(name: "encounter_reference", table: "clinical_documents");
            migrationBuilder.DropColumn(name: "uploaded_by_id", table: "clinical_documents");
            migrationBuilder.DropColumn(name: "source_type", table: "clinical_documents");
        }
    }
}
