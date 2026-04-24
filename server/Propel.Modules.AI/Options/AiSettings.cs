namespace Propel.Modules.AI.Options;

/// <summary>
/// Configuration options for the AI intake service, bound from the <c>"Ai"</c> section
/// of <c>appsettings.json</c> (AIR-O01, AIR-O02, AIR-O03).
/// <para>
/// API keys (OpenAI / Azure OpenAI) are NEVER stored here — they are read exclusively
/// from environment variables at runtime (OWASP A02 — Cryptographic Failures).
/// </para>
/// </summary>
public sealed class AiSettings
{
    /// <summary>
    /// OpenAI model identifier or Azure OpenAI deployment name (e.g. "gpt-4o").
    /// Overridden via env var <c>Ai__ModelDeploymentName</c> for per-environment configuration.
    /// </summary>
    public string ModelDeploymentName { get; set; } = "gpt-4o";

    /// <summary>
    /// Maximum token budget per request (AIR-O01). Default: 8,000.
    /// Applied to <c>OpenAIPromptExecutionSettings.MaxTokens</c> on every chat completion call.
    /// </summary>
    public int MaxTokensPerRequest { get; set; } = 8000;

    /// <summary>
    /// System prompt template version to load (AIR-O03).
    /// Resolved to <c>Prompts/intake-system-{IntakePromptVersion}.txt</c> at runtime.
    /// Update without redeployment by changing this value and restarting the process
    /// (or via live configuration reload in future iterations).
    /// </summary>
    public string IntakePromptVersion { get; set; } = "v1";

    /// <summary>
    /// Risk assessment prompt template version (AIR-O03, us_031, task_003).
    /// Resolved to <c>Prompts/risk-assessment-{RiskAssessmentPromptVersion}.txt</c> at runtime.
    /// Update without redeployment by changing this value and restarting the process.
    /// Override via env var <c>Ai__RiskAssessmentPromptVersion</c>.
    /// </summary>
    public string RiskAssessmentPromptVersion { get; set; } = "v1";

    /// <summary>
    /// Clinical extraction prompt template version (AIR-O03, US_040, task_003).
    /// Resolved to <c>Prompts/clinical-extraction-{ExtractionPromptVersion}.yaml</c> at runtime;
    /// falls back to <c>Prompts/clinical-extraction.yaml</c> for the default version.
    /// Override via env var <c>Ai__ExtractionPromptVersion</c>.
    /// </summary>
    public string ExtractionPromptVersion { get; set; } = "v1";

    /// <summary>
    /// Number of consecutive failures before the circuit breaker opens (AIR-O02). Default: 3.
    /// Maps to <c>CircuitBreakerStrategyOptions.MinimumThroughput</c>.
    /// </summary>
    public int CircuitBreakerFailureThreshold { get; set; } = 3;

    /// <summary>
    /// Sliding window duration (in seconds) over which failures are counted (AIR-O02). Default: 300 (5 min).
    /// Maps to <c>CircuitBreakerStrategyOptions.SamplingDuration</c>.
    /// </summary>
    public int CircuitBreakerWindowSeconds { get; set; } = 300;

    /// <summary>
    /// When <c>true</c>, routes requests through Azure OpenAI using the HIPAA BAA path (production).
    /// When <c>false</c> (development/staging), uses the direct OpenAI endpoint.
    /// OWASP A05: never defaults to Azure in dev to avoid accidental production key usage.
    /// </summary>
    public bool UseAzureOpenAI { get; set; } = false;

    /// <summary>
    /// De-duplication prompt template version (AIR-O03, EP-008-I/us_041, task_003).
    /// Resolved to <c>Prompts/deduplication/deduplication-system.txt</c> and
    /// <c>Prompts/deduplication/deduplication-user.txt</c> at runtime.
    /// Override via env var <c>Ai__DeduplicationPromptVersion</c> without redeployment.
    /// </summary>
    public string DeduplicationPromptVersion { get; set; } = "v1";
}
