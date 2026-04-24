using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Propel.Modules.AI.Models;
using Serilog;

namespace Propel.Modules.AI.Guardrails;

/// <summary>
/// Chunk-level ACL filter for the RAG retrieval pipeline (AIR-S02, task_001, AC-2).
/// <para>
/// Injected directly into <see cref="Services.ExtractionOrchestrator"/> and other RAG callers
/// rather than registered as a global Semantic Kernel filter, because ACL enforcement happens
/// on retrieved document chunks before prompt construction — not during SK function invocation.
/// </para>
/// <para>
/// For each <see cref="DocumentChunk"/> returned by the vector store, this filter calls
/// <see cref="IAuthorizationService.AuthorizeAsync"/> with the resource-based policy
/// <c>"CanAccessPatient"</c> against the chunk's <see cref="DocumentChunk.PatientId"/>.
/// Chunks that fail authorization are silently excluded from the context window.
/// Exclusion is logged at <c>Debug</c> level (expected behaviour — not an error).
/// </para>
/// </summary>
public sealed class RagAclFilter
{
    private readonly IAuthorizationService _authorizationService;

    public RagAclFilter(IAuthorizationService authorizationService)
    {
        _authorizationService = authorizationService;
    }

    /// <summary>
    /// Filters <paramref name="chunks"/> to only those the <paramref name="user"/> is authorised
    /// to access, as determined by the <c>"CanAccessPatient"</c> resource-based policy.
    /// </summary>
    /// <param name="chunks">Candidate chunks retrieved from the vector store.</param>
    /// <param name="user">The authenticated principal making the RAG request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A filtered enumerable containing only chunks for which authorization succeeded.
    /// Returns an empty collection when all chunks are unauthorised.
    /// </returns>
    public async Task<IReadOnlyList<DocumentChunk>> FilterChunksAsync(
        IEnumerable<DocumentChunk> chunks,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var authorised = new List<DocumentChunk>();

        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();

            var result = await _authorizationService.AuthorizeAsync(
                user,
                chunk.PatientId,
                "CanAccessPatient");

            if (result.Succeeded)
            {
                authorised.Add(chunk);
            }
            else
            {
                // Silent exclusion is the correct behaviour (AC-2) — no error, only debug.
                Log.Debug(
                    "RagAclFilter_ChunkExcluded: documentId={DocumentId} patientId={PatientId} — " +
                    "requesting user is not authorised to access this chunk (AIR-S02).",
                    chunk.DocumentId,
                    chunk.PatientId);
            }
        }

        return authorised.AsReadOnly();
    }
}
