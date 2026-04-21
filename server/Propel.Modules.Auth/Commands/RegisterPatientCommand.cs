using MediatR;

namespace Propel.Modules.Auth.Commands;

/// <summary>
/// Registers a new patient account.
/// Input is validated by <c>RegistrationRequestValidator</c> before the handler is invoked.
/// </summary>
public sealed record RegisterPatientCommand(
    string Email,
    string Password,
    string Name,
    string Phone,
    DateOnly DateOfBirth
) : IRequest<RegisterPatientResult>;

/// <summary>Result returned to the controller on successful registration.</summary>
public sealed record RegisterPatientResult(Guid PatientId);
