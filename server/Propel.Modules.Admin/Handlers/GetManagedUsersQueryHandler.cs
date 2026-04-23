using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using Propel.Modules.Admin.Dtos;
using Propel.Modules.Admin.Queries;

namespace Propel.Modules.Admin.Handlers;

/// <summary>
/// Handles <see cref="GetManagedUsersQuery"/>:
/// queries the Users table for all Staff and Admin accounts, ordered by name (US_045, AC-1).
/// Patients are excluded — patient management is handled through <c>IPatientRepository</c>.
/// </summary>
public sealed class GetManagedUsersQueryHandler
    : IRequestHandler<GetManagedUsersQuery, List<ManagedUserDto>>
{
    private readonly IUserRepository _userRepo;
    private readonly ILogger<GetManagedUsersQueryHandler> _logger;

    public GetManagedUsersQueryHandler(
        IUserRepository userRepo,
        ILogger<GetManagedUsersQueryHandler> logger)
    {
        _userRepo = userRepo;
        _logger = logger;
    }

    public async Task<List<ManagedUserDto>> Handle(
        GetManagedUsersQuery request,
        CancellationToken cancellationToken)
    {
        var users = await _userRepo.GetManagedUsersAsync(cancellationToken);

        _logger.LogInformation("GetManagedUsers: returned {Count} staff/admin accounts.", users.Count);

        return users.Select(u => new ManagedUserDto(
            u.Id,
            u.Name ?? string.Empty,
            u.Email,
            u.Role,
            u.Status,
            u.LastLoginAt.HasValue ? new DateTimeOffset(u.LastLoginAt.Value, TimeSpan.Zero) : null
        )).ToList();
    }
}
