using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="QueueEntry"/> entity (task_002).
/// Table: <c>queue_entries</c>
/// Key design decisions:
///   - One-to-one relationship with <see cref="Appointment"/> — each queue slot corresponds
///     to exactly one appointment. Uses <see cref="DeleteBehavior.Restrict"/> (DR-009).
///   - FK to <c>patients</c> uses <see cref="DeleteBehavior.Restrict"/> (DR-009).
///   - Composite index on <c>(status, position)</c> enables ordered queue reads
///     for the same-day queue dashboard (FR-022, UC-009).
/// </summary>
public sealed class QueueEntryConfiguration : IEntityTypeConfiguration<QueueEntry>
{
    public void Configure(EntityTypeBuilder<QueueEntry> builder)
    {
        builder.ToTable("queue_entries");
        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).ValueGeneratedOnAdd();

        builder.Property(q => q.Position)
               .IsRequired();

        builder.Property(q => q.ArrivalTime)
               .HasColumnType("timestamp with time zone");

        builder.Property(q => q.Status)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired();

        // FK: queue_entries → patients (Restrict — no cascade delete per DR-009)
        builder.HasOne(q => q.Patient)
               .WithMany()
               .HasForeignKey(q => q.PatientId)
               .OnDelete(DeleteBehavior.Restrict);

        // One-to-one with Appointment — Restrict: appointment record must be preserved (DR-009)
        // Appointment entity does not expose QueueEntry navigation property → WithOne() is parameterless.
        builder.HasOne(q => q.Appointment)
               .WithOne()
               .HasForeignKey<QueueEntry>(q => q.AppointmentId)
               .OnDelete(DeleteBehavior.Restrict);

        // Composite index — enables ordered queue reads for same-day dashboard (FR-022, UC-009)
        builder.HasIndex(q => new { q.Status, q.Position })
               .HasDatabaseName("ix_queue_entries_status_position");
    }
}
