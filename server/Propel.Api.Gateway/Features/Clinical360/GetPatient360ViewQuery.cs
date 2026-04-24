using MediatR;
using Propel.Modules.Clinical.Queries;

namespace Propel.Api.Gateway.Features.Clinical360;

/// <summary>
/// CQRS read query: aggregates <see cref="ExtractedData"/> and <see cref="ClinicalDocument"/>
/// into a 360-degree patient view (AC-1, AC-2).
/// Staff <c>userId</c> is included for future audit enrichment but is NOT used to filter data.
/// </summary>
public sealed record GetPatient360ViewQuery(Guid PatientId, Guid StaffUserId)
    : IRequest<Patient360ViewDto>;
