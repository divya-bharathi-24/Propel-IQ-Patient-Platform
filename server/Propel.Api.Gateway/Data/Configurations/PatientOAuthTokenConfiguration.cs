using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for <see cref="PatientOAuthToken"/> (us_035, NFR-004).
/// Table: <c>patient_oauth_tokens</c>
/// <list type="bullet">
///   <item>Token values are stored pre-encrypted by the handler (Data Protection API, AES-256).</item>
///   <item>Unique index on (patient_id, provider) ensures one token record per patient per provider.</item>
///   <item>FK to <c>patients</c> uses <c>Restrict</c> (no cascade delete per DR-009).</item>
/// </list>
/// </summary>
public sealed class PatientOAuthTokenConfiguration : IEntityTypeConfiguration<PatientOAuthToken>
{
    public void Configure(EntityTypeBuilder<PatientOAuthToken> builder)
    {
        builder.ToTable("patient_oauth_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();

        builder.Property(t => t.Provider)
               .HasMaxLength(20)
               .IsRequired();

        // Ciphertext is Base64-encoded — 4000 chars accommodates large token payloads
        builder.Property(t => t.EncryptedAccessToken)
               .HasMaxLength(4000)
               .IsRequired();

        builder.Property(t => t.EncryptedRefreshToken)
               .HasMaxLength(4000)
               .IsRequired();

        builder.Property(t => t.ExpiresAt)
               .HasColumnType("timestamp with time zone");

        builder.Property(t => t.CreatedAt)
               .HasColumnType("timestamp with time zone");

        builder.Property(t => t.UpdatedAt)
               .HasColumnType("timestamp with time zone");

        // One token record per patient per provider (upsert key)
        builder.HasIndex(t => new { t.PatientId, t.Provider })
               .IsUnique()
               .HasDatabaseName("ix_patient_oauth_tokens_patient_provider");

        // FK: patient_oauth_tokens → patients (Restrict — no cascade delete per DR-009)
        builder.HasOne(t => t.Patient)
               .WithMany()
               .HasForeignKey(t => t.PatientId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
