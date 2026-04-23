using MediatR;
using Propel.Modules.Admin.Dtos;

namespace Propel.Modules.Admin.Queries;

/// <summary>
/// Returns all Staff and Admin user accounts for the Admin user management list (US_045, AC-1).
/// Excludes Patient-role accounts — this endpoint manages the clinical team only.
/// </summary>
public sealed record GetManagedUsersQuery : IRequest<List<ManagedUserDto>>;
