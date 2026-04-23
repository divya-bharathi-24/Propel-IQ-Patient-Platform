using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using Propel.Modules.Admin.Dtos;
using Propel.Modules.Admin.Queries;

namespace Propel.Modules.Admin.Handlers;

/// <summary>
/// Handles <see cref="SearchPatientsQuery"/> for <c>GET /api/staff/patients/search</c> (US_026, AC-1).
/// <list type="number">
///   <item><b>Step 1 — Query repository</b>: delegates to <see cref="IStaffWalkInRepository.SearchPatientsAsync"/>
///         which performs a parameterised <c>ILIKE</c> search on <c>name</c> and exact match on
///         <c>date_of_birth::text</c>. Max 20 results (OWASP A03 — no raw string concatenation).</item>
///   <item><b>Step 2 — Map to DTO</b>: projects domain <c>Patient</c> entities to
///         <see cref="PatientSearchResultDto"/> containing only the non-sensitive fields needed
///         for the live-search UI.</item>
/// </list>
/// </summary>
public sealed class SearchPatientsQueryHandler
    : IRequestHandler<SearchPatientsQuery, IReadOnlyList<PatientSearchResultDto>>
{
    private const int MaxResults = 20;

    private readonly IStaffWalkInRepository _walkInRepo;
    private readonly ILogger<SearchPatientsQueryHandler> _logger;

    public SearchPatientsQueryHandler(
        IStaffWalkInRepository walkInRepo,
        ILogger<SearchPatientsQueryHandler> logger)
    {
        _walkInRepo = walkInRepo;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PatientSearchResultDto>> Handle(
        SearchPatientsQuery request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Staff patient search: query={Query} maxResults={MaxResults}",
            request.Query, MaxResults);

        var patients = await _walkInRepo.SearchPatientsAsync(
            request.Query,
            MaxResults,
            cancellationToken);

        return patients
            .Select(p => new PatientSearchResultDto(p.Id, p.Name, p.DateOfBirth, p.Email))
            .ToList()
            .AsReadOnly();
    }
}
