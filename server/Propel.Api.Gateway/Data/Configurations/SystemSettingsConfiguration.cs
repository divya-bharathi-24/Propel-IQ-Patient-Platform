using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for <see cref="SystemSetting"/> (US_033, task_005).
/// Table: <c>system_settings</c>
/// Key design decisions:
///   - <c>Key</c> is the natural primary key (VARCHAR 100); no surrogate PK needed.
///   - <c>Value</c> is TEXT to accommodate JSON arrays for reminder intervals.
///   - <c>UpdatedByUserId</c> is a raw nullable FK with no navigation property (write-only audit trail).
///   - Default reminder interval seed row <c>reminder_interval_hours = "[48,24,2]"</c> applied here
///     so the scheduler can read defaults even before the migration seed runs.
/// </summary>
public sealed class SystemSettingsConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("system_settings");

        builder.HasKey(s => s.Key);

        builder.Property(s => s.Key)
               .HasMaxLength(100)
               .IsRequired();

        builder.Property(s => s.Value)
               .HasColumnType("text")
               .IsRequired();

        builder.Property(s => s.UpdatedAt)
               .HasColumnType("timestamp with time zone")
               .HasDefaultValueSql("now()");

        // Raw FK — no navigation property (write-only audit, AD-7 pattern).
        builder.Property(s => s.UpdatedByUserId)
               .IsRequired(false);

        // Seed default reminder intervals: JSON array "[48,24,2]" (AC-1, FR-031).
        // A single JSON-array value avoids three separate rows and simplifies GetReminderIntervalsAsync.
        builder.HasData(new SystemSetting
        {
            Key            = "reminder_interval_hours",
            Value          = "[48,24,2]",
            UpdatedAt      = new DateTime(2026, 4, 22, 0, 0, 0, DateTimeKind.Utc),
            UpdatedByUserId = null
        });
    }
}
