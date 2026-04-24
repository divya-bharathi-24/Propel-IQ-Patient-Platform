using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for <see cref="PatientProfileVerification"/> (EP-008-I/us_041, task_002).
/// Table: <c>patient_profile_verifications</c>
/// Key design decisions:
///   - One record per patient — enforced by unique index on <c>patient_id</c>.
///   - <c>verified_by</c> FK references <c>users</c> (Restrict — no cascade delete, DR-009).
///   - <c>status</c> stored as string for human-readable audit logs.
///   - Table schema is created in task_004 migration; this configuration defines the mapping.
/// </summary>
public sealed class PatientProfileVerificationConfiguration
    : IEntityTypeConfiguration<PatientProfileVerification>
{
    public void Configure(EntityTypeBuilder<PatientProfileVerification> builder)
    {
        builder.ToTable("patient_profile_verifications");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedOnAdd();

        builder.Property(v => v.Status)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(v => v.VerifiedAt)
               .HasColumnType("timestamp with time zone")
               .IsRequired();

        // FK: patient_profile_verifications → patients (Restrict)
        builder.HasOne(v => v.Patient)
               .WithMany()
               .HasForeignKey(v => v.PatientId)
               .OnDelete(DeleteBehavior.Restrict);

        // FK: patient_profile_verifications → users (Restrict — staff user who verified)
        builder.HasOne(v => v.VerifiedByUser)
               .WithMany()
               .HasForeignKey(v => v.VerifiedBy)
               .OnDelete(DeleteBehavior.Restrict);

        // Unique index — one verification record per patient
        builder.HasIndex(v => v.PatientId)
               .IsUnique()
               .HasDatabaseName("ix_patient_profile_verifications_patient_id");
    }
}
