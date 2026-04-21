namespace Propel.Domain.Enums;

/// <summary>
/// Lifecycle state of a clinical document's AI-powered data extraction pipeline.
/// Stored as string in the database for human-readable audit logs.
/// </summary>
public enum DocumentProcessingStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
