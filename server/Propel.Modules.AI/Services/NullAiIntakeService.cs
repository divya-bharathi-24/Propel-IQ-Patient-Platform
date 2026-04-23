using Propel.Modules.AI.Exceptions;
using Propel.Modules.AI.Interfaces;
using Propel.Modules.AI.Models;

namespace Propel.Modules.AI.Services;

/// <summary>
/// Temporary stub implementation of <see cref="IAiIntakeService"/> used until
/// <c>SemanticKernelAiIntakeService</c> is delivered by task_003 (EP-005/us_028).
/// <para>
/// Always throws <see cref="AiServiceUnavailableException"/> so the
/// <c>ProcessIntakeTurnCommandHandler</c> returns the graceful fallback response
/// <c>{ isFallback: true, preservedFields: [...] }</c> to the frontend (AIR-O02).
/// </para>
/// <para>
/// Replace this registration in <c>Program.cs</c> with
/// <c>SemanticKernelAiIntakeService</c> when task_003 is merged.
/// </para>
/// </summary>
public sealed class NullAiIntakeService : IAiIntakeService
{
    public Task<IntakeTurnResult> ProcessTurnAsync(
        IReadOnlyList<ConversationTurn> history,
        IReadOnlyList<ExtractedField> currentFields,
        CancellationToken cancellationToken = default)
    {
        throw new AiServiceUnavailableException(
            "AI intake service is not yet configured. Awaiting task_003 (SemanticKernelAiIntakeService).");
    }
}
