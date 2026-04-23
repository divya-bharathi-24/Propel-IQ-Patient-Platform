using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="CalendarSync"/> entity (task_002).
/// Table: <c>calendar_syncs</c>
/// Key design decisions:
///   - <c>Provider</c> and <c>SyncStatus</c> stored as strings for human-readable DB values (AC-3).
///   - Unique composite index on <c>(Provider, ExternalEventId)</c> prevents duplicate sync
///     records for the same external event across all patients (AC-3).
///   - FKs to <see cref="Patient"/> and <see cref="Appointment"/> both use
///     <see cref="DeleteBehavior.Restrict"/> (DR-009, AC-3).
///   - Index on <c>AppointmentId</c> optimises appointment-scoped calendar sync lookups (AC-3).
/// </summary>
public sealed class CalendarSyncConfiguration : IEntityTypeConfiguration<CalendarSync>
{
    public void Configure(EntityTypeBuilder<CalendarSync> builder)
    {
        builder.ToTable("calendar_syncs");
        builder.HasKey(cs => cs.Id);
        builder.Property(cs => cs.Id).ValueGeneratedOnAdd();

        builder.Property(cs => cs.ExternalEventId)
               .HasMaxLength(255)
               .IsRequired();

        builder.Property(cs => cs.ErrorMessage)
               .HasMaxLength(500);

        builder.Property(cs => cs.Provider)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(cs => cs.SyncStatus)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(cs => cs.SyncedAt)
               .HasColumnType("timestamp with time zone");

        builder.Property(cs => cs.EventLink)
               .HasMaxLength(2000);

        builder.Property(cs => cs.RetryScheduledAt)
               .HasColumnType("timestamp with time zone");

        // ── US_037 retry queue columns ────────────────────────────────────────────────
        // RetryAt: set to UtcNow+10min on failure; null when no retry is pending (AC-3).
        builder.Property(cs => cs.RetryAt)
               .HasColumnType("timestamp with time zone")
               .IsRequired(false);

        // LastOperation: "Update" or "Delete" — drives the re-invocation path in the
        // retry processor; null until US_037 propagation logic sets it (AC-3).
        builder.Property(cs => cs.LastOperation)
               .HasMaxLength(10)
               .IsRequired(false);

        builder.Property(cs => cs.CreatedAt)
               .HasColumnType("timestamp with time zone");

        builder.Property(cs => cs.UpdatedAt)
               .HasColumnType("timestamp with time zone");

        // FK: calendar_syncs → patients (Restrict — no cascade delete per DR-009)
        builder.HasOne(cs => cs.Patient)
               .WithMany()
               .HasForeignKey(cs => cs.PatientId)
               .OnDelete(DeleteBehavior.Restrict);

        // FK: calendar_syncs → appointments (Restrict — no cascade delete per DR-009)
        builder.HasOne(cs => cs.Appointment)
               .WithMany()
               .HasForeignKey(cs => cs.AppointmentId)
               .OnDelete(DeleteBehavior.Restrict);

        // Unique composite index: prevents duplicate sync records for the same external event (AC-3)
        builder.HasIndex(cs => new { cs.Provider, cs.ExternalEventId })
               .IsUnique()
               .HasDatabaseName("ix_calendar_sync_provider_external_id");

        // Index: appointment-scoped calendar sync lookups
        builder.HasIndex(cs => cs.AppointmentId)
               .HasDatabaseName("ix_calendar_sync_appointment_id");

        // Partial index: drives the efficient retry-queue poll in GetDueForRetryAsync (AC-3, EC-2).
        // Covers only rows where sync_status = 'Failed', avoiding full-table scans on large datasets.
        builder.HasIndex(cs => cs.RetryAt)
               .HasFilter("\"sync_status\" = 'Failed'")
               .HasDatabaseName("ix_calendar_syncs_retry_at_failed");
    }
}
