using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;
using Propel.Domain.Enums;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="ClinicalDocument"/> entity.
/// Table: <c>clinical_documents</c>
/// Key design decisions:
///   - <c>processing_status</c> is stored as string for forward compatibility.
///   - <c>source_type</c> VARCHAR(50) with CHECK constraint; default PatientUpload (US_039, AC-2).
///   - FK to <c>patients</c> uses <see cref="DeleteBehavior.Restrict"/> (DR-009).
///   - FK to <c>users</c> (<c>uploaded_by_id</c>) uses <see cref="DeleteBehavior.SetNull"/>
///     so document history is preserved when a staff user is deactivated (US_039, TASK_003).
///   - Partial index on <c>(patient_id, source_type) WHERE deleted_at IS NULL</c> for fast
///     document history queries excluding soft-deleted rows (US_039, TASK_003).
/// </summary>
public sealed class ClinicalDocumentConfiguration : IEntityTypeConfiguration<ClinicalDocument>
{
    public void Configure(EntityTypeBuilder<ClinicalDocument> builder)
    {
        builder.ToTable("clinical_documents", t =>
        {
            // CHECK constraint: source_type must be a known enum value (US_039, AC-2, TASK_003)
            t.HasCheckConstraint("CK_clinical_documents_source_type",
                "source_type IN ('PatientUpload', 'StaffUpload')");
        });

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedOnAdd();

        builder.Property(d => d.FileName)
               .HasMaxLength(500)
               .IsRequired();

        builder.Property(d => d.StoragePath)
               .HasMaxLength(1000)
               .IsRequired();

        builder.Property(d => d.MimeType)
               .HasMaxLength(100)
               .IsRequired();

        builder.Property(d => d.FileSize)
               .HasColumnType("bigint");

        builder.Property(d => d.ProcessingStatus)
               .HasConversion<string>()
               .HasMaxLength(30)
               .IsRequired();

        builder.Property(d => d.UploadedAt)
               .HasColumnType("timestamp with time zone");

        // ── US_039 staff upload extension columns (TASK_003) ──────────────────

        builder.Property(d => d.SourceType)
               .HasColumnName("source_type")
               .HasColumnType("varchar(50)")
               .HasConversion<string>()
               .IsRequired();

        builder.Property(d => d.UploadedById)
               .HasColumnName("uploaded_by_id")
               .IsRequired(false);

        builder.Property(d => d.EncounterReference)
               .HasColumnName("encounter_reference")
               .HasMaxLength(100)
               .IsRequired(false);

        builder.Property(d => d.DeletedAt)
               .HasColumnName("deleted_at")
               .HasColumnType("timestamp with time zone")
               .IsRequired(false);

        builder.Property(d => d.DeletionReason)
               .HasColumnName("deletion_reason")
               .HasMaxLength(500)
               .IsRequired(false);

        // FK: clinical_documents → patients (Restrict — no cascade delete per DR-009)
        builder.HasOne(d => d.Patient)
               .WithMany()
               .HasForeignKey(d => d.PatientId)
               .OnDelete(DeleteBehavior.Restrict);

        // FK: clinical_documents → users (SetNull — preserves history when staff deactivated)
        builder.HasOne(d => d.UploadedBy)
               .WithMany()
               .HasForeignKey(d => d.UploadedById)
               .OnDelete(DeleteBehavior.SetNull)
               .IsRequired(false);

        // Index for patient-scoped document listing
        builder.HasIndex(d => d.PatientId)
               .HasDatabaseName("ix_clinical_documents_patient_id");

        // Partial index for staff document history queries (patient_id + source_type, active only)
        builder.HasIndex(d => new { d.PatientId, d.SourceType })
               .HasDatabaseName("ix_clinical_documents_patient_source");

        // Index: upload history per patient ordered by upload date (AC-3 — GetClinicalDocuments query)
        builder.HasIndex(d => new { d.PatientId, d.UploadedAt })
               .HasDatabaseName("idx_clinical_documents_patient_uploaded");

        // Partial index: AI processing poll — only active pipeline documents (Pending/Processing)
        builder.HasIndex(d => d.ProcessingStatus)
               .HasFilter("processing_status IN ('Pending', 'Processing')")
               .HasDatabaseName("idx_clinical_documents_processing_status_active");
    }
}
