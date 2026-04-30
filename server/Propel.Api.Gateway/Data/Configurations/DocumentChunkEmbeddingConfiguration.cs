using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="DocumentChunkEmbedding"/> entity (US_040, task_002).
/// Table: <c>document_chunk_embeddings</c>
/// TEMPORARY: Vector column mapping disabled until pgvector extension is installed.
/// Key design decisions:
/// <list type="bullet">
///   <item><description><c>embedding</c> is mapped as <c>vector(1536)</c> via a <see cref="ValueConverter{TModel,TProvider}"/>
///   converting <c>float[]</c> ↔ <see cref="Vector"/> — dimension matches text-embedding-3-small output (AC-1).</description></item>
///   <item><description>HNSW index on <c>embedding</c> with <c>vector_cosine_ops</c> enables approximate cosine
///   nearest-neighbour search required by AIR-R02.</description></item>
///   <item><description>FK to <c>clinical_documents</c> uses <see cref="DeleteBehavior.Cascade"/> so that
///   deleting a document automatically removes all its chunk embeddings (DR-009 exception — owned data).</description></item>
///   <item><description>FK to <c>patients</c> uses <see cref="DeleteBehavior.Restrict"/> per DR-009.</description></item>
///   <item><description>Composite index on <c>(document_id, page_number)</c> supports page-scoped chunk retrieval.</description></item>
/// </list>
/// </summary>
public sealed class DocumentChunkEmbeddingConfiguration : IEntityTypeConfiguration<DocumentChunkEmbedding>
{
    public void Configure(EntityTypeBuilder<DocumentChunkEmbedding> builder)
    {
        builder.ToTable("document_chunk_embeddings");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.ChunkText)
               .IsRequired();

        builder.Property(e => e.PageNumber)
               .IsRequired();

        builder.Property(e => e.StartTokenIndex)
               .IsRequired();

        builder.Property(e => e.EndTokenIndex)
               .IsRequired();

        builder.Property(e => e.CreatedAt)
               .HasColumnType("timestamp with time zone")
               .IsRequired();

        // TEMPORARY: Vector column configuration disabled until pgvector is installed
        // Uncomment these lines after running setup-pgvector.ps1
        
        // pgvector column — dimension = 1536 (text-embedding-3-small) — AC-1, AIR-R01.
        // ValueConverter translates float[] (domain) ↔ Vector (Npgsql pgvector type) transparently.
        // var embeddingConverter = new ValueConverter<float[], Vector>(
        //     v => new Vector(v),
        //     v => v.ToArray());

        // builder.Property(e => e.Embedding)
        //        .HasColumnType("vector(1536)")
        //        .HasConversion(embeddingConverter)
        //        .IsRequired();

        // HNSW index for approximate cosine similarity search (AIR-R02, task_005 migration).
        // The index uses vector_cosine_ops to match the <=> cosine distance operator in raw SQL queries.
        // builder.HasIndex(e => e.Embedding)
        //        .HasMethod("hnsw")
        //        .HasOperators("vector_cosine_ops")
        //        .HasDatabaseName("ix_document_chunk_embeddings_embedding_hnsw");

        // Composite index: supports page-scoped chunk queries and document-level ACL checks (AIR-S02).
        builder.HasIndex(e => new { e.DocumentId, e.PageNumber })
               .HasDatabaseName("ix_document_chunk_embeddings_document_page");

        // FK: document_chunk_embeddings → clinical_documents
        // Cascade: deleting a document removes all its chunks (owned relationship).
        builder.HasOne(e => e.Document)
               .WithMany()
               .HasForeignKey(e => e.DocumentId)
               .OnDelete(DeleteBehavior.Cascade);

        // FK: document_chunk_embeddings → patients (Restrict — no cascade delete per DR-009).
        builder.HasOne(e => e.Patient)
               .WithMany()
               .HasForeignKey(e => e.PatientId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
