using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPatientDashboardRepository"/> (US_016, TASK_002).
/// <para>
/// Uses three sequential <c>AsNoTracking()</c> projection queries to aggregate the dashboard
/// response (NFR-001: 2-second p95 target). No <c>Include()</c> calls are used; all required
/// columns are pulled via <c>Select()</c> projections to avoid N+1 and over-fetching (AD-2).
/// </para>
/// <list type="number">
///   <item><b>Step A</b> — Upcoming appointments scoped by <c>PatientId</c>, excluding
///         <c>Completed</c> and <c>Cancelled</c> statuses, ordered by date + time.
///         A correlated <c>NOT EXISTS</c> sub-query determines <c>HasPendingIntake</c>.</item>
///   <item><b>Step B</b> — Clinical document history scoped by <c>PatientId</c>,
///         ordered descending by upload date. All <c>ProcessingStatus</c> values including
///         <c>Failed</c> are included so the client can show a retry option.</item>
///   <item><b>Step C</b> — 360° view-verified flag derived from
///         <c>patients.view_verified_at IS NOT NULL</c> (AC-4, FR-047).</item>
/// </list>
/// <para>
/// All queries are scoped by the <c>patientId</c> extracted from the JWT in the controller
/// (OWASP A01 — Broken Access Control). This method never accepts a patient identifier
/// from user-controlled input.
/// </para>
/// </summary>
public sealed class PatientDashboardRepository : IPatientDashboardRepository
{
    private readonly AppDbContext _dbContext;

    public PatientDashboardRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PatientDashboardReadModel> GetDashboardAsync(
        Guid patientId,
        CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // ── Step A: Upcoming appointments with pending-intake flag ──────────────
        // Filters status NOT IN (Completed, Cancelled) and date >= today.
        // Correlated sub-query: HasPendingIntake = true when no completed IntakeRecord exists
        // for the appointment (i.e. completedAt IS NULL on all intake records for that appointment).
        // EF Core translates the nested Any() to a SQL NOT EXISTS sub-select (server-side evaluation).
        var upcomingAppointments = await _dbContext.Appointments
            .Where(a => a.PatientId == patientId
                     && a.Status != AppointmentStatus.Completed
                     && a.Status != AppointmentStatus.Cancelled
                     && a.Date >= today)
            .OrderBy(a => a.Date)
            .ThenBy(a => a.TimeSlotStart)
            .Select(a => new UpcomingAppointmentReadModel(
                a.Id,
                a.Date,
                a.TimeSlotStart,
                a.Specialty.Name,
                a.Status.ToString(),
                !_dbContext.IntakeRecords.Any(i => i.AppointmentId == a.Id && i.CompletedAt != null)))
            .AsNoTracking()
            .ToListAsync(ct);

        // ── Step B: Clinical document upload history ────────────────────────────
        // All ProcessingStatus values (including Failed) are included — the client
        // uses Failed status to present a retry option (task edge case).
        var documents = await _dbContext.ClinicalDocuments
            .Where(d => d.PatientId == patientId)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new DocumentReadModel(
                d.Id,
                d.FileName,
                d.UploadedAt,
                d.ProcessingStatus.ToString()))
            .AsNoTracking()
            .ToListAsync(ct);

        // ── Step C: 360° view-verified status ──────────────────────────────────
        // Derives boolean from patients.view_verified_at IS NOT NULL (AC-4, FR-047).
        // Column and migration are managed by US_016 / TASK_003.
        var viewVerified = await _dbContext.Patients
            .Where(p => p.Id == patientId)
            .Select(p => p.ViewVerifiedAt != null)
            .SingleAsync(ct);

        return new PatientDashboardReadModel(
            UpcomingAppointments: upcomingAppointments,
            Documents: documents,
            ViewVerified: viewVerified);
    }

    /// <inheritdoc />
    public Task<bool> HasEmailDeliveryFailureAsync(Guid patientId, CancellationToken ct = default)
    {
        return _dbContext.Notifications
            .AsNoTracking()
            .AnyAsync(
                n => n.PatientId == patientId
                  && n.Status == NotificationStatus.Failed
                  && n.RetryCount >= 2,
                ct);
    }
}
