using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicalDocumentPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Composite index: upload history per patient ordered by upload date DESC (AC-3)
            // Supports GetClinicalDocuments query for patient upload history dashboard.
            migrationBuilder.CreateIndex(
                name: "idx_clinical_documents_patient_uploaded",
                table: "clinical_documents",
                columns: new[] { "patient_id", "uploaded_at" });

            // Partial index: AI processing background service polls only Pending/Processing rows.
            // Filters out Completed/Failed documents to keep the index small (DR-005).
            migrationBuilder.Sql(
                "CREATE INDEX idx_clinical_documents_processing_status_active " +
                "ON clinical_documents (processing_status) " +
                "WHERE processing_status IN ('Pending', 'Processing')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS idx_clinical_documents_processing_status_active");

            migrationBuilder.DropIndex(
                name: "idx_clinical_documents_patient_uploaded",
                table: "clinical_documents");
        }
    }
}
