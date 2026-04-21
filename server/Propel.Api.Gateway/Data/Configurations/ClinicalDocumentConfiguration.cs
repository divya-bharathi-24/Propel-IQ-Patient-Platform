using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="ClinicalDocument"/> entity (task_002).
/// Table: <c>clinical_documents</c>
/// Key design decisions:
///   - <c>processing_status</c> is stored as a string to support future status additions
///     without a schema migration.
///   - FK to <c>patients</c> uses <see cref="DeleteBehavior.Restrict"/> (DR-009).
///   - Index on <c>patient_id</c> optimises document listing per patient.
/// </summary>
public sealed class ClinicalDocumentConfiguration : IEntityTypeConfiguration<ClinicalDocument>
{
    public void Configure(EntityTypeBuilder<ClinicalDocument> builder)
    {
        builder.ToTable("clinical_documents");
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

        // FK: clinical_documents → patients (Restrict — no cascade delete per DR-009)
        builder.HasOne(d => d.Patient)
               .WithMany()
               .HasForeignKey(d => d.PatientId)
               .OnDelete(DeleteBehavior.Restrict);

        // Index for patient-scoped document listing
        builder.HasIndex(d => d.PatientId)
               .HasDatabaseName("ix_clinical_documents_patient_id");
    }
}
