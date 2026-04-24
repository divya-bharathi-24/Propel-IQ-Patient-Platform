using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
// TODO: Uncomment when pgvector is installed and AI features are ready
// using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
// using Pgvector;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="ExtractedData"/> entity (task_002).
/// Table: <c>extracted_data</c>
/// Key design decisions:
///   - <c>embedding</c> is mapped as <c>vector(1536)</c> — dimension matches text-embedding-3-small
///     output. Requires the <c>Pgvector</c> NuGet package and <c>UseVector()</c> on the
///     Npgsql data source builder (AC-2). [TEMPORARILY DISABLED - AI features commented out]
///   - HNSW index on <c>embedding</c> enables approximate cosine similarity search (AIR-R02). [TEMPORARILY DISABLED]
///   - <c>confidence</c> is constrained to [0, 1] by a database CHECK constraint (AC-edge).
///   - Composite index on <c>(document_id, data_type)</c> for document-scoped extraction queries.
///   - Both FKs use <see cref="DeleteBehavior.Restrict"/> (DR-009, AC-4).
/// </summary>
public sealed class ExtractedDataConfiguration : IEntityTypeConfiguration<ExtractedData>
{
    public void Configure(EntityTypeBuilder<ExtractedData> builder)
    {
        builder.ToTable("extracted_data", t =>
            t.HasCheckConstraint(
                "ck_extracted_data_confidence",
                "confidence >= 0 AND confidence <= 1"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.DataType)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(e => e.FieldName)
               .HasMaxLength(200)
               .IsRequired();

        builder.Property(e => e.Value)
               .HasMaxLength(2000)
               .IsRequired();

        builder.Property(e => e.Confidence)
               .HasPrecision(4, 3);

        builder.Property(e => e.SourceTextSnippet)
               .HasMaxLength(1000);

        // TODO: Uncomment when pgvector is installed and AI features are ready
        // Value converter: float[]? ↔ Pgvector.Vector? — required because Npgsql registers
        // the vector type handler for Pgvector.Vector, not float[] directly (task_003, AC-2).
        // Nullable variants used to match the nullable property type float[]?.
        // var embeddingConverter = new ValueConverter<float[]?, Vector?>(
        //     v => v != null ? new Vector(v.AsMemory()) : null,
        //     v => v != null ? v.ToArray() : null);

        // pgvector column — dimension = 1536 (text-embedding-3-small) — AC-2
        // COMMENTED OUT - AI features disabled temporarily
        // builder.Property(e => e.Embedding)
        //        .HasColumnType("vector(1536)")
        //        .HasConversion(embeddingConverter);

        // HNSW index for approximate cosine similarity search (AIR-R02)
        // COMMENTED OUT - AI features disabled temporarily
        // builder.HasIndex(e => e.Embedding)
        //        .HasMethod("hnsw")
        //        .HasOperators("vector_cosine_ops")
        //        .HasDatabaseName("ix_extracted_data_embedding_hnsw");

        // TEMPORARY: Ignore the Embedding property until pgvector is installed
        builder.Ignore(e => e.Embedding);

        // PriorityReview flag — set to true by orchestrator when confidence < 0.80 (AIR-003, task_005).
        // Default false ensures existing rows without an explicit confidence review are not flagged.
        builder.Property(e => e.PriorityReview)
               .IsRequired()
               .HasDefaultValue(false);

        // ── De-duplication fields (us_041/task_003) ──────────────────────────
        // IsCanonical: true = highest-confidence representative of its similarity cluster (AC-1).
        // Default false — set by PatientDeduplicationService after the pipeline completes.
        builder.Property(e => e.IsCanonical)
               .IsRequired()
               .HasDefaultValue(false);

        // CanonicalGroupId: links all members of a similarity cluster to the canonical record.
        // Nullable — null for Unprocessed records and standalone fields with no similar peers.
        builder.Property(e => e.CanonicalGroupId)
               .IsRequired(false);

        // DeduplicationStatus: Unprocessed / Canonical / Duplicate / FallbackManual.
        // Stored as string for human-readable audit logs; default = Unprocessed.
        builder.Property(e => e.DeduplicationStatus)
               .HasConversion<string>()
               .HasMaxLength(30)
               .IsRequired()
               .HasDefaultValue(Propel.Domain.Enums.DeduplicationStatus.Unprocessed);

        // Index on (patient_id, deduplication_status) — enables fast pipeline re-query
        // of all Unprocessed/FallbackManual fields for a given patient.
        builder.HasIndex(e => new { e.PatientId, e.DeduplicationStatus })
               .HasDatabaseName("ix_extracted_data_patient_dedup_status");

        // Partial index: patient-scoped canonical lookup used by the 360-degree aggregation
        // query (task_002 GetAggregated360ViewAsync). Only indexes rows where IsCanonical = true,
        // so the planner can quickly find the representative record for each field group (AC-1).
        builder.HasIndex(e => new { e.PatientId, e.IsCanonical })
               .HasDatabaseName("ix_extracted_data_patient_id_is_canonical")
               .HasFilter("is_canonical = true");

        // Composite index for document-scoped extraction queries
        builder.HasIndex(e => new { e.DocumentId, e.DataType })
               .HasDatabaseName("ix_extracted_data_document_type");

        // FK: extracted_data → clinical_documents (Restrict — no cascade delete per DR-009, AC-4)
        builder.HasOne(e => e.Document)
               .WithMany(d => d.ExtractedData)
               .HasForeignKey(e => e.DocumentId)
               .OnDelete(DeleteBehavior.Restrict);

        // FK: extracted_data → patients (Restrict — no cascade delete per DR-009, AC-4)
        builder.HasOne(e => e.Patient)
               .WithMany()
               .HasForeignKey(e => e.PatientId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
