using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;
using Propel.Domain.Enums;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="Patient"/> entity (task_002, task_003).
/// Table: <c>patients</c>
/// Key design decisions:
///   - Email uniqueness enforced at DB level via case-insensitive functional unique index
///     <c>uq_patients_email_lower ON patients (lower(email))</c> created in migration
///     <c>AddCaseInsensitiveEmailIndex</c> via <c>migrationBuilder.Sql()</c>. This index
///     rejects duplicates regardless of casing (AC-3). EF Core does not support expression
///     indexes via <see cref="Microsoft.EntityFrameworkCore.Metadata.Builders.IndexBuilder"/>
///     so the index is not declared here — it is managed exclusively through migration SQL.
///   - Soft-delete via global query filter — records with Status = Deactivated are excluded
///     from all queries automatically (DR-010, AC-2).
///   - created_at defaults to <c>now()</c> at the DB level to avoid clock-skew from app servers.
/// </summary>
public sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("patients");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedOnAdd();

        // PHI columns (Name, Phone, DateOfBirth) are encrypted at rest by AesGcmPhiEncryptionService
        // via EF Core value converters in AppDbContext.OnModelCreating (NFR-004, NFR-013).
        // Column type is "text" to accommodate Base64-encoded ciphertext (longer than plaintext).
        builder.Property(p => p.Name)
               .HasColumnType("text")
               .IsRequired();

        builder.Property(p => p.Email)
               .HasMaxLength(320)
               .IsRequired();

        builder.Property(p => p.Phone)
               .HasColumnType("text")
               .IsRequired();

        // DateOfBirth is stored as encrypted text ("yyyy-MM-dd" string, then AES-256 ciphertext).
        // HasColumnType("date") removed — value converter changes storage type to text.
        builder.Property(p => p.DateOfBirth)
               .HasColumnType("text");

        builder.Property(p => p.PasswordHash)
               .HasMaxLength(500)
               .IsRequired();

        builder.Property(p => p.EmailVerified)
               .HasDefaultValue(false);

        builder.Property(p => p.Status)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(p => p.CreatedAt)
               .HasColumnType("timestamp with time zone")
               .HasDefaultValueSql("now()");

        // Global query filter — soft-delete: queries never surface Deactivated patients (DR-010)
        builder.HasQueryFilter(p => p.Status != PatientStatus.Deactivated);
    }
}
