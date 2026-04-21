using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="WaitlistEntry"/> entity (task_002).
/// Table: <c>waitlist_entries</c>
/// Key design decisions:
///   - FK to <c>patients</c> uses <see cref="DeleteBehavior.Restrict"/> (DR-009).
///   - FK to <c>appointments</c> is a one-to-one relationship (each booking has at most one
///     waitlist entry for a preferred slot).
///   - Index on <c>enrolled_at</c> enforces FIFO processing order when multiple patients
///     are waiting for the same slot (UC-004, DR-003).
/// </summary>
public sealed class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> builder)
    {
        builder.ToTable("waitlist_entries");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedOnAdd();

        builder.Property(w => w.PreferredDate)
               .HasColumnType("date");

        builder.Property(w => w.PreferredTimeSlot)
               .HasColumnType("time");

        builder.Property(w => w.EnrolledAt)
               .HasColumnType("timestamp with time zone");

        builder.Property(w => w.Status)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired();

        // FK: waitlist_entries → patients (Restrict — no cascade delete per DR-009)
        builder.HasOne(w => w.Patient)
               .WithMany(p => p.WaitlistEntries)
               .HasForeignKey(w => w.PatientId)
               .OnDelete(DeleteBehavior.Restrict);

        // FK: waitlist_entries → appointments (one-to-one — each appointment has ≤ 1 waitlist entry)
        builder.HasOne(w => w.CurrentAppointment)
               .WithOne(a => a.WaitlistEntry)
               .HasForeignKey<WaitlistEntry>(w => w.CurrentAppointmentId)
               .OnDelete(DeleteBehavior.Restrict);

        // FIFO ordering index — ensures earliest-enrolled patient gets the swapped slot first (DR-003)
        builder.HasIndex(w => w.EnrolledAt)
               .HasDatabaseName("ix_waitlist_enrolled_at");
    }
}
