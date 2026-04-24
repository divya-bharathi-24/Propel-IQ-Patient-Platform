using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <summary>
    /// Migration: AddDocumentChunkEmbeddingsAndPriorityReview (US_040, task_005).
    ///
    /// Changes applied in Up():
    ///   1. Enables the pgvector extension (idempotent — shared extension, safe to call if already active).
    ///   2. Adds <c>priority_review</c> (BOOLEAN NOT NULL DEFAULT false) to <c>extracted_data</c>.
    ///      Set to true by the AI orchestrator when <c>confidence &lt; 0.80</c> (AIR-003, AC-3).
    ///   3. Creates <c>document_chunk_embeddings</c> table with:
    ///      - <c>embedding vector(1536)</c> — pgvector column for cosine similarity search (AC-1, AIR-R01).
    ///      - FK to <c>clinical_documents</c> (CASCADE delete — owned relationship).
    ///      - FK to <c>patients</c> (RESTRICT — no cascade per DR-009).
    ///      - HNSW index with <c>vector_cosine_ops</c> for approximate nearest-neighbour search (AIR-R02, TR-008).
    ///      - Composite index on (document_id, page_number) for page-scoped retrieval.
    ///      - Index on patient_id for ACL-filtered queries (AIR-S02).
    ///
    /// Delta check (AC-4): <c>ClinicalDocument.processing_status</c> already exists from US_038 migration
    /// (20260423020000_ExtendClinicalDocumentForStaffUpload). This migration does NOT re-add it.
    ///
    /// Down() rollback (per DR-013):
    ///   Drops the HNSW index, document_chunk_embeddings table, and priority_review column.
    ///   The pgvector extension is NOT dropped — it is shared infrastructure.
    ///
    /// Additive migration — existing data is not modified. Zero-downtime compatible on PostgreSQL 16+.
    /// </summary>
    public partial class AddDocumentChunkEmbeddingsAndPriorityReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Enable pgvector extension — idempotent, safe to call multiple times (TR-008, AD-5).
            //    Must execute BEFORE creating any vector(N) column or HNSW index.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            // 2. Add priority_review flag to extracted_data (AIR-003, AC-3, task_005).
            //    Default false — existing rows are not flagged without an explicit confidence check.
            migrationBuilder.AddColumn<bool>(
                name: "priority_review",
                table: "extracted_data",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // 3. Create document_chunk_embeddings table (AC-1, AIR-R01, TR-008, AD-5).
            migrationBuilder.CreateTable(
                name: "document_chunk_embeddings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_text = table.Column<string>(type: "text", nullable: false),
                    page_number = table.Column<int>(type: "integer", nullable: false),
                    start_token_index = table.Column<int>(type: "integer", nullable: false),
                    end_token_index = table.Column<int>(type: "integer", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_chunk_embeddings", x => x.id);
                    // CASCADE: deleting a document removes all its chunk embeddings (owned data, DR-009 exception).
                    table.ForeignKey(
                        name: "fk_document_chunk_embeddings_clinical_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "clinical_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    // RESTRICT: patient deletion must not cascade into chunk embeddings (DR-009).
                    table.ForeignKey(
                        name: "fk_document_chunk_embeddings_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Composite index: supports page-scoped chunk queries and document-level ACL checks (AIR-S02).
            migrationBuilder.CreateIndex(
                name: "ix_document_chunk_embeddings_document_page",
                table: "document_chunk_embeddings",
                columns: new[] { "document_id", "page_number" });

            // HNSW index for approximate cosine similarity search (AIR-R02, TR-008).
            // vector_cosine_ops matches the <=> cosine distance operator used in RAG retrieval queries.
            migrationBuilder.CreateIndex(
                name: "ix_document_chunk_embeddings_embedding_hnsw",
                table: "document_chunk_embeddings",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            // patient_id index: supports ACL-filtered chunk retrieval (AIR-S02).
            migrationBuilder.CreateIndex(
                name: "ix_document_chunk_embeddings_patient_id",
                table: "document_chunk_embeddings",
                column: "patient_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop document_chunk_embeddings table — cascades all its indexes and FK constraints.
            // Note: pgvector extension is NOT dropped — it is shared infrastructure (TR-008).
            migrationBuilder.DropTable(
                name: "document_chunk_embeddings");

            // Drop priority_review column from extracted_data (AIR-003 rollback).
            migrationBuilder.DropColumn(
                name: "priority_review",
                table: "extracted_data");
        }
    }
}

