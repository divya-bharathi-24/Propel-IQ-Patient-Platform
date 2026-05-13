using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class EnablePgvectorEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Enable pgvector extension (idempotent) ───────────────────────────────
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            // ── Add deleted_at / deletion_reason to clinical_documents if missing ────
            migrationBuilder.Sql(@"
                ALTER TABLE clinical_documents
                    ADD COLUMN IF NOT EXISTS deleted_at timestamp with time zone,
                    ADD COLUMN IF NOT EXISTS deletion_reason text;
            ");

            // ── Create document_chunk_embeddings table if it does not exist ──────────
            // This table was previously guarded by /* TEMPORARY */ in the original migration.
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS document_chunk_embeddings (
                    id uuid NOT NULL,
                    document_id uuid NOT NULL,
                    patient_id uuid NOT NULL,
                    chunk_text text NOT NULL,
                    page_number integer NOT NULL,
                    start_token_index integer NOT NULL,
                    end_token_index integer NOT NULL,
                    embedding vector(1536) NOT NULL,
                    created_at timestamp with time zone NOT NULL,
                    CONSTRAINT pk_document_chunk_embeddings PRIMARY KEY (id),
                    CONSTRAINT fk_document_chunk_embeddings_clinical_documents_document_id
                        FOREIGN KEY (document_id)
                        REFERENCES clinical_documents (id)
                        ON DELETE CASCADE,
                    CONSTRAINT fk_document_chunk_embeddings_patients_patient_id
                        FOREIGN KEY (patient_id)
                        REFERENCES patients (id)
                        ON DELETE RESTRICT
                );
            ");

            // ── Indexes ──────────────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_document_chunk_embeddings_document_page
                ON document_chunk_embeddings (document_id, page_number);
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_document_chunk_embeddings_patient_id
                ON document_chunk_embeddings (patient_id);
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_document_chunk_embeddings_embedding_hnsw
                ON document_chunk_embeddings
                USING hnsw (embedding vector_cosine_ops);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_document_chunk_embeddings_embedding_hnsw",
                table: "document_chunk_embeddings");

            migrationBuilder.DropIndex(
                name: "ix_calendar_sync_appointment_id",
                table: "calendar_syncs");

            migrationBuilder.AlterColumn<float[]>(
                name: "embedding",
                table: "document_chunk_embeddings",
                type: "real[]",
                nullable: false,
                oldClrType: typeof(Vector),
                oldType: "vector(1536)");

            migrationBuilder.AddColumn<Guid>(
                name: "appointment_id1",
                table: "calendar_syncs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_calendar_sync_appointment_id",
                table: "calendar_syncs",
                column: "appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_syncs_appointment_id1",
                table: "calendar_syncs",
                column: "appointment_id1",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_syncs_appointments_appointment_id1",
                table: "calendar_syncs",
                column: "appointment_id1",
                principalTable: "appointments",
                principalColumn: "id");
        }
    }
}
