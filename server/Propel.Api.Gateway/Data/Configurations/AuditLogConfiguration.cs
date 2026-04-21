using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="AuditLog"/> entity (task_002).
/// Table: <c>audit_logs</c>
/// Key design decisions:
///   - Per AD-7, the table is INSERT-only. No FK navigation properties are configured,
///     preventing ORM-level cascade deletes. No <see cref="DeleteBehavior"/> is applied.
///   - <c>details</c> is mapped as PostgreSQL JSONB to store structured before/after payloads.
///   - Three indexes support efficient audit queries by actor, patient, and time (descending).
///   - The database-level INSERT-only enforcement trigger is applied in the migration (task_003)
///     via <c>migrationBuilder.Sql()</c>, raising SQLSTATE 55000 on UPDATE or DELETE.
///   - <see cref="AuditLog"/> uses <c>init</c> accessors and does not implement any
///     soft-deletable interface, so the <see cref="AppDbContext.SaveChangesAsync"/> override
///     never intercepts or modifies AuditLog entries in the ChangeTracker (AC-1).
/// </summary>
public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();

        builder.Property(a => a.Action)
               .HasMaxLength(100)
               .IsRequired();

        builder.Property(a => a.EntityType)
               .HasMaxLength(100)
               .IsRequired();

        builder.Property(a => a.IpAddress)
               .HasMaxLength(45);  // Fits full IPv6 address (39 chars) + zone ID margin

        builder.Property(a => a.CorrelationId)
               .HasMaxLength(64);

        // Role of the actor at event time (nullable for anonymous events — US_013, AC-1)
        builder.Property(a => a.Role)
               .HasMaxLength(50);

        // JSONB column — stores structured before/after state or contextual metadata (AD-7)
        builder.Property(a => a.Details)
               .HasColumnType("jsonb");

        builder.Property(a => a.Timestamp)
               .HasColumnType("timestamp with time zone")
               .IsRequired();

        // UserId and PatientId are raw column values only — no HasOne/WithMany navigation (AD-7)
        // No OnDelete(DeleteBehavior.*) is configured to prevent any ORM cascade to audit_logs.

        // Index: audit lookup by acting user (ix_audit_logs_user_id)
        builder.HasIndex(a => a.UserId)
               .HasDatabaseName("ix_audit_logs_user_id");

        // Index: audit lookup by affected patient (ix_audit_logs_patient_id)
        builder.HasIndex(a => a.PatientId)
               .HasDatabaseName("ix_audit_logs_patient_id");

        // Index: time-ordered audit retrieval — descending so latest records surface first
        builder.HasIndex(a => a.Timestamp)
               .IsDescending()
               .HasDatabaseName("ix_audit_logs_timestamp");

        // Composite index: audit lookup by entity type + entity id (task_003, DR-009)
        builder.HasIndex(a => new { a.EntityType, a.EntityId })
               .HasDatabaseName("ix_audit_logs_entity_type_entity_id");

        // Composite index: HIPAA compliance queries filtering by action + timestamp range (US_013, task_002)
        // Leading column is action (high selectivity for filtered scans); timestamp DESC for date-range queries.
        builder.HasIndex(a => new { a.Action, a.Timestamp })
               .IsDescending(false, true)
               .HasDatabaseName("ix_audit_logs_action_timestamp");

        // Index: security monitoring queries filtering by ip_address (brute-force pattern detection, US_013)
        builder.HasIndex(a => a.IpAddress)
               .HasDatabaseName("ix_audit_logs_ip_address");
    }
}
