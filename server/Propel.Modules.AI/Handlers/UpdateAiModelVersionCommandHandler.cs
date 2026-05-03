using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.AI.Commands;
using Propel.Modules.AI.Options;
using Serilog;
using StackExchange.Redis;

namespace Propel.Modules.AI.Handlers;

/// <summary>
/// Handles <see cref="UpdateAiModelVersionCommand"/>: validates model version against
/// the configured whitelist, writes to Redis key <c>ai:config:model_version</c>,
/// and emits an immutable AuditLog entry (EP-010/us_050, AC-3, AIR-O03).
/// <para>
/// The change is effective within 60 seconds (<c>RedisLiveAiModelConfig</c> cache TTL),
/// meeting the 5-minute model-swap SLA (AIR-O03).
/// </para>
/// Returns HTTP 400 equivalent via <see cref="UpdateAiModelVersionResult.Success"/> = false
/// when the requested version is not in the <c>AllowedModelVersions</c> whitelist (OWASP A03).
/// </summary>
public sealed class UpdateAiModelVersionCommandHandler
    : IRequestHandler<UpdateAiModelVersionCommand, UpdateAiModelVersionResult>
{
    private const string ModelVersionRedisKey = "ai:config:model_version";

    private readonly IConnectionMultiplexer?               _redis;
    private readonly IOptionsMonitor<AiResilienceSettings> _options;
    private readonly IAuditLogRepository                   _auditLog;
    private readonly ILogger<UpdateAiModelVersionCommandHandler> _logger;

    public UpdateAiModelVersionCommandHandler(
        IOptionsMonitor<AiResilienceSettings> options,
        IAuditLogRepository auditLog,
        ILogger<UpdateAiModelVersionCommandHandler> logger,
        IConnectionMultiplexer? redis = null)
    {
        _redis    = redis;
        _options  = options;
        _auditLog = auditLog;
        _logger   = logger;
    }

    public async Task<UpdateAiModelVersionResult> Handle(
        UpdateAiModelVersionCommand request,
        CancellationToken cancellationToken)
    {
        var settings = _options.CurrentValue;

        // ── Whitelist validation (OWASP A03 — reject versions not in approved list) ──
        var allowed = settings.AllowedModelVersions;
        if (allowed.Length > 0 && !allowed.Contains(request.ModelVersion, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "UpdateAiModelVersion rejected: version={ModelVersion} not in AllowedModelVersions whitelist.",
                request.ModelVersion);
            return new UpdateAiModelVersionResult(
                Success: false,
                ActiveModelVersion: await ReadCurrentVersionAsync(settings),
                ErrorMessage: $"Model version '{request.ModelVersion}' is not in the allowed versions list.");
        }

        // ── Read current version for audit log before/after ──────────────────
        string previousVersion = await ReadCurrentVersionAsync(settings);

        // ── Write new version to Redis (or fail gracefully in development) ────
        if (_redis is null || !_redis.IsConnected)
        {
            _logger.LogWarning(
                "UpdateAiModelVersion_RedisUnavailable: Cannot update model version (Redis disabled in development mode).");
            return new UpdateAiModelVersionResult(
                Success: false,
                ActiveModelVersion: previousVersion,
                ErrorMessage: "Redis is not available (development mode). Model version updates require Redis.");
        }

        try
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync(ModelVersionRedisKey, request.ModelVersion).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex,
                "UpdateAiModelVersion_RedisFailed: Failed to write model version to Redis — key={Key} version={Version}",
                ModelVersionRedisKey, request.ModelVersion);
            return new UpdateAiModelVersionResult(
                Success: false,
                ActiveModelVersion: previousVersion,
                ErrorMessage: "Failed to update model version in configuration store. Please retry.");
        }

        // ── Emit immutable AuditLog entry (AD-7, AIR-O03) ────────────────────
        try
        {
            await _auditLog.AppendAsync(new AuditLog
            {
                Id         = Guid.NewGuid(),
                UserId     = request.RequestingUserId,
                Action     = "AiModelVersionUpdated",
                EntityType = "AiConfig",
                EntityId   = Guid.Empty,
                Details    = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    previousVersion,
                    newVersion = request.ModelVersion,
                    redisKey   = ModelVersionRedisKey
                })),
                Timestamp = DateTime.UtcNow
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Audit log failure must not roll back the Redis write (graceful degradation, AD-7).
            Log.Error(ex,
                "UpdateAiModelVersion_AuditLogFailed: Model version updated but audit log write failed — version={Version}",
                request.ModelVersion);
        }

        _logger.LogInformation(
            "UpdateAiModelVersion: model version changed from={Previous} to={New} by userId={UserId}",
            previousVersion, request.ModelVersion, request.RequestingUserId);

        return new UpdateAiModelVersionResult(
            Success: true,
            ActiveModelVersion: request.ModelVersion);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<string> ReadCurrentVersionAsync(AiResilienceSettings settings)
    {
        if (_redis is null || !_redis.IsConnected)
            return settings.DefaultModelVersion;

        try
        {
            var db  = _redis.GetDatabase();
            var val = await db.StringGetAsync(ModelVersionRedisKey).ConfigureAwait(false);
            return val.HasValue && !val.IsNullOrEmpty ? (string)val! : settings.DefaultModelVersion;
        }
        catch
        {
            return settings.DefaultModelVersion;
        }
    }
}
