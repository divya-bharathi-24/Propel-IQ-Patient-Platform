using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class FixMissingClinicalDocumentColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add columns that exist in the EF model but are missing from the actual DB.
            // Using raw SQL with DO $$ blocks so these are safe to run even if columns already exist.
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name='clinical_documents' AND column_name='encounter_reference'
    ) THEN
        ALTER TABLE clinical_documents ADD COLUMN encounter_reference text NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name='clinical_documents' AND column_name='source_type'
    ) THEN
        ALTER TABLE clinical_documents ADD COLUMN source_type text NOT NULL DEFAULT 'PatientUpload';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name='clinical_documents' AND column_name='uploaded_by_id'
    ) THEN
        ALTER TABLE clinical_documents ADD COLUMN uploaded_by_id uuid NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name='clinical_documents' AND column_name='deleted_at'
    ) THEN
        ALTER TABLE clinical_documents ADD COLUMN deleted_at timestamp with time zone NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name='clinical_documents' AND column_name='deletion_reason'
    ) THEN
        ALTER TABLE clinical_documents ADD COLUMN deletion_reason text NULL;
    END IF;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE clinical_documents
    DROP COLUMN IF EXISTS encounter_reference,
    DROP COLUMN IF EXISTS source_type,
    DROP COLUMN IF EXISTS uploaded_by_id,
    DROP COLUMN IF EXISTS deleted_at,
    DROP COLUMN IF EXISTS deletion_reason;
");
        }
    }
}
