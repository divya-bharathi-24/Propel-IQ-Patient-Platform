using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="User"/> entity (task_003, US_012).
/// Table: <c>users</c>
/// Covers Staff and Admin role accounts. Patient authentication uses the
/// <see cref="Patient"/> entity directly.
/// Key design decisions:
///   - Email uniqueness enforced at DB level via case-insensitive PostgreSQL functional unique
///     index <c>uq_users_email_lower ON users (lower(email))</c> created in migration
///     <c>CreateUserAndCredentialTables</c> via <c>migrationBuilder.Sql()</c>. EF Core does
///     not support expression indexes via <see cref="IndexBuilder"/>, so the index is NOT
///     declared here — it is managed exclusively through migration SQL (AC-3).
///   - Role constrained to <c>Patient|Staff|Admin</c> via DB CHECK constraint (AC-4).
///   - Status constrained to <c>Active|Deactivated</c> via DB CHECK constraint (DR-010).
///   - CredentialEmailStatus constrained to <c>Pending|Sent|Failed|Bounced</c> (AC-1).
///   - PasswordHash is nullable to support the invite flow (AC-2): account exists before
///     credentials are set.
///   - Soft-delete: Status = 'Deactivated' — no hard DELETE (DR-010).
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", t =>
        {
            // RBAC: only recognised role values permitted in the database (AC-4)
            t.HasCheckConstraint("ck_users_role", "role IN ('Patient','Staff','Admin')");

            // Soft-delete lifecycle states (DR-010)
            t.HasCheckConstraint("ck_users_status", "status IN ('Active','Deactivated')");

            // SendGrid delivery state values (AC-1)
            t.HasCheckConstraint(
                "ck_users_credential_email_status",
                "credential_email_status IN ('Pending','Sent','Failed','Bounced')");
        });

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedOnAdd();

        builder.Property(u => u.Email)
               .HasMaxLength(320)
               .IsRequired();

        // PasswordHash is nullable: null until credentials are set up via invite token (US_012, AC-2)
        builder.Property(u => u.PasswordHash)
               .HasMaxLength(500)
               .IsRequired(false);

        builder.Property(u => u.Name)
               .HasMaxLength(200)
               .IsRequired(false);

        builder.Property(u => u.Role)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired();

        // Status reuses PatientStatus enum (Active/Deactivated) for Staff and Admin (DR-010)
        builder.Property(u => u.Status)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired();

        // Tracks SendGrid invite email delivery state (US_012, AC-1 edge case)
        builder.Property(u => u.CredentialEmailStatus)
               .HasMaxLength(20)
               .HasDefaultValue("Pending")
               .IsRequired();

        builder.Property(u => u.LastLoginAt)
               .HasColumnType("timestamp with time zone");

        builder.Property(u => u.CreatedAt)
               .HasColumnType("timestamp with time zone")
               .HasDefaultValueSql("now()");

        // Email uniqueness is enforced via a case-insensitive PostgreSQL expression index
        // uq_users_email_lower ON users (lower(email)) — managed in migration SQL (AC-3).
        // EF Core does not support functional/expression indexes via HasIndex(), so no
        // HasIndex() call is made here.
    }
}
