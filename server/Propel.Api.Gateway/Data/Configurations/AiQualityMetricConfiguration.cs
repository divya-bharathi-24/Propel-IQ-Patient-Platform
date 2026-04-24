using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="AiQualityMetric"/> entity
/// (EP-010/us_048, task_003 — schema).
/// Table: <c>AiQualityMetrics</c>
/// <para>
/// Key design decisions:
/// <list type="bullet">
///   <item><description>INSERT-only: no FK cascade deletes, no soft-delete intercept (AD-7).</description></item>
///   <item><description><c>Id</c> is <c>ValueGeneratedNever()</c> — caller supplies Guid to avoid DB round-trip.</description></item>
///   <item><description>Composite index on <c>(MetricType, RecordedAt DESC)</c> serves the rolling-window
///     read pattern used by <c>EfAiMetricsReadRepository</c>.</description></item>
///   <item><description>Index on <c>SessionId</c> supports per-session lookups and future debugging queries.</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class AiQualityMetricConfiguration : IEntityTypeConfiguration<AiQualityMetric>
{
    public void Configure(EntityTypeBuilder<AiQualityMetric> builder)
    {
        builder.ToTable("AiQualityMetrics");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.MetricType)
               .HasMaxLength(32)
               .IsRequired();

        builder.Property(m => m.FieldName)
               .HasMaxLength(256);

        builder.Property(m => m.IsAgreement);
        builder.Property(m => m.IsHallucination);
        builder.Property(m => m.IsSchemaValid);

        builder.Property(m => m.RecordedAt)
               .HasColumnType("timestamptz")
               .IsRequired();

        // Composite index: MetricType + RecordedAt DESC — primary rolling-window access pattern.
        builder.HasIndex(m => new { m.MetricType, m.RecordedAt })
               .HasDatabaseName("IX_AiQualityMetrics_MetricType_RecordedAt")
               .IsDescending(false, true);

        // Index on SessionId — per-session metric lookups.
        builder.HasIndex(m => m.SessionId)
               .HasDatabaseName("IX_AiQualityMetrics_SessionId");
    }
}
