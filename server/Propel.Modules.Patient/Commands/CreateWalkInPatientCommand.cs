using MediatR;

namespace Propel.Modules.Patient.Commands;

/// <summary>
/// Creates a basic Patient record for the walk-in booking flow (US_012, AC-3).
/// Staff-only endpoint. Returns 409 with <c>existingPatientId</c> when the email
/// already exists — enabling the frontend to offer a link-to-existing flow.
/// Validated by <c>CreateWalkInPatientValidator</c> before the handler is invoked.
/// </summary>
public sealed record CreateWalkInPatientCommand(
    string Name,
    string Email,
    string? Phone,
    Guid StaffId,
    string? IpAddress,
    string? CorrelationId
) : IRequest<CreateWalkInPatientResult>;

/// <summary>Returned to the controller on successful walk-in patient creation.</summary>
public sealed record CreateWalkInPatientResult(Guid PatientId);
