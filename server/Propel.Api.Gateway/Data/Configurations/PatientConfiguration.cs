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
///   - Optimistic concurrency via PostgreSQL <c>xmin</c> system column (US_015, AC-4).
///     <c>xmin</c> is a uint maintained by PostgreSQL on every row write — no migration needed.
///   - US_015 demographic columns: BiologicalSex, Address (encrypted text), EmergencyContact
///     (encrypted text), CommunicationPreferences (jsonb), InsurerName, MemberId, GroupNumber.
///     PHI value converters for Address and EmergencyContact are applied in
///     <see cref="Propel.Api.Gateway.Data.AppDbContext.OnModelCreating"/> after this config runs.
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

        // ── US_015 demographic extension columns ──────────────────────────────
        // BiologicalSex: locked field (read-only post-registration) — plain text, nullable.
        builder.Property(p => p.BiologicalSex)
               .HasColumnName("biological_sex")
               .HasMaxLength(30);

        // AddressEncrypted: PHI — AES-256 encrypted JSON string stored in TEXT column.
        // Handlers encrypt before write and decrypt after read via IPhiEncryptionService.
        builder.Property(p => p.AddressEncrypted)
               .HasColumnName("address_encrypted")
               .HasColumnType("text");

        // EmergencyContactEncrypted: PHI — AES-256 encrypted JSON string stored in TEXT column.
        builder.Property(p => p.EmergencyContactEncrypted)
               .HasColumnName("emergency_contact_encrypted")
               .HasColumnType("text");

        // CommunicationPreferencesJson: non-PHI — plain JSON stored in JSONB column.
        builder.Property(p => p.CommunicationPreferencesJson)
               .HasColumnName("communication_preferences_json")
               .HasColumnType("jsonb");

        // PendingAlertsJson: non-PHI — plain JSONB array of pending alert payloads (US_025, dual-failure).
        // Written only when both email and SMS notification dispatch fail for a slot swap.
        builder.Property(p => p.PendingAlertsJson)
               .HasColumnName("pending_alerts_json")
               .HasColumnType("jsonb");

        builder.Property(p => p.InsurerName)
               .HasColumnName("insurer_name")
               .HasMaxLength(200);

        builder.Property(p => p.MemberId)
               .HasColumnName("member_id")
               .HasMaxLength(200);

        builder.Property(p => p.GroupNumber)
               .HasColumnName("group_number")
               .HasMaxLength(200);

        // Optimistic concurrency token — PostgreSQL xmin system column (US_015, AC-4).
        // xmin is a uint automatically maintained by PostgreSQL on every row write.
        // No migration is required — xmin exists on all PostgreSQL tables.
        builder.Property(p => p.RowVersion)
               .HasColumnName("xmin")
               .HasColumnType("xid")
               .IsRowVersion();

        // ── US_016 TASK_003 — 360° view-verified timestamp (FR-047) ──────────
        // Nullable TIMESTAMPTZ column added by the AddPatientViewVerifiedAt migration (TASK_003).
        // NULL means not yet verified; IS NOT NULL means verified.
        // The dashboard query derives viewVerified = ViewVerifiedAt IS NOT NULL (AC-4).
        builder.Property(p => p.ViewVerifiedAt)
               .HasColumnName("view_verified_at")
               .HasColumnType("timestamptz")
               .IsRequired(false);

        // Partial index: efficient query for "all verified patients" (FR-047 staff view).
        // Skips unverified rows (the vast majority) — consistent with PostgreSQL partial index semantics.
        builder.HasIndex(p => p.Id)
               .HasFilter("view_verified_at IS NOT NULL")
               .HasDatabaseName("IX_patients_verified");

        // Global query filter — soft-delete: queries never surface Deactivated patients (DR-010)
        builder.HasQueryFilter(p => p.Status != PatientStatus.Deactivated);
    }
}
