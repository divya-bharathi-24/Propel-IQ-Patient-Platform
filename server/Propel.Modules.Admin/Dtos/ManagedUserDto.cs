using Propel.Domain.Enums;

namespace Propel.Modules.Admin.Dtos;

/// <summary>
/// API-safe DTO for user management responses (US_045, AC-1, AC-2).
/// Returned by GET /api/admin/users, POST /api/admin/users, PATCH /api/admin/users/{id},
/// and POST /api/admin/users/{id}/resend-credentials.
/// <para>
/// <see cref="EmailDeliveryFailed"/> is only populated in creation and resend responses;
/// it is omitted (null) from list and update responses.
/// </para>
/// </summary>
public sealed record ManagedUserDto(
    Guid Id,
    string Name,
    string Email,
    UserRole Role,
    PatientStatus Status,
    DateTimeOffset? LastLoginAt,
    bool? EmailDeliveryFailed = null
);
