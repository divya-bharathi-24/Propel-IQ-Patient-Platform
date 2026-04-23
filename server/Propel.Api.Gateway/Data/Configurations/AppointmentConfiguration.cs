using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;
using Propel.Domain.Enums;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="Appointment"/> entity (task_002).
/// Table: <c>appointments</c>
/// Key design decisions:
///   - Optimistic concurrency via PostgreSQL <c>xmin</c> system column (AC-3).
///     When two concurrent requests attempt to book the same slot, only one succeeds;
///     the other receives <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>.
///   - FK to <c>patients</c> and <c>specialties</c> uses <see cref="DeleteBehavior.Restrict"/>
///     to prevent cascade deletes (DR-009, AC-4).
///   - Soft-delete via global query filter — Cancelled appointments are excluded from
///     standard queries (DR-010).
///   - Composite index on (date, time_slot_start, specialty_id) optimises slot availability queries.
/// </summary>
public sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();

        builder.Property(a => a.Date)
               .HasColumnType("date");

        builder.Property(a => a.TimeSlotStart)
               .HasColumnType("time")
               .IsRequired(false);

        builder.Property(a => a.TimeSlotEnd)
               .HasColumnType("time")
               .IsRequired(false);

        builder.Property(a => a.Status)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(a => a.CancellationReason)
               .HasMaxLength(500);

        builder.Property(a => a.CreatedAt)
               .HasColumnType("timestamp with time zone")
               .HasDefaultValueSql("now()");

        // Optimistic concurrency token — mapped to PostgreSQL xmin system column (AC-3, DR-003)
        // xmin is a uint in PostgreSQL; Npgsql maps it to C# uint automatically.
        builder.Property(a => a.RowVersion)
               .HasColumnName("xmin")
               .HasColumnType("xid")
               .IsRowVersion();

        // FK: appointments → patients  (optional for anonymous walk-ins; Restrict prevents cascade-delete per DR-009)
        builder.HasOne(a => a.Patient)
               .WithMany(p => p.Appointments)
               .HasForeignKey(a => a.PatientId)
               .IsRequired(false)
               .OnDelete(DeleteBehavior.Restrict);

        // anonymous_visit_id: UUID generated for anonymous walk-in appointments (US_026, AC-3)
        builder.Property(a => a.AnonymousVisitId)
               .IsRequired(false);

        // FK: appointments → specialties (Restrict prevents cascade-delete per DR-009)
        builder.HasOne(a => a.Specialty)
               .WithMany(s => s.Appointments)
               .HasForeignKey(a => a.SpecialtyId)
               .OnDelete(DeleteBehavior.Restrict);

        // Composite index optimises real-time slot availability queries (FR-008)
        builder.HasIndex(a => new { a.Date, a.TimeSlotStart, a.SpecialtyId })
               .HasDatabaseName("ix_appointments_slot_lookup");

        // Global query filter — soft-delete: Cancelled appointments excluded from standard queries (DR-010)
        builder.HasQueryFilter(a => a.Status != AppointmentStatus.Cancelled);
    }
}
