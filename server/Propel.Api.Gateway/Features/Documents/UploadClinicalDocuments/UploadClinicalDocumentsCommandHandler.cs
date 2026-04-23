using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Api.Gateway.Features.Documents.Dtos;
using Propel.Api.Gateway.Features.Documents.Notifications;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Features.Documents.UploadClinicalDocuments;

/// <summary>
/// Handles <see cref="UploadClinicalDocumentsCommand"/> for
/// <c>POST /api/documents/upload</c> (US_038, AC-2, AC-4, FR-041, FR-042, FR-043, FR-057, FR-058).
/// <list type="number">
///   <item><b>Step 1 — Per-file validation</b>: PDF MIME + magic bytes check; file too large → <see cref="UploadFileResultDto"/> with <c>success=false</c> (AC-4 partial batch).</item>
///   <item><b>Step 2 — Read bytes + magic bytes guard</b>: Prevents MIME-spoofed uploads (OWASP A05).</item>
///   <item><b>Step 3 — Encrypt bytes</b>: via <see cref="IFileEncryptionService"/> (AES-256, NFR-004, FR-043).</item>
///   <item><b>Step 4 — Store encrypted bytes</b>: via <see cref="IDocumentStorageService"/>; <see cref="Propel.Api.Gateway.Features.Documents.Exceptions.StorageUnavailableException"/> propagates as HTTP 503 (edge case — no partial records).</item>
///   <item><b>Step 5 — INSERT ClinicalDocument</b>: <c>processingStatus = Pending</c>, <c>sourceType = PatientUpload</c>.</item>
///   <item><b>Step 6 — Publish ClinicalDocumentUploadedNotification</b>: triggers async AI extraction pipeline (AC-3, AD-3).</item>
///   <item><b>Step 7 — Write audit log</b>: <c>PatientDocumentUploaded</c> (FR-057, FR-058, AD-7).</item>
/// </list>
/// Already-persisted files are NOT rolled back when a later file in the batch fails validation (AC-4).
/// <see cref="Propel.Api.Gateway.Features.Documents.Exceptions.StorageUnavailableException"/> aborts
/// the entire batch before any record is created (edge case).
/// </summary>
public sealed class UploadClinicalDocumentsCommandHandler
    : IRequestHandler<UploadClinicalDocumentsCommand, UploadBatchResultDto>
{
    /// <summary>Maximum accepted file size: 25 MB = 26,214,400 bytes (FR-042).</summary>
    private const long MaxFileSizeBytes = 26_214_400L;

    private readonly AppDbContext _db;
    private readonly IFileEncryptionService _encryptionService;
    private readonly IDocumentStorageService _storageService;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IPublisher _publisher;
    private readonly ILogger<UploadClinicalDocumentsCommandHandler> _logger;

    public UploadClinicalDocumentsCommandHandler(
        AppDbContext db,
        IFileEncryptionService encryptionService,
        IDocumentStorageService storageService,
        IAuditLogRepository auditLogRepo,
        IPublisher publisher,
        ILogger<UploadClinicalDocumentsCommandHandler> logger)
    {
        _db = db;
        _encryptionService = encryptionService;
        _storageService = storageService;
        _auditLogRepo = auditLogRepo;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<UploadBatchResultDto> Handle(
        UploadClinicalDocumentsCommand request,
        CancellationToken cancellationToken)
    {
        var results = new List<UploadFileResultDto>(request.Files.Count);

        foreach (var file in request.Files)
        {
            // Step 1 — Per-file MIME validation (FR-042, AC-4 partial batch semantics).
            if (!string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase)
                || !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new UploadFileResultDto(file.FileName, false, "Only PDF files are accepted."));
                continue;
            }

            if (file.Length > MaxFileSizeBytes)
            {
                results.Add(new UploadFileResultDto(file.FileName, false, "File too large — maximum 25 MB."));
                continue;
            }

            // Step 2 — Read bytes and verify PDF magic bytes (%PDF-) to prevent MIME spoofing (OWASP A05).
            byte[] fileBytes;
            await using (var stream = file.OpenReadStream())
            {
                fileBytes = new byte[file.Length];
                await stream.ReadExactlyAsync(fileBytes, cancellationToken);
            }

            if (!IsPdfMagicBytes(fileBytes))
            {
                results.Add(new UploadFileResultDto(file.FileName, false, "Only PDF files are accepted."));
                continue;
            }

            // Step 3 — Encrypt file bytes (AES-256 via Data Protection API, NFR-004, FR-043).
            var encryptedBytes = await _encryptionService.EncryptAsync(fileBytes, cancellationToken);

            // Step 4 — Store encrypted bytes.
            // StorageUnavailableException propagates up — caught by GlobalExceptionFilter → HTTP 503.
            // No SaveChangesAsync() has been called for this file yet, so no partial record is created.
            var storagePath = await _storageService.StoreAsync(
                encryptedBytes, file.FileName, cancellationToken);

            // Step 5 — INSERT ClinicalDocument (processingStatus = Pending, sourceType = PatientUpload).
            var document = new ClinicalDocument
            {
                Id = Guid.NewGuid(),
                PatientId = request.PatientId,
                FileName = file.FileName,
                FileSize = file.Length,
                StoragePath = storagePath,
                MimeType = "application/pdf",
                ProcessingStatus = DocumentProcessingStatus.Pending,
                SourceType = DocumentSourceType.PatientUpload,
                UploadedAt = DateTime.UtcNow
            };

            _db.ClinicalDocuments.Add(document);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "PatientUpload_Success: DocumentId={DocumentId} PatientId={PatientId} FileName={FileName} FileSize={FileSize}",
                document.Id, document.PatientId, document.FileName, document.FileSize);

            // Step 6 — Publish notification to trigger async AI extraction pipeline (AC-3, AD-3).
            await _publisher.Publish(
                new ClinicalDocumentUploadedNotification(document.Id, document.PatientId),
                cancellationToken);

            // Step 7 — Write audit log (FR-057, FR-058, AD-7 — isolated context, never rolled back).
            await _auditLogRepo.AppendAsync(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = request.PatientId,
                PatientId = request.PatientId,
                Role = "Patient",
                Action = "PatientDocumentUploaded",
                EntityType = "ClinicalDocument",
                EntityId = document.Id,
                Details = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    fileName = document.FileName,
                    fileSize = document.FileSize,
                    storagePath = document.StoragePath,
                    processingStatus = document.ProcessingStatus.ToString()
                })),
                Timestamp = DateTime.UtcNow
            }, cancellationToken);

            results.Add(new UploadFileResultDto(file.FileName, true, null, document.Id));
        }

        return new UploadBatchResultDto(results);
    }

    /// <summary>
    /// Validates the first five bytes of a file match the PDF magic bytes sequence <c>%PDF-</c>.
    /// Prevents MIME-type spoofing where a non-PDF file is renamed with a <c>.pdf</c> extension (OWASP A05).
    /// </summary>
    private static bool IsPdfMagicBytes(byte[] bytes) =>
        bytes.Length >= 5 &&
        bytes[0] == 0x25 && // %
        bytes[1] == 0x50 && // P
        bytes[2] == 0x44 && // D
        bytes[3] == 0x46 && // F
        bytes[4] == 0x2D;   // -
}
