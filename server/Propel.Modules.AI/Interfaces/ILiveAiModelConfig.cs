namespace Propel.Modules.AI.Interfaces;

/// <summary>
/// Provides the live AI model version that should be used for the current invocation (AIR-O03, US_050 AC-3).
/// <para>
/// Reads the current value from the Redis key <c>ai:config:model_version</c> with a
/// 60-second in-memory cache. Falls back to <c>AiResilience:DefaultModelVersion</c> from
/// <c>appsettings.json</c> when Redis is unavailable or the key is absent.
/// </para>
/// <para>
/// Updating the Redis key is reflected in SK invocations within 60 seconds —
/// no application restart required (AIR-O03 hot-swap requirement).
/// </para>
/// </summary>
public interface ILiveAiModelConfig
{
    /// <summary>
    /// Returns the current AI model version string (e.g. <c>"gpt-4o"</c>, <c>"gpt-4o-mini"</c>).
    /// Never returns null or empty; falls back to the configured default.
    /// </summary>
    Task<string> GetModelVersionAsync(CancellationToken ct = default);
}
