using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="IntakeRecord"/> entity (task_002).
/// Table: <c>intake_records</c>
/// Key design decisions:
///   - Four JSONB columns (<c>demographics</c>, <c>medical_history</c>, <c>symptoms</c>,
///     <c>medications</c>) store structured patient intake payloads as PostgreSQL JSONB (AC-1).
///   - FKs to <c>patients</c> and <c>appointments</c> use <see cref="DeleteBehavior.Restrict"/>
///     to prevent cascade deletes (DR-009).
///   - Index on <c>patient_id</c> optimises patient-scoped intake record lookups.
/// </summary>
public sealed class IntakeRecordConfiguration : IEntityTypeConfiguration<IntakeRecord>
{
    public void Configure(EntityTypeBuilder<IntakeRecord> builder)
    {
        builder.ToTable("intake_records");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedOnAdd();

        builder.Property(i => i.Source)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(i => i.CompletedAt)
               .HasColumnType("timestamp with time zone");

        // JSONB columns — PostgreSQL jsonb for structured intake payloads (AC-1, FR-016, FR-017)
        builder.Property(i => i.Demographics)
               .HasColumnType("jsonb")
               .IsRequired();

        builder.Property(i => i.MedicalHistory)
               .HasColumnType("jsonb")
               .IsRequired();

        builder.Property(i => i.Symptoms)
               .HasColumnType("jsonb")
               .IsRequired();

        builder.Property(i => i.Medications)
               .HasColumnType("jsonb")
               .IsRequired();

        // FK: intake_records → patients (Restrict — no cascade delete per DR-009)
        builder.HasOne(i => i.Patient)
               .WithMany()
               .HasForeignKey(i => i.PatientId)
               .OnDelete(DeleteBehavior.Restrict);

        // FK: intake_records → appointments (Restrict — no cascade delete per DR-009)
        builder.HasOne(i => i.Appointment)
               .WithMany()
               .HasForeignKey(i => i.AppointmentId)
               .OnDelete(DeleteBehavior.Restrict);

        // Index for patient-scoped intake lookups
        builder.HasIndex(i => i.PatientId)
               .HasDatabaseName("ix_intake_records_patient_id");
    }
}
