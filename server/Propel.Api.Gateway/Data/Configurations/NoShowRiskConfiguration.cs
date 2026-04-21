using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="NoShowRisk"/> entity (task_002).
/// Table: <c>no_show_risks</c>
/// Key design decisions:
///   - <c>score</c> is constrained to [0, 1] by a database CHECK constraint (AC-edge).
///   - <c>factors</c> is mapped as JSONB to hold contributing risk factor payloads (FR-040).
///   - One-to-one relationship with <see cref="Appointment"/>: the risk record is
///     fully derived from the appointment and should cascade-delete when the appointment
///     is removed (acceptable deviation from DR-009 for this derived entity).
/// </summary>
public sealed class NoShowRiskConfiguration : IEntityTypeConfiguration<NoShowRisk>
{
    public void Configure(EntityTypeBuilder<NoShowRisk> builder)
    {
        builder.ToTable("no_show_risks", t =>
            t.HasCheckConstraint(
                "ck_no_show_risk_score",
                "score >= 0 AND score <= 1"));
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();

        builder.Property(r => r.Score)
               .HasPrecision(4, 3);

        // JSONB column — stores contributing risk factor breakdown (FR-040)
        builder.Property(r => r.Factors)
               .HasColumnType("jsonb")
               .IsRequired();

        builder.Property(r => r.CalculatedAt)
               .HasColumnType("timestamp with time zone");

        // One-to-one with Appointment — Cascade: risk record is derived from appointment (FR-040)
        // Appointment entity does not expose NoShowRisk navigation property → WithOne() is parameterless.
        builder.HasOne(r => r.Appointment)
               .WithOne()
               .HasForeignKey<NoShowRisk>(r => r.AppointmentId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
