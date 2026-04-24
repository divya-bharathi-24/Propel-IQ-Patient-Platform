using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;
using Propel.Api.Gateway.Data;
using Propel.Domain.Dtos;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IDocumentChunkEmbeddingRepository"/> using
/// pgvector cosine distance operator (<c>&lt;=&gt;</c>) for ACL-filtered similarity search.
/// <para>
/// Security invariants enforced on every query:
/// <list type="bullet">
///   <item><description>AIR-S02: <c>WHERE document_id = ANY(@authorizedDocIds)</c> predicate is always present — no unrestricted vector search.</description></item>
///   <item><description>OWASP A03: all SQL parameters are <see cref="NpgsqlParameter"/> instances — no string interpolation of caller-supplied values.</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class DocumentChunkEmbeddingRepository : IDocumentChunkEmbeddingRepository
{
    private const int BatchSize = 100;

    private readonly AppDbContext _context;

    public DocumentChunkEmbeddingRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task InsertBatchAsync(
        IEnumerable<DocumentChunkEmbedding> chunks,
        CancellationToken ct = default)
    {
        var batch = new List<DocumentChunkEmbedding>(BatchSize);

        foreach (var chunk in chunks)
        {
            batch.Add(chunk);

            if (batch.Count >= BatchSize)
            {
                await _context.DocumentChunkEmbeddings.AddRangeAsync(batch, ct);
                await _context.SaveChangesAsync(ct);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await _context.DocumentChunkEmbeddings.AddRangeAsync(batch, ct);
            await _context.SaveChangesAsync(ct);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DocumentChunkEmbedding>> GetByDocumentIdAsync(
        Guid documentId,
        CancellationToken ct = default)
        => await _context.DocumentChunkEmbeddings
            .AsNoTracking()
            .Where(c => c.DocumentId == documentId)
            .ToListAsync(ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VectorQueryResult>> CosineSimilaritySearchAsync(
        float[] queryVector,
        string[] authorizedDocumentIds,
        int topK,
        CancellationToken ct = default)
    {
        // Parse string GUIDs; silently discard any malformed entries to avoid injection surface.
        var docGuids = authorizedDocumentIds
            .Select(id => Guid.TryParse(id, out var g) ? g : (Guid?)null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToArray();

        if (docGuids.Length == 0)
            return Array.Empty<VectorQueryResult>();

        // OWASP A03: parameterised raw SQL — no caller-supplied value is interpolated into the query string.
        // pgvector <=> operator returns cosine distance in [0, 2]; lower = more similar.
        // The HNSW index (ix_document_chunk_embeddings_embedding_hnsw) is used by the planner
        // when ordering by <=> with LIMIT — verify via EXPLAIN ANALYZE in staging (task_002 checklist).
        const string sql = """
            SELECT
                ce.id                                     AS "ChunkId",
                ce.chunk_text                             AS "ChunkText",
                ce.page_number                            AS "PageNumber",
                ce.document_id                            AS "DocumentId",
                cd.file_name                              AS "DocumentName",
                (ce.embedding <=> @queryVector)::real     AS "CosineDistance",
                cd.uploaded_at                            AS "UploadedAt"
            FROM   document_chunk_embeddings ce
            JOIN   clinical_documents        cd ON cd.id = ce.document_id
            WHERE  ce.document_id = ANY(@authorizedDocIds)
            ORDER  BY ce.embedding <=> @queryVector
            LIMIT  @topK
            """;

        // NpgsqlParameter with Vector type — UseVector() registered the Npgsql type handler on startup.
        var vectorParam  = new NpgsqlParameter("queryVector",     new Vector(queryVector));
        var docIdsParam  = new NpgsqlParameter("authorizedDocIds", docGuids);
        var topKParam    = new NpgsqlParameter("topK",             topK);

        var results = await _context.Database
            .SqlQueryRaw<VectorQueryResult>(sql, vectorParam, docIdsParam, topKParam)
            .ToListAsync(ct);

        return results;
    }
}
