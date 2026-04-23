using MediatR;
using Propel.Domain.Interfaces;
using Propel.Modules.Notification.Models;

namespace Propel.Modules.Notification.Queries;

/// <summary>
/// MediatR query that retrieves the current reminder interval configuration from
/// <c>system_settings</c> (US_033, AC-3).
/// Returns a <see cref="ReminderSettingsDto"/> containing the configured interval hours.
/// </summary>
public sealed record GetReminderSettingsQuery : IRequest<ReminderSettingsDto>;

/// <summary>
/// Handles <see cref="GetReminderSettingsQuery"/> — delegates to
/// <see cref="ISystemSettingsRepository"/> to read the current reminder intervals.
/// </summary>
public sealed class GetReminderSettingsQueryHandler
    : IRequestHandler<GetReminderSettingsQuery, ReminderSettingsDto>
{
    private readonly ISystemSettingsRepository _settingsRepo;

    public GetReminderSettingsQueryHandler(ISystemSettingsRepository settingsRepo)
    {
        _settingsRepo = settingsRepo;
    }

    public async Task<ReminderSettingsDto> Handle(
        GetReminderSettingsQuery request,
        CancellationToken cancellationToken)
    {
        var intervals = await _settingsRepo.GetReminderIntervalsAsync(cancellationToken);
        return new ReminderSettingsDto(intervals);
    }
}
