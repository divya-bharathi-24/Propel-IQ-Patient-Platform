namespace Propel.Api.Gateway.Features.Documents.Exceptions;

/// <summary>
/// Thrown by <see cref="Propel.Domain.Interfaces.IDocumentStorageService"/> implementations
/// when the underlying storage backend is unavailable.
/// Maps to HTTP 503 Service Unavailable via <c>GlobalExceptionFilter</c> (US_038, US_039 edge case).
/// No <see cref="Propel.Domain.Entities.ClinicalDocument"/> record is persisted when this exception is thrown.
/// </summary>
public sealed class StorageUnavailableException : Exception
{
    public StorageUnavailableException()
        : base("Document storage is temporarily unavailable. Please try again later.")
    {
    }

    public StorageUnavailableException(string message)
        : base(message)
    {
    }

    public StorageUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
