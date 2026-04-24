using Microsoft.Extensions.Options;
using Propel.Api.Gateway.Features.Documents.Exceptions;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Documents;

/// <summary>
/// Streams an incoming <see cref="Stream"/> to local disk in 4 MB chunks without buffering the
/// entire file in memory (NFR-019, AC-4, EP-011/us_053/task_002).
/// <para>
/// Uses <see cref="FileStream"/> with <c>useAsync: true</c> so the OS uses async I/O completion
/// ports (Windows) / io_uring (Linux) rather than blocking thread-pool threads.
/// </para>
/// <para>
/// <b>Phase 1 (local disk):</b> Encryption is deferred to the OS-level disk encryption of the
/// deployment environment. <b>Phase 2 (Azure Blob):</b> Server-side encryption applies automatically.
/// For patient-uploaded documents requiring per-file AES-256 encryption, continue to use
/// <see cref="LocalDocumentStorageService"/> via <see cref="IDocumentStorageService"/>.
/// </para>
/// <para>
/// Throws <see cref="StorageUnavailableException"/> on any I/O failure so callers return
/// HTTP 503 without creating partial database records (US_053, edge case).
/// </para>
/// </summary>
public sealed class PdfStreamingStorageService : IPdfStreamingStorageService
{
    /// <summary>4 MB read/write buffer — balances memory pressure against syscall frequency.</summary>
    private const int ChunkSize = 4 * 1024 * 1024; // 4 MB

    private readonly DocumentStorageSettings _settings;
    private readonly ILogger<PdfStreamingStorageService> _logger;

    public PdfStreamingStorageService(
        IOptions<DocumentStorageSettings> settings,
        ILogger<PdfStreamingStorageService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> SaveAsync(
        Stream inputStream,
        string fileName,
        CancellationToken ct = default)
    {
        try
        {
            var storageDir = _settings.StoragePath;
            if (!Directory.Exists(storageDir))
                Directory.CreateDirectory(storageDir);

            // Strip any path components from fileName to prevent path traversal (OWASP A01).
            var sanitizedName = Path.GetFileName(fileName);
            // UUID prefix prevents filename collision; no .enc suffix — not encrypted at file level.
            var storageName = $"{Guid.NewGuid():N}_{sanitizedName}";
            var fullPath = Path.Combine(storageDir, storageName);

            // useAsync: true — ensures OS-level async I/O; avoids blocking thread-pool threads.
            await using var outputStream = new FileStream(
                fullPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: ChunkSize,
                useAsync: true);

            var buffer = new byte[ChunkSize];
            int bytesRead;
            while ((bytesRead = await inputStream.ReadAsync(buffer, ct)) > 0)
                await outputStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);

            _logger.LogInformation(
                "PdfStreaming: Stored document {FileName} → {StoragePath}",
                sanitizedName, storageName);

            return storageName;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PdfStreaming: Storage failure for {FileName}", fileName);
            throw new StorageUnavailableException(
                "Document storage is temporarily unavailable.", ex);
        }
    }
}
