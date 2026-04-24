using Microsoft.EntityFrameworkCore;
using Npgsql;
using Propel.Api.Gateway.Data;
using Propel.Domain.Dtos;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IExtractedDataRepository"/> for AI-extracted
/// clinical data fields (US_040, AC-3, AIR-001, AIR-002).
/// <para>
/// Records are inserted in batches of 50 rows to control memory pressure.
/// De-duplication flag updates (us_041/task_003) use a targeted EF Core LINQ query
/// — no raw string interpolation (OWASP A03).
/// </para>
/// </summary>
public sealed class ExtractedDataRepository : IExtractedDataRepository
{
    private const int BatchSize = 50;

    private readonly AppDbContext _context;

    public ExtractedDataRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task InsertBatchAsync(
        IReadOnlyList<ExtractedData> fields,
        CancellationToken ct = default)
    {
        if (fields.Count == 0)
            return;

        var batch = new List<ExtractedData>(BatchSize);

        foreach (var field in fields)
        {
            batch.Add(field);

            if (batch.Count >= BatchSize)
            {
                await _context.ExtractedData.AddRangeAsync(batch, ct);
                await _context.SaveChangesAsync(ct);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await _context.ExtractedData.AddRangeAsync(batch, ct);
            await _context.SaveChangesAsync(ct);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ExtractedData>> GetByDocumentIdAsync(
        Guid documentId,
        CancellationToken ct = default)
        => await _context.ExtractedData
            .AsNoTracking()
            .Where(e => e.DocumentId == documentId)
            .ToListAsync(ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ExtractedData>> GetCompletedByPatientIdAsync(
        Guid patientId,
        CancellationToken ct = default)
        => await _context.ExtractedData
            .AsNoTracking()
            .Where(e => e.PatientId == patientId
                     && e.Document.ProcessingStatus == DocumentProcessingStatus.Completed)
            .ToListAsync(ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SimilarFieldPair>> GetSimilarFieldPairsAsync(
        Guid patientId,
        CancellationToken ct = default)
    {
        // OWASP A03: only NpgsqlParameter instances used — no string interpolation of caller values.
        // pgvector cosine_distance operator (<=>): 1 - distance = cosine similarity.
        // Self-join with a.id < b.id ensures each pair appears exactly once.
        // Threshold 0.7 enforces AIR-R02 minimum similarity requirement.
        // The embedding column exists in the DB even though EF ignores it (commented out config).
        const string sql = """
            SELECT a.id        AS id1,
                   b.id        AS id2,
                   1 - (a.embedding <=> b.embedding) AS similarity
            FROM   extracted_data a
            JOIN   extracted_data b
                   ON  a.data_type  = b.data_type
                   AND a.patient_id = b.patient_id
                   AND a.id         < b.id
            JOIN   clinical_documents da ON da.id = a.document_id
            JOIN   clinical_documents db ON db.id = b.document_id
            WHERE  a.patient_id = @patientId
              AND  da.processing_status = 'Completed'
              AND  db.processing_status = 'Completed'
              AND  a.embedding IS NOT NULL
              AND  b.embedding IS NOT NULL
              AND  1 - (a.embedding <=> b.embedding) >= 0.7
            """;

        var param = new NpgsqlParameter("patientId", patientId);

        await using var command = _context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(param);

        await _context.Database.OpenConnectionAsync(ct);

        var results = new List<SimilarFieldPair>();

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new SimilarFieldPair(
                Id1        : reader.GetGuid(0),
                Id2        : reader.GetGuid(1),
                Similarity : reader.GetDouble(2)));
        }

        return results.AsReadOnly();
    }

    /// <inheritdoc/>
    public async Task UpdateDeduplicationFlagsAsync(
        IReadOnlyList<ExtractedData> records,
        CancellationToken ct = default)
    {
        if (records.Count == 0)
            return;

        // Load tracked entities for the affected IDs so EF Core can detect changes.
        // Single query with WHERE id = ANY(@ids) to minimise round-trips.
        var ids = records.Select(r => r.Id).ToArray();

        var tracked = await _context.ExtractedData
            .Where(e => ids.Contains(e.Id))
            .ToListAsync(ct);

        var lookup = records.ToDictionary(r => r.Id);

        foreach (var entity in tracked)
        {
            if (!lookup.TryGetValue(entity.Id, out var updated))
                continue;

            entity.IsCanonical         = updated.IsCanonical;
            entity.CanonicalGroupId    = updated.CanonicalGroupId;
            entity.DeduplicationStatus = updated.DeduplicationStatus;
        }

        await _context.SaveChangesAsync(ct);
    }
}

