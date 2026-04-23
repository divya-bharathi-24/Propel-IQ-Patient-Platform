using MediatR;
using Propel.Domain.Enums;

namespace Propel.Modules.Patient.Queries;

/// <summary>
/// Internal result produced by <see cref="RunInsuranceSoftCheckQuery"/> (US_022, task_002).
/// <para>
/// <see cref="Status"/> carries the classification outcome.
/// <see cref="Guidance"/> is the human-readable guidance text constant from
/// <see cref="Propel.Modules.Patient.Services.InsuranceSoftCheckClassifier"/>.
/// </para>
/// </summary>
public sealed record InsurancePreCheckResult(InsuranceValidationResult Status, string Guidance);

/// <summary>
/// MediatR query for <c>POST /api/insurance/pre-check</c> (US_022, task_002, AC-1).
/// <para>
/// <c>PatientId</c> is resolved from the JWT <c>sub</c> claim in the controller —
/// never accepted from the request body (OWASP A01 — Broken Access Control).
/// </para>
/// <para>
/// <c>ProviderName</c> and <c>InsuranceId</c> are intentionally nullable.
/// Missing fields are classified as <c>Incomplete</c> by the handler without
/// querying the database (AC-1, AC-4).
/// </para>
/// </summary>
/// <param name="ProviderName">Insurance provider name supplied by the patient; may be null.</param>
/// <param name="InsuranceId">Member ID supplied by the patient; may be null.</param>
/// <param name="PatientId">Resolved from JWT sub claim — used for structured correlation logging only.</param>
public sealed record RunInsuranceSoftCheckQuery(
    string? ProviderName,
    string? InsuranceId,
    Guid PatientId
) : IRequest<InsurancePreCheckResult>;
