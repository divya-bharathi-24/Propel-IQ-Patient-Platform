using System.Text.Json;
using Microsoft.SemanticKernel;
using Propel.Modules.AI.Exceptions;
using Propel.Modules.AI.Interfaces;
using Serilog;

namespace Propel.Modules.AI.Guardrails;

/// <summary>
/// Semantic Kernel <see cref="IFunctionInvocationFilter"/> that validates structured JSON
/// output from AI kernel functions against the expected schema (us_048, AC-2, AIR-Q03).
/// <para>
/// Registered with the <c>Kernel</c> as a function invocation filter. On each function call:
/// <list type="number">
///   <item><description>Extracts the raw string result from <see cref="FunctionResult"/>.</description></item>
///   <item><description>Attempts <c>JsonDocument.Parse</c> to verify the output is well-formed JSON.</description></item>
///   <item><description>Validates required fields against the function-registered schema key
///     (<c>MetadataKey_RequiredJsonFields</c>).</description></item>
///   <item><description>On success: calls <see cref="IAiMetricsWriter.WriteSchemaValidityEventAsync"/> with <c>isValid = true</c>.</description></item>
///   <item><description>On failure: logs a Serilog error, calls writer with <c>isValid = false</c>,
///     and throws <see cref="AiSchemaValidationException"/> to trigger the caller's manual-review fallback.</description></item>
/// </list>
/// </para>
/// <para>
/// Callers that use <c>IChatCompletionService</c> directly (rather than Kernel.InvokeAsync) can
/// call the static <see cref="ValidateJsonOutputAsync"/> helper to apply the same validation logic
/// without the SK filter infrastructure.
/// </para>
/// </summary>
public sealed class AiOutputSchemaValidator : IFunctionInvocationFilter
{
    /// <summary>
    /// Metadata key used to attach a comma-separated list of required top-level JSON field names
    /// to a <c>KernelFunction</c> definition. The filter reads this key during post-processing.
    /// Example: <c>"vitals,medications,diagnoses"</c>.
    /// </summary>
    public const string MetadataKey_RequiredJsonFields = "ai:schema:required_fields";

    private readonly IAiMetricsWriter _metricsWriter;

    public AiOutputSchemaValidator(IAiMetricsWriter metricsWriter)
    {
        _metricsWriter = metricsWriter;
    }

    /// <inheritdoc/>
    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, Task> next)
    {
        // Allow the function to execute first.
        await next(context);

        // Only validate when the function produced a string result.
        var rawContent = context.Result?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(rawContent))
            return;

        var functionName = context.Function.Name;

        // Retrieve optional required-fields schema from function metadata.
        string? requiredFieldsCsv = null;
        if (context.Function.Metadata.AdditionalProperties.TryGetValue(
                MetadataKey_RequiredJsonFields, out var schemaObj))
        {
            requiredFieldsCsv = schemaObj as string;
        }

        bool isValid = ValidateJsonStructure(rawContent, functionName, requiredFieldsCsv, out var validationError);

        await _metricsWriter.WriteSchemaValidityEventAsync(functionName, isValid).ConfigureAwait(false);

        if (!isValid)
        {
            Log.Error(
                "AiOutputSchemaValidator_Failed: function={FunctionName} reason={Reason}",
                functionName, validationError);

            throw new AiSchemaValidationException(
                $"AI schema validation failed for function '{functionName}': {validationError}");
        }
    }

    /// <summary>
    /// Validates a raw AI-generated JSON string without the SK filter context. Used by
    /// orchestrators that call <c>IChatCompletionService</c> directly rather than through
    /// a Kernel invocation (AIR-Q03, us_048).
    /// </summary>
    /// <param name="rawJson">Raw AI output string to validate.</param>
    /// <param name="pipelineName">Name used in log entries and metric events (no PII).</param>
    /// <param name="requiredTopLevelFields">
    /// Optional comma-separated list of required top-level JSON keys (e.g. <c>"vitals,medications"</c>).
    /// When <c>null</c>, only well-formed JSON is required.
    /// </param>
    /// <param name="metricsWriter">Writer to persist the validity event.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="AiSchemaValidationException">
    /// Thrown when the JSON is malformed or a required field is absent — caller must trigger
    /// the manual-review fallback.
    /// </exception>
    public static async Task ValidateJsonOutputAsync(
        string rawJson,
        string pipelineName,
        string? requiredTopLevelFields,
        IAiMetricsWriter metricsWriter,
        CancellationToken ct = default)
    {
        bool isValid = ValidateJsonStructure(rawJson, pipelineName, requiredTopLevelFields, out var validationError);

        await metricsWriter.WriteSchemaValidityEventAsync(pipelineName, isValid, ct).ConfigureAwait(false);

        if (!isValid)
        {
            Log.Error(
                "AiOutputSchemaValidator_Failed: pipeline={PipelineName} reason={Reason}",
                pipelineName, validationError);

            throw new AiSchemaValidationException(
                $"AI schema validation failed for pipeline '{pipelineName}': {validationError}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Validates well-formed JSON and checks for required top-level property names.
    /// Returns <c>true</c> on success; populates <paramref name="validationError"/> on failure.
    /// </summary>
    private static bool ValidateJsonStructure(
        string rawJson,
        string contextName,
        string? requiredFieldsCsv,
        out string validationError)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawJson);
        }
        catch (JsonException ex)
        {
            validationError = $"Output is not valid JSON — {ex.Message}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(requiredFieldsCsv))
        {
            validationError = string.Empty;
            return true;
        }

        var required = requiredFieldsCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            validationError = "Root JSON element is not an object; cannot validate required fields.";
            return false;
        }

        foreach (var field in required)
        {
            if (!document.RootElement.TryGetProperty(field, out _))
            {
                validationError = $"Required field '{field}' is missing from the AI output.";
                return false;
            }
        }

        validationError = string.Empty;
        return true;
    }
}
