using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="MedicalCode"/> entity (task_002).
/// Table: <c>medical_codes</c>
/// Key design decisions:
///   - <c>confidence</c> is constrained to [0, 1] by a database CHECK constraint (AC-edge).
///   - <c>code_type</c> and <c>verification_status</c> are stored as strings for
///     forward-compatible enum expansion.
///   - Nullable <c>verified_by</c> / <c>verified_at</c> represent the unverified state (AC-3).
///   - Composite index on <c>(patient_id, verification_status)</c> optimises pending-code
///     retrieval queries for staff review dashboard (FR-039).
///   - Both FKs use <see cref="DeleteBehavior.Restrict"/> (DR-009, AC-4).
/// </summary>
public sealed class MedicalCodeConfiguration : IEntityTypeConfiguration<MedicalCode>
{
    public void Configure(EntityTypeBuilder<MedicalCode> builder)
    {
        builder.ToTable("medical_codes", t =>
            t.HasCheckConstraint(
                "ck_medical_codes_confidence",
                "confidence >= 0 AND confidence <= 1"));
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedOnAdd();

        builder.Property(m => m.CodeType)
               .HasConversion<string>()
               .HasMaxLength(10)
               .IsRequired();

        builder.Property(m => m.Code)
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(m => m.Description)
               .HasMaxLength(500)
               .IsRequired();

        builder.Property(m => m.Confidence)
               .HasPrecision(4, 3);

        builder.Property(m => m.VerificationStatus)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(m => m.VerifiedBy);

        builder.Property(m => m.VerifiedAt)
               .HasColumnType("timestamp with time zone");

        // FK: medical_codes → patients (Restrict — no cascade delete per DR-009)
        builder.HasOne(m => m.Patient)
               .WithMany()
               .HasForeignKey(m => m.PatientId)
               .OnDelete(DeleteBehavior.Restrict);

        // FK: medical_codes → clinical_documents (Restrict — no cascade delete per DR-009, AC-4)
        builder.HasOne(m => m.SourceDocument)
               .WithMany()
               .HasForeignKey(m => m.SourceDocumentId)
               .OnDelete(DeleteBehavior.Restrict);

        // Composite index — optimises pending-code queries for staff review dashboard (FR-039)
        builder.HasIndex(m => new { m.PatientId, m.VerificationStatus })
               .HasDatabaseName("ix_medical_codes_patient_pending");
    }
}
