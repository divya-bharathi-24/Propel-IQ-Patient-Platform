namespace Propel.Domain.Interfaces;

/// <summary>
/// Abstracts the underlying clinical document storage backend.
/// Implementations write pre-encrypted bytes to the configured storage path and return
/// a resolvable path that is persisted to the <c>clinical_documents.storage_path</c> column.
/// Implementations throw <see cref="Propel.Api.Gateway.Features.Documents.Exceptions.StorageUnavailableException"/>
/// when the storage backend is unavailable — callers must handle this as HTTP 503 (US_038, US_039 edge case).
/// </summary>
public interface IDocumentStorageService
{
    /// <summary>
    /// Writes pre-encrypted file bytes to the storage backend.
    /// </summary>
    /// <param name="encryptedBytes">Already-encrypted file content (caller is responsible for encryption).</param>
    /// <param name="fileName">Original file name (used to derive the storage key / path).</param>
    /// <param name="cancellationToken">Propagated from ASP.NET Core request pipeline.</param>
    /// <returns>A storage path string that uniquely identifies the persisted document.</returns>
    Task<string> StoreAsync(byte[] encryptedBytes, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves encrypted bytes from the storage backend by storage path.
    /// </summary>
    /// <param name="storagePath">The path returned by <see cref="StoreAsync"/>.</param>
    /// <param name="cancellationToken">Propagated from ASP.NET Core request pipeline.</param>
    /// <returns>Encrypted bytes as written by <see cref="StoreAsync"/>.</returns>
    Task<byte[]> RetrieveAsync(string storagePath, CancellationToken cancellationToken = default);
}
