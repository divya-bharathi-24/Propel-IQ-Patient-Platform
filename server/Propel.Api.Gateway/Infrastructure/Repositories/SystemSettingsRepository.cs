using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ISystemSettingsRepository"/> (US_033, FR-032, AC-3).
/// Reads and writes system-wide configurable settings from the <c>system_settings</c> table.
/// Uses <see cref="AppDbContext"/> injected via the scoped DI lifetime — the scheduler
/// resolves this via <c>IServiceScopeFactory</c> per tick cycle to avoid captive-dependency
/// issues in the singleton-lifetime <c>BackgroundService</c> (AD-1).
/// All queries use parameterised LINQ — no raw SQL interpolation (OWASP A03).
/// </summary>
public sealed class SystemSettingsRepository : ISystemSettingsRepository
{
    private const string ReminderIntervalsKey = "reminder_interval_hours";
    private static readonly int[] DefaultIntervals = [48, 24, 2];

    private readonly AppDbContext _db;

    public SystemSettingsRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<int[]> GetReminderIntervalsAsync(CancellationToken cancellationToken = default)
    {
        var setting = await _db.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == ReminderIntervalsKey, cancellationToken);

        if (setting is null)
            return DefaultIntervals;

        try
        {
            var intervals = JsonSerializer.Deserialize<int[]>(setting.Value);
            return intervals is { Length: > 0 } ? intervals : DefaultIntervals;
        }
        catch (JsonException)
        {
            // Malformed value — fall back to defaults rather than crashing the scheduler.
            return DefaultIntervals;
        }
    }

    /// <inheritdoc/>
    public async Task SetReminderIntervalsAsync(
        int[] intervalHours,
        Guid updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        var utcNow  = DateTime.UtcNow;
        var newJson = JsonSerializer.Serialize(intervalHours);

        var existing = await _db.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == ReminderIntervalsKey, cancellationToken);

        if (existing is null)
        {
            _db.SystemSettings.Add(new SystemSetting
            {
                Key             = ReminderIntervalsKey,
                Value           = newJson,
                UpdatedAt       = utcNow,
                UpdatedByUserId = updatedByUserId
            });
        }
        else
        {
            existing.Value           = newJson;
            existing.UpdatedAt       = utcNow;
            existing.UpdatedByUserId = updatedByUserId;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
