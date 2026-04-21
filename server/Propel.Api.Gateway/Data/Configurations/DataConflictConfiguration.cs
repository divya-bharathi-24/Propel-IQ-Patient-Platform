using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="DataConflict"/> entity (task_002).
/// Table: <c>data_conflicts</c>
/// Key design decisions:
///   - Two distinct FK relationships to <c>clinical_documents</c> via
///     <c>source_document_id1</c> and <c>source_document_id2</c> capture the pair of
///     conflicting source documents (FR-035).
///   - Resolution fields (<c>resolved_value</c>, <c>resolved_by</c>, <c>resolved_at</c>)
///     are nullable to represent the unresolved state (AC-3 edge case).
///   - All FKs use <see cref="DeleteBehavior.Restrict"/> (DR-009, AC-4).
///   - Index on <c>patient_id</c> for quick conflict listing per patient.
/// </summary>
public sealed class DataConflictConfiguration : IEntityTypeConfiguration<DataConflict>
{
    public void Configure(EntityTypeBuilder<DataConflict> builder)
    {
        builder.ToTable("data_conflicts");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedOnAdd();

        builder.Property(d => d.FieldName)
               .HasMaxLength(200)
               .IsRequired();

        builder.Property(d => d.Value1)
               .HasMaxLength(2000)
               .IsRequired();

        builder.Property(d => d.Value2)
               .HasMaxLength(2000)
               .IsRequired();

        builder.Property(d => d.ResolutionStatus)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(d => d.ResolvedValue)
               .HasMaxLength(2000);

        builder.Property(d => d.ResolvedBy);

        builder.Property(d => d.ResolvedAt)
               .HasColumnType("timestamp with time zone");

        // FK: data_conflicts → patients (Restrict — no cascade delete per DR-009)
        builder.HasOne(d => d.Patient)
               .WithMany()
               .HasForeignKey(d => d.PatientId)
               .OnDelete(DeleteBehavior.Restrict);

        // FK: data_conflicts → clinical_documents via SourceDocumentId1
        // WithMany() — ClinicalDocument does not expose this conflict collection (DR-009, AC-4)
        builder.HasOne(d => d.SourceDocument1)
               .WithMany()
               .HasForeignKey(d => d.SourceDocumentId1)
               .OnDelete(DeleteBehavior.Restrict);

        // FK: data_conflicts → clinical_documents via SourceDocumentId2 (second conflicting source)
        builder.HasOne(d => d.SourceDocument2)
               .WithMany()
               .HasForeignKey(d => d.SourceDocumentId2)
               .OnDelete(DeleteBehavior.Restrict);

        // Index for patient-scoped conflict listing
        builder.HasIndex(d => d.PatientId)
               .HasDatabaseName("ix_data_conflicts_patient_id");
    }
}
