using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="AiPromptAuditLog"/> entity
/// (EP-010/us_049, AC-4, task_002).
/// <para>
/// Table: <c>ai_prompt_audit_logs</c>
/// Key design decisions:
/// <list type="bullet">
///   <item><description>INSERT-only — no UPDATE or DELETE (AD-7, DR-011 7-year retention).</description></item>
///   <item><description><see cref="AiPromptAuditLog.RedactedPrompt"/> and <see cref="AiPromptAuditLog.ResponseText"/>
///     mapped to PostgreSQL <c>text</c> (no length limit); bounded by AIR-O01 8,000-token budget.</description></item>
///   <item><description>Descending index on <c>(recorded_at DESC, id DESC)</c> supports the keyset
///     pagination query at <c>GET /api/admin/ai-audit-logs</c> without full table scan.</description></item>
///   <item><description>Partial index on <c>session_id</c> where not null for optional sessionId filter.</description></item>
///   <item><description>Partial index on <c>requesting_user_id</c> where not null for optional userId filter.</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class AiPromptAuditLogConfiguration : IEntityTypeConfiguration<AiPromptAuditLog>
{
    public void Configure(EntityTypeBuilder<AiPromptAuditLog> builder)
    {
        builder.ToTable("ai_prompt_audit_logs");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.RecordedAt)
               .IsRequired();

        builder.Property(e => e.SessionId)
               .HasMaxLength(200);

        builder.Property(e => e.RequestingUserId)
               .HasMaxLength(100);

        builder.Property(e => e.ModelName)
               .HasMaxLength(100);

        builder.Property(e => e.FunctionName)
               .HasMaxLength(200);

        // Unbounded text columns — content is bounded by AIR-O01 token budget at the application layer.
        builder.Property(e => e.RedactedPrompt)
               .HasColumnType("text");

        builder.Property(e => e.ResponseText)
               .HasColumnType("text");

        builder.Property(e => e.ContentFilterBlocked)
               .IsRequired()
               .HasDefaultValue(false);

        // Keyset pagination index: ORDER BY recorded_at DESC, id DESC (AC-4, perf).
        builder.HasIndex(e => new { e.RecordedAt, e.Id })
               .HasDatabaseName("ix_ai_prompt_audit_logs_recorded_at_id")
               .IsDescending(true, true);

        // Partial index for sessionId filter.
        builder.HasIndex(e => e.SessionId)
               .HasDatabaseName("ix_ai_prompt_audit_logs_session_id")
               .HasFilter("session_id IS NOT NULL");

        // Partial index for userId filter.
        builder.HasIndex(e => e.RequestingUserId)
               .HasDatabaseName("ix_ai_prompt_audit_logs_requesting_user_id")
               .HasFilter("requesting_user_id IS NOT NULL");
    }
}
