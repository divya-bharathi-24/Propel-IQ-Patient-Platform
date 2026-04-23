using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;
using Propel.Domain.Enums;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="Notification"/> entity (task_002).
/// Table: <c>notifications</c>
/// Key design decisions:
///   - <c>Channel</c> and <c>Status</c> stored as strings for human-readable DB values (AC-2).
///   - FK to <see cref="Patient"/> uses <see cref="DeleteBehavior.Restrict"/> (DR-009).
///   - FK to <see cref="Appointment"/> is optional (nullable <c>AppointmentId</c>) and also
///     uses <see cref="DeleteBehavior.Restrict"/>; configured without a navigation property
///     on the notification side because <see cref="Notification"/> has no Appointment nav prop.
///   - Three indexes support notification queries by patient, status, and appointment (AC-2).
///   - <c>ScheduledAt</c> and <c>SuppressedAt</c> added for US_033 reminder scheduler (task_005).
///   - Composite index on (AppointmentId, TemplateType, ScheduledAt) optimises idempotency checks.
///   - <c>TriggeredBy</c> and <c>ErrorReason</c> added for US_034 ad-hoc manual reminder (task_003).
///   - Composite index on (AppointmentId, SentAt DESC) supports debounce and last-reminder queries.
/// </summary>
public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedOnAdd();

        builder.Property(n => n.TemplateType)
               .HasMaxLength(150)
               .IsRequired();

        builder.Property(n => n.ErrorMessage)
               .HasMaxLength(500);

        builder.Property(n => n.Channel)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(n => n.Status)
               .HasConversion<string>()
               .HasMaxLength(30)
               .IsRequired();

        builder.Property(n => n.SentAt)
               .HasColumnType("timestamp with time zone");

        builder.Property(n => n.LastRetryAt)
               .HasColumnType("timestamp with time zone");

        builder.Property(n => n.CreatedAt)
               .HasColumnType("timestamp with time zone");

        builder.Property(n => n.UpdatedAt)
               .HasColumnType("timestamp with time zone");

        // US_033 (task_005) — reminder scheduler columns.
        builder.Property(n => n.ScheduledAt)
               .HasColumnType("timestamp with time zone")
               .IsRequired(false);

        builder.Property(n => n.SuppressedAt)
               .HasColumnType("timestamp with time zone")
               .IsRequired(false);

        // FK: notifications → patients (Restrict — no cascade delete per DR-009)
        builder.HasOne(n => n.Patient)
               .WithMany()
               .HasForeignKey(n => n.PatientId)
               .OnDelete(DeleteBehavior.Restrict);

        // FK: notifications → appointments (optional, Restrict — no cascade delete per DR-009)
        // Configured without navigation property on Notification side.
        builder.HasOne<Appointment>()
               .WithMany()
               .HasForeignKey(n => n.AppointmentId)
               .IsRequired(false)
               .OnDelete(DeleteBehavior.Restrict);

        // Index: lookup notifications by patient
        builder.HasIndex(n => n.PatientId)
               .HasDatabaseName("ix_notifications_patient_id");

        // Index: filter notifications by delivery status
        builder.HasIndex(n => n.Status)
               .HasDatabaseName("ix_notifications_status");

        // Index: lookup notifications associated with an appointment
        builder.HasIndex(n => n.AppointmentId)
               .HasDatabaseName("ix_notifications_appointment_id");

        // Composite index for scheduler idempotency check (AppointmentId, TemplateType, ScheduledAt).
        // Supports EXISTS query before creating duplicate reminder Notification records (US_033, AC-1).
        builder.HasIndex(n => new { n.AppointmentId, n.TemplateType, n.ScheduledAt })
               .HasDatabaseName("ix_notifications_appt_template_scheduled");

        // US_034 (task_003) — ad-hoc manual reminder trigger columns.
        builder.Property(n => n.TriggeredBy)
               .IsRequired(false);

        // FK: notifications.TriggeredBy → users.Id (nullable; SET NULL on user deletion).
        builder.HasOne<User>()
               .WithMany()
               .HasForeignKey(n => n.TriggeredBy)
               .IsRequired(false)
               .OnDelete(DeleteBehavior.SetNull);

        builder.Property(n => n.ErrorReason)
               .HasMaxLength(1000)
               .IsRequired(false);

        // Composite index (AppointmentId ASC, SentAt DESC) — supports debounce check and
        // last-manual-reminder lookup (US_034, AC-2, AC-3).
        builder.HasIndex(n => new { n.AppointmentId, n.SentAt })
               .HasDatabaseName("ix_notifications_appointment_id_sent_at")
               .IsDescending(false, true);
    }
}
