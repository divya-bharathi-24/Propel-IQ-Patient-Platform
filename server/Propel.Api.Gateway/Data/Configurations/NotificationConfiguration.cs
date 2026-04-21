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

        builder.Property(n => n.CreatedAt)
               .HasColumnType("timestamp with time zone");

        builder.Property(n => n.UpdatedAt)
               .HasColumnType("timestamp with time zone");

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
    }
}
