using MediatR;
using Propel.Modules.AI.Interfaces;

namespace Propel.Modules.Clinical.Commands;

/// <summary>
/// MediatR command to run the AI semantic de-duplication pipeline for a patient's
/// extracted clinical data fields (EP-008-I/us_041, task_003, AC-1, AC-2).
/// <para>
/// <c>PatientId</c> MUST be resolved from the verified JWT claim or from a trusted
/// internal system event — never from an untrusted request body (OWASP A01).
/// </para>
/// </summary>
public sealed record DeduplicatePatientDataCommand(
    /// <summary>
    /// The patient whose <c>ExtractedData</c> records (with <c>ProcessingStatus = Completed</c>)
    /// will be de-duplicated by <see cref="IPatientDeduplicationService"/>.
    /// </summary>
    Guid PatientId) : IRequest<DeduplicationResult>;
