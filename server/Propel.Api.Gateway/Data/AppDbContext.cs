using Microsoft.EntityFrameworkCore;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Data;

/// <summary>
/// Application database context (task_002).
/// All entity type configurations are auto-discovered via
/// <see cref="ModelBuilder.ApplyConfigurationsFromAssembly"/> — no manual registration required.
/// Soft-delete is enforced in <see cref="SaveChangesAsync"/>: hard DELETEs on
/// <see cref="Patient"/> and <see cref="Appointment"/> entities are intercepted and
/// converted to status updates (DR-010, AC-2).
/// PHI value converters for <see cref="Patient.Name"/>, <see cref="Patient.Phone"/>, and
/// <see cref="Patient.DateOfBirth"/> are applied in <see cref="OnModelCreating"/> via the
/// injected <see cref="IPhiEncryptionService"/> (NFR-004, NFR-013).
/// </summary>
public sealed class AppDbContext : DbContext
{
    private readonly IPhiEncryptionService _phiEncryption;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        IPhiEncryptionService phiEncryption) : base(options)
    {
        _phiEncryption = phiEncryption;
    }

    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();
    public DbSet<Specialty> Specialties => Set<Specialty>();

    // ── US_007 clinical / AI / queue entities (task_002) ─────────────────────
    public DbSet<IntakeRecord> IntakeRecords => Set<IntakeRecord>();
    public DbSet<ClinicalDocument> ClinicalDocuments => Set<ClinicalDocument>();
    public DbSet<ExtractedData> ExtractedData => Set<ExtractedData>();
    public DbSet<DataConflict> DataConflicts => Set<DataConflict>();
    public DbSet<MedicalCode> MedicalCodes => Set<MedicalCode>();
    public DbSet<NoShowRisk> NoShowRisks => Set<NoShowRisk>();
    public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();

    // ── US_008 audit / notification / insurance / calendar entities (task_002) ──
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<InsuranceValidation> InsuranceValidations => Set<InsuranceValidation>();
    public DbSet<CalendarSync> CalendarSyncs => Set<CalendarSync>();

    // ── US_011 authentication — refresh tokens (task_002) ────────────────────
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // ── US_012 admin account management — credential setup tokens (task_002) ──
    public DbSet<CredentialSetupToken> CredentialSetupTokens => Set<CredentialSetupToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // TODO: Uncomment when pgvector is installed and AI features are ready
        // Enable pgvector extension — migration will emit CREATE EXTENSION IF NOT EXISTS vector (AC-2)
        // modelBuilder.HasPostgresExtension("vector");  // COMMENTED OUT - AI features disabled temporarily

        // Auto-discovers all IEntityTypeConfiguration<T> implementations in this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // ── PHI value converters (NFR-004, NFR-013) ────────────────────────────
        // Applied after ApplyConfigurationsFromAssembly so structural config (table, keys,
        // indexes) is already in place. Converters transparently encrypt on write and
        // decrypt on read without changing the domain model.
        modelBuilder.Entity<Patient>(entity =>
        {
            entity.Property(p => p.Name)
                .HasConversion(
                    v => _phiEncryption.Encrypt(v),
                    v => _phiEncryption.Decrypt(v));

            entity.Property(p => p.Phone)
                .HasConversion(
                    v => _phiEncryption.Encrypt(v),
                    v => _phiEncryption.Decrypt(v));

            // DateOfBirth (DateOnly) serialised as "yyyy-MM-dd" before encryption,
            // parsed back after decryption.
            entity.Property(p => p.DateOfBirth)
                .HasConversion(
                    v => _phiEncryption.Encrypt(v.ToString("yyyy-MM-dd")),
                    v => DateOnly.Parse(_phiEncryption.Decrypt(v)));
        });
    }

    /// <summary>
    /// Intercepts <see cref="EntityState.Deleted"/> entries for <see cref="Patient"/> and
    /// <see cref="Appointment"/> and converts them to soft-deletes (DR-010, AC-2).
    /// No hard DELETE SQL is ever issued for these entity types.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Deleted)
            {
                if (entry.Entity is Patient patient)
                {
                    entry.State = EntityState.Modified;
                    patient.Status = PatientStatus.Deactivated;
                }
                else if (entry.Entity is Appointment appointment)
                {
                    entry.State = EntityState.Modified;
                    appointment.Status = AppointmentStatus.Cancelled;
                }
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
