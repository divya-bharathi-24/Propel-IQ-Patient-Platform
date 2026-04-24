namespace Propel.Domain.Interfaces;

/// <summary>
/// Streams an incoming file directly to the storage backend in 4 MB chunks,
/// avoiding full-file buffering in memory (NFR-019, AC-4).
/// Used for large document uploads (≤50 MB) where per-file encryption is
/// deferred to the storage backend (Phase 1: OS-level disk encryption;
/// Phase 2: Azure Blob Storage server-side encryption).
/// </summary>
public interface IPdfStreamingStorageService
{
    /// <summary>
    /// Streams <paramref name="inputStream"/> to the storage backend using 4 MB read/write chunks.
    /// The caller is responsible for opening and disposing the stream.
    /// </summary>
    /// <param name="inputStream">Readable stream of the incoming file (e.g. <c>IFormFile.OpenReadStream()</c>).</param>
    /// <param name="fileName">Original file name — used to derive the storage key (path-component stripped).</param>
    /// <param name="ct">Cancellation token propagated from the HTTP request pipeline.</param>
    /// <returns>A storage path string that uniquely identifies the persisted document.</returns>
    Task<string> SaveAsync(Stream inputStream, string fileName, CancellationToken ct = default);
}
