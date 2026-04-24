using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="MedicalCode"/> entity (task_003).
/// Table: <c>medical_codes</c>
/// Key design decisions:
///   - <c>confidence</c> is nullable (null for manual entries) and constrained to [0, 1]
///     for non-null values by a database CHECK constraint (AC-4, DR-007).
///   - <c>code_type</c> and <c>verification_status</c> are stored as strings for
///     forward-compatible enum expansion.
///   - <c>verified_by</c> / <c>verified_at</c> nullable — represent the unverified state (AC-2).
///   - FK <c>patient_id</c> uses Cascade delete (patient deletion removes codes).
///   - FK <c>source_document_id</c> uses SetNull (document deletion preserves codes, AC-4).
///   - FK <c>verified_by</c> uses Restrict (user deletion blocked while codes reference them).
///   - Composite index on <c>(patient_id, code_type, verification_status)</c> optimises
///     fetching all Pending codes per patient by type (FR-039, AC-2).
///   - Index on <c>(patient_id, verification_status)</c> supports pending-code count queries
///     in the confirmation response (FR-039).
/// </summary>
public sealed class MedicalCodeConfiguration : IEntityTypeConfiguration<MedicalCode>
{
    public void Configure(EntityTypeBuilder<MedicalCode> builder)
    {
        builder.ToTable("medical_codes", t =>
            t.HasCheckConstraint(
                "ck_medical_codes_confidence",
                "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)"));

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedOnAdd();

        builder.Property(m => m.CodeType)
               .HasConversion<string>()
               .HasMaxLength(10)
               .IsRequired();

        builder.Property(m => m.Code)
               .HasMaxLength(10)
               .IsRequired();

        builder.Property(m => m.Description)
               .HasMaxLength(512)
               .IsRequired();

        // Nullable — null for manually entered codes where no AI confidence applies (AC-4)
        builder.Property(m => m.Confidence)
               .HasPrecision(4, 3);

        builder.Property(m => m.VerificationStatus)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(m => m.VerifiedBy);

        builder.Property(m => m.VerifiedAt)
               .HasColumnType("timestamp with time zone");

        // IsManualEntry: false for AI-suggested codes, true for staff manual entries (AC-4, DR-007)
        builder.Property(m => m.IsManualEntry)
               .IsRequired()
               .HasDefaultValue(false);

        // RejectionReason: populated only for Rejected codes (AC-3, FR-053)
        builder.Property(m => m.RejectionReason)
               .HasMaxLength(1000);

        builder.Property(m => m.CreatedAt)
               .HasColumnType("timestamp with time zone")
               .HasDefaultValueSql("now()");

        // FK: medical_codes → patients (Cascade — patient deletion removes all codes)
        builder.HasOne(m => m.Patient)
               .WithMany()
               .HasForeignKey(m => m.PatientId)
               .OnDelete(DeleteBehavior.Cascade);

        // FK: medical_codes → clinical_documents (optional — null for manual entries;
        // SetNull so document deletion preserves code records, AC-4)
        builder.HasOne(m => m.SourceDocument)
               .WithMany()
               .HasForeignKey(m => m.SourceDocumentId)
               .IsRequired(false)
               .OnDelete(DeleteBehavior.SetNull);

        // FK: medical_codes → users (optional — null while Pending; Restrict per DR-009)
        builder.HasOne(m => m.VerifiedByUser)
               .WithMany()
               .HasForeignKey(m => m.VerifiedBy)
               .IsRequired(false)
               .OnDelete(DeleteBehavior.Restrict);

        // Composite index — optimises fetching all Pending codes per patient by type (FR-039, AC-2)
        builder.HasIndex(m => new { m.PatientId, m.CodeType, m.VerificationStatus })
               .HasDatabaseName("ix_medical_codes_patient_codetype_status");

        // Index — supports pending-code count in confirmation response (FR-039)
        builder.HasIndex(m => new { m.PatientId, m.VerificationStatus })
               .HasDatabaseName("ix_medical_codes_patient_status");
    }
}

