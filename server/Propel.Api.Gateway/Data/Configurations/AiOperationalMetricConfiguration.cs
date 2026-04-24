using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;
using Propel.Domain.Enums;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="AiOperationalMetric"/> entity
/// (EP-010/us_050, task_004 — schema).
/// Table: <c>AiOperationalMetrics</c>
/// <para>
/// Key design decisions:
/// <list type="bullet">
///   <item><description>INSERT-only: no FK cascade deletes, no soft-delete intercept (AD-7).</description></item>
///   <item><description><c>Id</c> is <c>ValueGeneratedNever()</c> — caller supplies Guid to avoid DB round-trip.</description></item>
///   <item><description><c>MetricType</c> stored as <c>int</c> for efficient composite index scans.</description></item>
///   <item><description><c>ValueA</c>/<c>ValueB</c> use <c>numeric(18,4)</c> — covers token counts and latency ms without float precision loss.</description></item>
///   <item><description>Composite index on <c>(MetricType ASC, RecordedAt DESC)</c> — primary rolling-window read pattern.</description></item>
///   <item><description>Partial index on <c>SessionId IS NOT NULL</c> — skips CB trip events (cross-session, null SessionId).</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class AiOperationalMetricConfiguration : IEntityTypeConfiguration<AiOperationalMetric>
{
    public void Configure(EntityTypeBuilder<AiOperationalMetric> builder)
    {
        builder.ToTable("AiOperationalMetrics");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        // Store enum as integer for efficient composite index scans (task_004, step 3).
        builder.Property(m => m.MetricType)
               .HasConversion<int>()
               .IsRequired();

        builder.Property(m => m.SessionId);

        builder.Property(m => m.ModelVersion)
               .HasMaxLength(100)
               .IsRequired();

        // numeric(18,4) covers token counts (int range) and latency ms (up to 8 digits) without precision loss.
        builder.Property(m => m.ValueA)
               .HasColumnType("numeric(18,4)");

        builder.Property(m => m.ValueB)
               .HasColumnType("numeric(18,4)");

        // Bounded max-length; CB open duration and error type are short strings (not text).
        builder.Property(m => m.Metadata)
               .HasMaxLength(1000);

        builder.Property(m => m.RecordedAt)
               .HasColumnType("timestamptz")
               .IsRequired();

        // Index 1: (MetricType ASC, RecordedAt DESC) — primary: WHERE MetricType = X ORDER BY RecordedAt DESC LIMIT N.
        builder.HasIndex(m => new { m.MetricType, m.RecordedAt })
               .HasDatabaseName("IX_AiOperationalMetrics_MetricType_RecordedAt")
               .IsDescending(false, true);

        // Index 2: RecordedAt DESC — supports time-window count queries across all metric types.
        builder.HasIndex(m => m.RecordedAt)
               .HasDatabaseName("IX_AiOperationalMetrics_RecordedAt")
               .IsDescending(true);

        // Index 3: partial index on SessionId IS NOT NULL — skips CB trip rows (null SessionId).
        builder.HasIndex(m => m.SessionId)
               .HasDatabaseName("IX_AiOperationalMetrics_SessionId")
               .HasFilter("\"SessionId\" IS NOT NULL");
    }
}
