namespace Propel.Modules.Patient.Dtos;

/// <summary>
/// Request body for <c>POST /api/insurance/pre-check</c> (US_022, task_002, AC-1, AC-4).
/// <para>
/// Both fields are intentionally nullable — the endpoint classifies missing values as
/// <c>Incomplete</c> rather than rejecting the request (AC-4: skip-step flow).
/// </para>
/// </summary>
/// <param name="ProviderName">Insurance provider name; may be null when the patient skips the field.</param>
/// <param name="InsuranceId">Member ID; may be null when the patient skips the field.</param>
public sealed record InsurancePreCheckRequestDto(
    string? ProviderName,
    string? InsuranceId);
