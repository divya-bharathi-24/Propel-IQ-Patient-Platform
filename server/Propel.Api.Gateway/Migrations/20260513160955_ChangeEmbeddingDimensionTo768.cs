using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class ChangeEmbeddingDimensionTo768 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // pgvector requires TRUNCATE before changing vector dimensions
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_document_chunk_embeddings_embedding_hnsw;");
            migrationBuilder.Sql("TRUNCATE TABLE document_chunk_embeddings;");
            migrationBuilder.Sql("ALTER TABLE document_chunk_embeddings ALTER COLUMN embedding TYPE vector(768);");
            migrationBuilder.Sql("CREATE INDEX ix_document_chunk_embeddings_embedding_hnsw ON document_chunk_embeddings USING hnsw (embedding vector_cosine_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_document_chunk_embeddings_embedding_hnsw;");
            migrationBuilder.Sql("TRUNCATE TABLE document_chunk_embeddings;");
            migrationBuilder.Sql("ALTER TABLE document_chunk_embeddings ALTER COLUMN embedding TYPE vector(1536);");
            migrationBuilder.Sql("CREATE INDEX ix_document_chunk_embeddings_embedding_hnsw ON document_chunk_embeddings USING hnsw (embedding vector_cosine_ops);");
        }
    }
}
