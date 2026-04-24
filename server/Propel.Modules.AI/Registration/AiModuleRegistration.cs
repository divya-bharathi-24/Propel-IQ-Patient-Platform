using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML.Tokenizers;
using Propel.Modules.AI.Guardrails;
using Propel.Modules.AI.Interfaces;
using Propel.Modules.AI.Options;
using Propel.Modules.AI.Services;
using Serilog;
using StackExchange.Redis;

namespace Propel.Modules.AI.Registration;

/// <summary>
/// Registers the AI operational resilience filter chain on the DI container (US_050, AIR-O01, AIR-O02, AIR-O03).
/// <para>
/// Filters are added in priority order for the Semantic Kernel filter pipeline:
/// <list type="number">
///   <item><description>Priority −10: <see cref="AiModelVersionFilter"/> (<c>IPromptRenderFilter</c>) — live model hot-swap (AIR-O03).</description></item>
///   <item><description>Priority  10: <see cref="TokenBudgetFilter"/> (<c>IPromptRenderFilter</c>) — 8,000-token truncation (AIR-O01).</description></item>
///   <item><description>Priority  50: <see cref="CircuitBreakerFilter"/> (<c>IFunctionInvocationFilter</c>) — Redis-backed open/closed (AIR-O02).</description></item>
/// </list>
/// </para>
/// <para>
/// Call <see cref="AddAiOperationalResilience"/> from <c>Program.cs</c> after the existing
/// EP-010/us_049 safety guardrail registrations.
/// </para>
/// </summary>
public static class AiModuleRegistration
{
    /// <summary>
    /// Registers AI operational resilience services and SK filters.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configuration">Application configuration (binds <c>"AiResilience"</c> section).</param>
    public static IServiceCollection AddAiOperationalResilience(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Bind AiResilienceSettings (AIR-O01, AIR-O02, AIR-O03) ────────────
        services.Configure<AiResilienceSettings>(configuration.GetSection("AiResilience"));

        // ── TiktokenTokenizer singleton (GPT-4o / o200k_base encoding) ───────
        // TiktokenTokenizer is thread-safe and expensive to create — singleton is correct.
        services.AddSingleton(_ =>
        {
            try
            {
                return TiktokenTokenizer.CreateForModel("gpt-4o");
            }
            catch (Exception ex)
            {
                // Fail fast at startup if the tokenizer cannot be created.
                Log.Fatal(ex,
                    "AiModuleRegistration: failed to create TiktokenTokenizer for 'gpt-4o' — " +
                    "check Microsoft.ML.Tokenizers package and network access to encoding files.");
                throw;
            }
        });

        // ── ILiveAiModelConfig: Redis-backed model version with 60-s cache ───
        // Redis (IConnectionMultiplexer?) is resolved lazily so a null Redis in development
        // falls back gracefully to appsettings (RedisLiveAiModelConfig handles null).
        services.AddSingleton<ILiveAiModelConfig>(sp =>
        {
            IConnectionMultiplexer? redis = null;
            try
            {
                redis = sp.GetService<IConnectionMultiplexer>();
            }
            catch
            {
                // Development: IConnectionMultiplexer throws — pass null for graceful degradation.
            }

            return new RedisLiveAiModelConfig(
                redis,
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<AiResilienceSettings>>());
        });

        // ── Priority −10: AiModelVersionFilter (IPromptRenderFilter) ─────────
        // Runs before PiiRedactionFilter (priority 0) so the model ID is set
        // before any other prompt render filter runs.
        services.AddSingleton<AiModelVersionFilter>();

        // ── Priority 10: TokenBudgetFilter (IPromptRenderFilter) ─────────────
        // Runs after PiiRedactionFilter (priority 0) so the redacted prompt is counted,
        // ensuring token budget reflects the actual outbound payload.
        services.AddSingleton<TokenBudgetFilter>();

        // ── Priority 50: CircuitBreakerFilter (IFunctionInvocationFilter) ────
        // Checks open state before function invocation; tracks failures after.
        // Registered as singleton — Redis state is shared across all requests per instance.
        services.AddSingleton<CircuitBreakerFilter>(sp =>
        {
            IConnectionMultiplexer? redis = null;
            try
            {
                redis = sp.GetService<IConnectionMultiplexer>();
            }
            catch
            {
                // Development: IConnectionMultiplexer throws — pass null for graceful degradation.
            }

            return new CircuitBreakerFilter(redis);
        });

        Log.Information(
            "AI operational resilience registered (EP-010/us_050): " +
            "AiModelVersionFilter(−10), TokenBudgetFilter(10), CircuitBreakerFilter(50).");

        return services;
    }
}
