namespace Propel.Domain.Dtos;

/// <summary>
/// Result of a pgvector cosine self-join on <c>extracted_data</c> representing two fields
/// that are semantically similar (cosine similarity ≥ 0.7, AIR-R02).
/// Used by the de-duplication pipeline (EP-008-I/us_041, task_003).
/// </summary>
public sealed record SimilarFieldPair(
    /// <summary>Primary key of the first field (lower GUID ensures each pair appears once).</summary>
    Guid Id1,
    /// <summary>Primary key of the second field.</summary>
    Guid Id2,
    /// <summary>
    /// Cosine similarity score: <c>1 − cosine_distance</c>. Range [0.7, 1.0] after threshold filter.
    /// Score of 1.0 means the embeddings are identical (AIR-R02).
    /// </summary>
    double Similarity);
