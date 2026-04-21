using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="RefreshToken"/> entity (task_003, US_011).
/// Table: <c>refresh_tokens</c>
/// Key design decisions:
///   - Only the SHA-256 hex hash of the raw refresh token is persisted — never the raw value (OWASP A02, NFR-008).
///   - Unique index on <c>token_hash</c> guarantees O(1) lookup on every /refresh call and
///     prevents hash collisions from being silently accepted.
///   - <c>family_id</c> groups all rotated tokens issued from a single login chain.
///     A composite index on <c>(user_id, family_id)</c> enables a single UPDATE to revoke
///     all tokens in a family during reuse-detection (AC-3, edge case: stolen token).
///   - FK to <c>users</c> uses ON DELETE CASCADE so all tokens for a deleted account are
///     automatically purged — no orphaned rows (AC-1).
///   - <c>revoked_at</c> is nullable: NULL = active; non-NULL = rotated or explicitly revoked (AC-3, AC-4).
/// </summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();

        builder.Property(t => t.UserId)
               .IsRequired();

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

        // Composite index: enables atomic family-wide revocation in a single UPDATE (AC-3)
        builder.HasIndex(t => new { t.UserId, t.FamilyId })
               .HasDatabaseName("ix_refresh_tokens_user_id_family_id");

        // FK → users.id with cascade delete — orphaned tokens are auto-purged on account removal (AC-1)
        builder.HasOne<User>()
               .WithMany()
               .HasForeignKey(t => t.UserId)
               .OnDelete(DeleteBehavior.Cascade)
               .HasConstraintName("fk_refresh_tokens_users_user_id");
    }
}
