using MediatR;

namespace Propel.Modules.Risk.Commands;

/// <summary>
/// MediatR command that triggers no-show risk score calculation and UPSERT for a single appointment
/// (us_031, task_002, AC-1, AC-4).
/// <para>
/// Dispatched by:
/// <list type="bullet">
///   <item><c>NoShowRiskCalculationBackgroundService</c> — batch hourly processing of upcoming booked appointments.</item>
///   <item><c>BookingConfirmedRiskEventHandler</c> — immediately after a new booking is persisted (fire-and-forget via MediatR notification).</item>
/// </list>
/// </para>
/// </summary>
/// <param name="AppointmentId">Primary key of the appointment to score.</param>
public sealed record CalculateNoShowRiskCommand(Guid AppointmentId) : IRequest<Unit>;
