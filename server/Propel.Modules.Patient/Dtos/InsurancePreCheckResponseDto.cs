using Propel.Domain.Enums;

namespace Propel.Modules.Patient.Dtos;

/// <summary>
/// Result returned by <c>POST /api/insurance/pre-check</c> (US_022, task_002, AC-1, AC-2).
/// <para>
/// <see cref="Status"/> is a string-serialised <see cref="InsuranceValidationResult"/> value.
/// <see cref="Guidance"/> is a human-readable message displayed in the booking wizard Step 3.
/// Guidance strings are defined as constants in <see cref="Propel.Modules.Patient.Services.InsuranceSoftCheckClassifier"/>
/// (single source of truth — never duplicated in the controller or frontend).
/// </para>
/// </summary>
/// <param name="Status">One of: Verified | NotRecognized | Incomplete | CheckPending.</param>
/// <param name="Guidance">Human-readable guidance text for display in the booking wizard.</param>
public sealed record InsurancePreCheckResponseDto(string Status, string Guidance);
