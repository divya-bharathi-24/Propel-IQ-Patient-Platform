using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="RefreshToken"/> entity (task_003, US_011).
/// Table: <c>refresh_tokens</c>
/// Key design decisions:
///   - Supports both Patient and User (Staff/Admin) authentication via nullable PatientId/UserId.
///     Exactly one must be non-null (enforced via CHECK constraint in migration).
///   - Only the SHA-256 hex hash of the raw refresh token is persisted — never the raw value (OWASP A02, NFR-008).
///   - Unique index on <c>token_hash</c> guarantees O(1) lookup on every /refresh call and
///     prevents hash collisions from being silently accepted.
///   - <c>family_id</c> groups all rotated tokens issued from a single login chain.
///     Composite indexes on <c>(user_id, family_id)</c> and <c>(patient_id, family_id)</c> enable
///     a single UPDATE to revoke all tokens in a family during reuse-detection (AC-3, edge case: stolen token).
///   - FKs to <c>users</c> and <c>patients</c> use ON DELETE CASCADE so all tokens for a deleted account are
///     automatically purged — no orphaned rows (AC-1).
///   - <c>revoked_at</c> is nullable: NULL = active; non-NULL = rotated or explicitly revoked (AC-3, AC-4).
/// </summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens", t =>
        {
            // Exactly one of patient_id or user_id must be non-null (enforced at DB level)
            t.HasCheckConstraint(
                "ck_refresh_tokens_patient_or_user",
                "(patient_id IS NOT NULL AND user_id IS NULL) OR (patient_id IS NULL AND user_id IS NOT NULL)");
        });
        
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();

        builder.Property(t => t.PatientId)
               .IsRequired(false);

        builder.Property(t => t.UserId)
               .IsRequired(false);

        builder.Property(t => t.TokenHash)
               .HasMaxLength(512)
               .IsRequired();

        builder.Property(t => t.FamilyId)
               .IsRequired();

        builder.Property(t => t.DeviceId)
               .HasMaxLength(255)
               .IsRequired();

        builder.Property(t => t.ExpiresAt)
               .HasColumnType("timestamp with time zone")
               .IsRequired();

        builder.Property(t => t.CreatedAt)
               .HasColumnType("timestamp with time zone")
               .IsRequired();

        builder.Property(t => t.RevokedAt)
               .HasColumnType("timestamp with time zone");

        // Unique index: primary access pattern on every /refresh call — O(1) hash lookup (AC-1)
        builder.HasIndex(t => t.TokenHash)
               .IsUnique()
               .HasDatabaseName("ix_refresh_tokens_token_hash");

        // Composite indexes: enable atomic family-wide revocation in a single UPDATE (AC-3)
        builder.HasIndex(t => new { t.UserId, t.FamilyId })
               .HasDatabaseName("ix_refresh_tokens_user_id_family_id")
               .HasFilter("user_id IS NOT NULL");

        builder.HasIndex(t => new { t.PatientId, t.FamilyId })
               .HasDatabaseName("ix_refresh_tokens_patient_id_family_id")
               .HasFilter("patient_id IS NOT NULL");

        // FK → users.id with cascade delete — orphaned tokens are auto-purged on account removal (AC-1)
        builder.HasOne<User>()
               .WithMany()
               .HasForeignKey(t => t.UserId)
               .OnDelete(DeleteBehavior.Cascade)
               .HasConstraintName("fk_refresh_tokens_users_user_id");

        // FK → patients.id with cascade delete — orphaned tokens are auto-purged on patient account removal
        builder.HasOne<Patient>()
               .WithMany()
               .HasForeignKey(t => t.PatientId)
               .OnDelete(DeleteBehavior.Cascade)
               .HasConstraintName("fk_refresh_tokens_patients_patient_id");
    }
}
