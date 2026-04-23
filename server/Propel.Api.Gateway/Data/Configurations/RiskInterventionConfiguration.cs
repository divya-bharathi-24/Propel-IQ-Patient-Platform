using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;
using Propel.Domain.Enums;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="RiskIntervention"/> entity (US_032, task_003).
/// Table: <c>risk_interventions</c>
/// Key design decisions:
///   - <c>type</c> and <c>status</c> are stored as VARCHAR strings for readability and
///     forward-compatibility (no numeric enum migration required for new values).
///   - <c>dismissal_reason</c> is capped at 500 chars by both the DB constraint and
///     FluentValidation validator (AC-3, DismissInterventionCommandValidator).
///   - FK to <c>appointments</c> uses <see cref="DeleteBehavior.Cascade"/>: intervention rows
///     are derived from the appointment and have no independent lifecycle (acceptable
///     deviation from DR-009 for this derived entity, mirroring NoShowRiskConfiguration).
///   - FK to <c>no_show_risks</c> uses <see cref="DeleteBehavior.Cascade"/>: intervention
///     rows are derived from the risk record and share its lifecycle.
///   - FK to <c>users</c> (staff) uses <see cref="DeleteBehavior.SetNull"/>: deleting a
///     staff user retains the intervention for audit history (staff_id nulled).
///   - Partial index on <c>(appointment_id) WHERE status = 'Pending'</c> optimises the
///     "requires-attention" dashboard query (AC-4).
/// </summary>
public sealed class RiskInterventionConfiguration : IEntityTypeConfiguration<RiskIntervention>
{
    public void Configure(EntityTypeBuilder<RiskIntervention> builder)
    {
        builder.ToTable("risk_interventions", t =>
        {
            t.HasCheckConstraint(
                "CK_risk_interventions_type",
                "type IN ('AdditionalReminder', 'CallbackRequest')");
            t.HasCheckConstraint(
                "CK_risk_interventions_status",
                "status IN ('Pending', 'Accepted', 'Dismissed', 'AutoCleared')");
        });

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id)
               .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(i => i.Type)
               .HasConversion<string>()
               .HasMaxLength(30)
               .IsRequired();

        builder.Property(i => i.Status)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired()
               .HasDefaultValue(InterventionStatus.Pending);

        builder.Property(i => i.DismissalReason)
               .HasMaxLength(500)
               .IsRequired(false);

        builder.Property(i => i.AcknowledgedAt)
               .HasColumnType("timestamptz")
               .IsRequired(false);

        builder.Property(i => i.CreatedAt)
               .HasColumnType("timestamptz")
               .HasDefaultValueSql("NOW()");

        // FK: risk_interventions → appointments (Cascade — intervention rows are derived from appointment)
        builder.HasOne(i => i.Appointment)
               .WithMany()
               .HasForeignKey(i => i.AppointmentId)
               .OnDelete(DeleteBehavior.Cascade);

        // FK: risk_interventions → no_show_risks (Cascade — interventions are derived from risk)
        builder.HasOne(i => i.NoShowRisk)
               .WithMany(r => r.RiskInterventions)
               .HasForeignKey(i => i.NoShowRiskId)
               .OnDelete(DeleteBehavior.Cascade);

        // FK: risk_interventions → users (SetNull — deleting staff user retains audit history)
        builder.HasOne(i => i.Staff)
               .WithMany()
               .HasForeignKey(i => i.StaffId)
               .OnDelete(DeleteBehavior.SetNull)
               .IsRequired(false);

        // Partial index — optimises the "requires-attention" dashboard query (AC-4)
        builder.HasIndex(i => i.AppointmentId)
               .HasFilter("status = 'Pending'")
               .HasDatabaseName("IX_risk_interventions_pending");
    }
}

