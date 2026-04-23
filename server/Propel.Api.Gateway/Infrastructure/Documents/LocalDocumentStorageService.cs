using Microsoft.Extensions.Options;
using Propel.Api.Gateway.Features.Documents.Exceptions;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Documents;

/// <summary>
/// Local file system implementation of <see cref="IDocumentStorageService"/>.
/// Writes encrypted document bytes to a configurable directory path.
/// Each document is stored using a UUID-prefixed filename to prevent collisions (NFR-004).
/// Throws <see cref="StorageUnavailableException"/> when the storage directory is inaccessible
/// so callers return HTTP 503 without creating partial records (US_038, US_039 edge case).
/// </summary>
public sealed class LocalDocumentStorageService : IDocumentStorageService
{
    private readonly DocumentStorageSettings _settings;
    private readonly ILogger<LocalDocumentStorageService> _logger;

    public LocalDocumentStorageService(
        IOptions<DocumentStorageSettings> settings,
        ILogger<LocalDocumentStorageService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> StoreAsync(
        byte[] encryptedBytes,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var storageDir = _settings.StoragePath;
            if (!Directory.Exists(storageDir))
                Directory.CreateDirectory(storageDir);

            // Use UUID prefix to prevent filename collision and avoid path traversal (OWASP A01).
            var sanitizedName = Path.GetFileName(fileName); // strip any path components
            var storageName = $"{Guid.NewGuid():N}_{sanitizedName}.enc";
            var fullPath = Path.Combine(storageDir, storageName);

            await File.WriteAllBytesAsync(fullPath, encryptedBytes, cancellationToken);

            _logger.LogInformation(
                "DocumentStorage: Stored encrypted document {FileName} → {StoragePath}",
                sanitizedName, storageName);

            return storageName;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DocumentStorage: Storage failure for {FileName}", fileName);
            throw new StorageUnavailableException(
                "Document storage is temporarily unavailable.", ex);
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> RetrieveAsync(
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fullPath = Path.Combine(_settings.StoragePath, storagePath);
            return await File.ReadAllBytesAsync(fullPath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DocumentStorage: Retrieval failure for {StoragePath}", storagePath);
            throw new StorageUnavailableException(
                "Document storage is temporarily unavailable.", ex);
        }
    }
}
