using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="InsuranceValidation"/> entity (task_002).
/// Table: <c>insurance_validations</c>
/// Key design decisions:
///   - <c>ValidationResult</c> stored as string for human-readable DB values (AC-2).
///   - FK to <see cref="Patient"/> uses <see cref="DeleteBehavior.Restrict"/> (DR-009).
///   - Two indexes support insurance lookups by patient and by validation outcome (AC-2).
/// </summary>
public sealed class InsuranceValidationConfiguration : IEntityTypeConfiguration<InsuranceValidation>
{
    public void Configure(EntityTypeBuilder<InsuranceValidation> builder)
    {
        builder.ToTable("insurance_validations");
        builder.HasKey(iv => iv.Id);
        builder.Property(iv => iv.Id).ValueGeneratedOnAdd();

        builder.Property(iv => iv.ProviderName)
               .HasMaxLength(200)
               .IsRequired();

        builder.Property(iv => iv.InsuranceId)
               .HasMaxLength(100)
               .IsRequired();

        builder.Property(iv => iv.ValidationMessage)
               .HasMaxLength(500);

        builder.Property(iv => iv.ValidationResult)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(iv => iv.ValidatedAt)
               .HasColumnType("timestamp with time zone");

        builder.Property(iv => iv.CreatedAt)
               .HasColumnType("timestamp with time zone");

        // FK: insurance_validations → patients (Restrict — no cascade delete per DR-009)
        builder.HasOne(iv => iv.Patient)
               .WithMany()
               .HasForeignKey(iv => iv.PatientId)
               .OnDelete(DeleteBehavior.Restrict);

        // FK: insurance_validations → appointments (Cascade — orphan validations cleaned up with appointment)
        // IsRequired(false) — AppointmentId is nullable; validation can exist before appointment is committed
        builder.HasOne(iv => iv.Appointment)
               .WithMany()
               .HasForeignKey(iv => iv.AppointmentId)
               .OnDelete(DeleteBehavior.Cascade)
               .IsRequired(false);

        // Index: lookup insurance validations by patient
        builder.HasIndex(iv => iv.PatientId)
               .HasDatabaseName("ix_insurance_validations_patient_id");

        // Index: filter validations by outcome
        builder.HasIndex(iv => iv.ValidationResult)
               .HasDatabaseName("ix_insurance_validations_result");

        // Composite index: patient dashboard query (AC-2) — validated_at DESC for latest-first ordering
        builder.HasIndex(iv => new { iv.PatientId, iv.ValidatedAt })
               .HasDatabaseName("ix_insurance_validations_patient_id_validated_at")
               .IsDescending(false, true);
    }
}
