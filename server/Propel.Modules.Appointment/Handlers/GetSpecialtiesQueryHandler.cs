using MediatR;
using Propel.Domain.Interfaces;
using Propel.Modules.Appointment.Dtos;
using Propel.Modules.Appointment.Queries;

namespace Propel.Modules.Appointment.Handlers;

/// <summary>
/// Handles <see cref="GetSpecialtiesQuery"/> for <c>GET /api/appointments/specialties</c> (US_018).
/// Returns all specialty reference rows ordered by name via <see cref="ISpecialtyRepository"/>.
/// </summary>
public sealed class GetSpecialtiesQueryHandler
    : IRequestHandler<GetSpecialtiesQuery, IReadOnlyList<SpecialtyDto>>
{
    private readonly ISpecialtyRepository _repository;

    public GetSpecialtiesQueryHandler(ISpecialtyRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<SpecialtyDto>> Handle(
        GetSpecialtiesQuery request,
        CancellationToken cancellationToken)
    {
        var specialties = await _repository.GetAllAsync(cancellationToken);
        return specialties
            .Select(s => new SpecialtyDto(s.Id, s.Name))
            .ToList()
            .AsReadOnly();
    }
}
