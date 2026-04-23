using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Enums;
using Propel.Modules.Risk.Events;

namespace Propel.Api.Gateway.Infrastructure.Handlers;

/// <summary>
/// Stub handler for <see cref="InterventionAcceptedNotification"/> (US_032, AC-2).
/// Triggers the relevant downstream action based on <c>InterventionType</c>:
/// <list type="bullet">
///   <item><c>AdditionalReminder</c> — logs intent for ad-hoc patient reminder; full reminder
///         dispatch is delivered in EP-006/us_033 via <c>INotificationService.SendAdHocReminderAsync</c>.</item>
///   <item><c>CallbackRequest</c> — logs the callback request for Staff follow-up tracking.</item>
/// </list>
/// <para>
/// AG-6 compliance: wraps handler body in try/catch; logs Warning on failure; never throws.
/// </para>
/// </summary>
public sealed class InterventionAcceptedNotificationHandler
    : INotificationHandler<InterventionAcceptedNotification>
{
    private readonly ILogger<InterventionAcceptedNotificationHandler> _logger;

    public InterventionAcceptedNotificationHandler(ILogger<InterventionAcceptedNotificationHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(InterventionAcceptedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            switch (notification.InterventionType)
            {
                case InterventionType.AdditionalReminder:
                    // TODO (EP-006/us_033): Replace with INotificationService.SendAdHocReminderAsync()
                    // when the full reminder integration is delivered.
                    _logger.LogInformation(
                        "InterventionAccepted_AdHocReminder: AppointmentId={AppointmentId} StaffId={StaffId} — ad-hoc reminder queued (stub).",
                        notification.AppointmentId, notification.StaffId);
                    break;

                case InterventionType.CallbackRequest:
                    _logger.LogInformation(
                        "InterventionAccepted_CallbackRequest: AppointmentId={AppointmentId} StaffId={StaffId} — callback request logged for Staff follow-up.",
                        notification.AppointmentId, notification.StaffId);
                    break;

                default:
                    _logger.LogWarning(
                        "InterventionAccepted_UnknownType: AppointmentId={AppointmentId} Type={Type}",
                        notification.AppointmentId, notification.InterventionType);
                    break;
            }
        }
        catch (Exception ex)
        {
            // AG-6: handler must never throw.
            _logger.LogWarning(
                ex,
                "InterventionAccepted_HandlerFailed: AppointmentId={AppointmentId} Type={Type}",
                notification.AppointmentId, notification.InterventionType);
        }

        return Task.CompletedTask;
    }
}
