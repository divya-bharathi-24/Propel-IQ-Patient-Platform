using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IIntakeRepository"/> (US_017, task_002).
/// All data access methods are patient-scoped — every query filters on both
/// <c>appointmentId</c> AND <c>patientId</c> to prevent cross-patient data leakage (OWASP A01).
/// </summary>
public sealed class IntakeRepository : IIntakeRepository
{
    private readonly AppDbContext _context;

    public IntakeRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public Task<IntakeRecord?> GetByAppointmentIdAsync(
        Guid appointmentId,
        Guid patientId,
        CancellationToken cancellationToken = default)
        => _context.IntakeRecords
            .FirstOrDefaultAsync(
                i => i.AppointmentId == appointmentId && i.PatientId == patientId,
                cancellationToken);

    /// <inheritdoc/>
    public async Task UpsertDraftAsync(
        Guid appointmentId,
        Guid patientId,
        JsonDocument draftData,
        CancellationToken cancellationToken = default)
    {
        var record = await _context.IntakeRecords
            .FirstOrDefaultAsync(
                i => i.AppointmentId == appointmentId && i.PatientId == patientId,
                cancellationToken);

        if (record is null)
        {
            // No existing record — create a draft-only shell so it can be retrieved later (AC-4)
            record = new IntakeRecord
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                AppointmentId = appointmentId,
                Source = IntakeSource.Manual,
                Demographics = JsonDocument.Parse("{}"),
                MedicalHistory = JsonDocument.Parse("{}"),
                Symptoms = JsonDocument.Parse("{}"),
                Medications = JsonDocument.Parse("{}"),
                DraftData = draftData,
                LastModifiedAt = DateTime.UtcNow
            };
            _context.IntakeRecords.Add(record);
        }
        else
        {
            // Update only the draft columns — never touch completedAt or the primary JSONB columns
            record.DraftData = draftData;
            record.LastModifiedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IntakeRecord> UpsertAsync(
        IntakeRecord record,
        CancellationToken cancellationToken = default)
    {
        // If the entity is already tracked (loaded by GetByAppointmentIdAsync in the same scope),
        // EF Core change tracking will detect the mutations and issue an UPDATE automatically.
        // If the entity is new (not tracked), add it so EF Core issues an INSERT.
        var entry = _context.Entry(record);
        if (entry.State == EntityState.Detached)
            _context.IntakeRecords.Add(record);

        await _context.SaveChangesAsync(cancellationToken);
        return record;
    }

    // ── US_030 — AI session resume & offline draft sync (task_002) ───────────

    /// <inheritdoc/>
    public Task<bool> ExistsForPatientAsync(
        Guid appointmentId,
        Guid patientId,
        CancellationToken cancellationToken = default)
        => _context.IntakeRecords
            .AnyAsync(
                r => r.AppointmentId == appointmentId && r.PatientId == patientId,
                cancellationToken);

    // ── US_029 — Manual intake form (task_002) ────────────────────────────────

    /// <inheritdoc/>
    public Task<IntakeRecord?> GetManualDraftAsync(
        Guid appointmentId,
        Guid patientId,
        CancellationToken cancellationToken = default)
        => _context.IntakeRecords
            .OrderByDescending(r => r.Id)   // latest draft wins when multiple exist (edge-case guard)
            .FirstOrDefaultAsync(
                r => r.AppointmentId == appointmentId
                  && r.PatientId == patientId
                  && r.Source == IntakeSource.Manual
                  && r.CompletedAt == null,
                cancellationToken);

    /// <inheritdoc/>
    public Task<IntakeRecord?> GetAiExtractedAsync(
        Guid appointmentId,
        Guid patientId,
        CancellationToken cancellationToken = default)
        => _context.IntakeRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.AppointmentId == appointmentId
                  && r.PatientId == patientId
                  && r.Source == IntakeSource.AI
                  && r.CompletedAt != null,
                cancellationToken);

    /// <inheritdoc/>
    public async Task RemoveAsync(
        IntakeRecord record,
        CancellationToken cancellationToken = default)
    {
        _context.IntakeRecords.Remove(record);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
