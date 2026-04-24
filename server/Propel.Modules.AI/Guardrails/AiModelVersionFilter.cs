using Microsoft.SemanticKernel;
using Propel.Modules.AI.Interfaces;
using Serilog;

namespace Propel.Modules.AI.Guardrails;

/// <summary>
/// Semantic Kernel <see cref="IPromptRenderFilter"/> that overrides the model ID in SK execution
/// settings with the live value from <see cref="ILiveAiModelConfig"/> (AIR-O03, US_050 AC-3).
/// <para>
/// Registered at priority –10 (before all other prompt render filters) so the model override
/// is in place before PII redaction or token budget filters run.
/// </para>
/// <para>
/// Behaviour:
/// <list type="number">
///   <item><description>Calls <see cref="ILiveAiModelConfig.GetModelVersionAsync"/> — Redis-backed with
///     60-second in-memory cache; falls back to <c>AiResilience:DefaultModelVersion</c>.</description></item>
///   <item><description>If the resolved version differs from the current execution settings model ID,
///     replaces the <c>OpenAIPromptExecutionSettings</c> in the arguments map.</description></item>
///   <item><description>Passes control to <c>next</c> without modification if model version is unchanged
///     or execution settings are absent.</description></item>
/// </list>
/// </para>
/// <para>
/// An operator can update Redis key <c>ai:config:model_version</c> and the change propagates
/// to all SK invocations within 60 seconds — no application restart required.
/// </para>
/// </summary>
public sealed class AiModelVersionFilter : IPromptRenderFilter
{
    private readonly ILiveAiModelConfig _config;

    public AiModelVersionFilter(ILiveAiModelConfig config)
    {
        _config = config;
    }

    /// <inheritdoc/>
    public async Task OnPromptRenderAsync(
        PromptRenderContext context,
        Func<PromptRenderContext, Task> next)
    {
        string liveModelVersion = await _config.GetModelVersionAsync(context.CancellationToken);

        // Mutate ModelId on each execution settings entry before the prompt renders.
        // PromptExecutionSettings.ModelId is settable (not frozen yet at IPromptRenderFilter stage).
        if (context.Arguments.ExecutionSettings is { Count: > 0 } settings)
        {
            foreach (var entry in settings.Values)
            {
                if (!string.Equals(entry.ModelId, liveModelVersion, StringComparison.Ordinal))
                {
                    Log.Debug(
                        "AiModelVersionFilter_Override: model overridden from {OldModel} to {NewModel} (AIR-O03).",
                        entry.ModelId ?? "(unset)",
                        liveModelVersion);

                    entry.ModelId = liveModelVersion;
                }
            }
        }

        await next(context);
    }
}
