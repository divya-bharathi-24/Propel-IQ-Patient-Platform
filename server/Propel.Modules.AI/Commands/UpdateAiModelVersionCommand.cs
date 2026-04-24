using MediatR;

namespace Propel.Modules.AI.Commands;

/// <summary>
/// Admin-only command to update the active AI model version (EP-010/us_050, AC-3, AIR-O03).
/// <para>
/// On success, the handler writes <paramref name="ModelVersion"/> to Redis key
/// <c>ai:config:model_version</c>. The change is effective within 60 seconds (the
/// <c>RedisLiveAiModelConfig</c> cache TTL) — meeting the 5-minute effectiveness SLA (AIR-O03).
/// </para>
/// Model version is validated against <c>AiResilience:AllowedModelVersions</c> whitelist before writing.
/// </summary>
/// <param name="ModelVersion">New model version identifier (e.g., "gpt-4o-2024-11-20").</param>
/// <param name="RequestingUserId">Authenticated admin user ID, resolved from JWT by the controller (OWASP A01).</param>
public sealed record UpdateAiModelVersionCommand(string ModelVersion, Guid? RequestingUserId) : IRequest<UpdateAiModelVersionResult>;

/// <summary>
/// Result returned by <see cref="UpdateAiModelVersionCommand"/> handler.
/// </summary>
/// <param name="Success"><c>true</c> when the model version was successfully written to Redis.</param>
/// <param name="ActiveModelVersion">The now-active model version string.</param>
/// <param name="ErrorMessage">Non-null when <see cref="Success"/> is <c>false</c> (validation failure).</param>
public sealed record UpdateAiModelVersionResult(
    bool Success,
    string ActiveModelVersion,
    string? ErrorMessage = null);
