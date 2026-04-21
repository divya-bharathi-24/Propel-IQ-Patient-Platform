using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="EmailVerificationToken"/> entity.
/// Table: <c>email_verification_tokens</c>
/// Key design decisions:
///   - Only the SHA-256 hash of the raw token is stored (NFR-008).
///   - Unique index on <c>token_hash</c> guarantees O(1) lookup and prevents collision.
///   - Index on <c>patient_id</c> supports efficient invalidation of pending tokens.
///   - FK to <c>patients</c> uses ON DELETE CASCADE to clean up orphaned tokens.
/// </summary>
public sealed class EmailVerificationTokenConfiguration
    : IEntityTypeConfiguration<EmailVerificationToken>
{
    public void Configure(EntityTypeBuilder<EmailVerificationToken> builder)
    {
        builder.ToTable("email_verification_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();

        builder.Property(t => t.PatientId)
               .IsRequired();

        builder.Property(t => t.TokenHash)
               .HasMaxLength(64)   // SHA-256 hex = 64 characters
               .IsRequired();

        builder.Property(t => t.ExpiresAt)
               .HasColumnType("timestamp with time zone")
               .IsRequired();

        builder.Property(t => t.UsedAt)
               .HasColumnType("timestamp with time zone");

        builder.Property(t => t.CreatedAt)
               .HasColumnType("timestamp with time zone")
               .HasDefaultValueSql("now()")
               .IsRequired();

        // Unique index: prevents duplicate token hashes and enables O(1) lookup
        builder.HasIndex(t => t.TokenHash)
               .IsUnique()
               .HasDatabaseName("ix_email_verification_tokens_token_hash");

        // Index for efficient patient-scoped invalidation
        builder.HasIndex(t => t.PatientId)
               .HasDatabaseName("ix_email_verification_tokens_patient_id");

        builder.HasOne(t => t.Patient)
               .WithMany()
               .HasForeignKey(t => t.PatientId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
