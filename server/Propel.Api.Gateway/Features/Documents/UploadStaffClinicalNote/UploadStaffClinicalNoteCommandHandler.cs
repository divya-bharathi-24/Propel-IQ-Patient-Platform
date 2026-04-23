using System.Security.Claims;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Propel.Api.Gateway.Data;
using Propel.Api.Gateway.Features.Documents.Dtos;
using Propel.Api.Gateway.Features.Documents.Notifications;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Features.Documents.UploadStaffClinicalNote;

/// <summary>
/// Handles <see cref="UploadStaffClinicalNoteCommand"/> for
/// <c>POST /api/staff/documents/upload</c> (US_039, AC-1, AC-3, AC-4, FR-044, FR-058).
/// <list type="number">
///   <item><b>Step 1 — Resolve staffId from JWT</b> (OWASP A01 — never from form body).</item>
///   <item><b>Step 2 — Verify patient exists</b> (404 if not found).</item>
///   <item><b>Step 3 — Optional encounter reference check</b> (sets encounterWarning; does not reject).</item>
///   <item><b>Step 4 — Encrypt file bytes</b> via <see cref="IFileEncryptionService"/> (NFR-004, FR-043).</item>
///   <item><b>Step 5 — Store encrypted bytes</b> via <see cref="IDocumentStorageService"/> (503 on failure — no partial record).</item>
///   <item><b>Step 6 — INSERT ClinicalDocument</b> with <c>sourceType = StaffUpload</c> and <c>uploadedById = staffId</c>.</item>
///   <item><b>Step 7 — Publish ClinicalDocumentUploadedNotification</b> after commit (AC-3, AD-3 async trigger).</item>
///   <item><b>Step 8 — Write audit log</b> <c>StaffClinicalNoteUploaded</c> (FR-058, AD-7).</item>
/// </list>
/// </summary>
public sealed class UploadStaffClinicalNoteCommandHandler
    : IRequestHandler<UploadStaffClinicalNoteCommand, UploadNoteResponseDto>
{
    private readonly AppDbContext _db;
    private readonly IFileEncryptionService _encryptionService;
    private readonly IDocumentStorageService _storageService;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IPublisher _publisher;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<UploadStaffClinicalNoteCommandHandler> _logger;

    public UploadStaffClinicalNoteCommandHandler(
        AppDbContext db,
        IFileEncryptionService encryptionService,
        IDocumentStorageService storageService,
        IAuditLogRepository auditLogRepo,
        IPublisher publisher,
        IHttpContextAccessor httpContextAccessor,
        ILogger<UploadStaffClinicalNoteCommandHandler> logger)
    {
        _db = db;
        _encryptionService = encryptionService;
        _storageService = storageService;
        _auditLogRepo = auditLogRepo;
        _publisher = publisher;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<UploadNoteResponseDto> Handle(
        UploadStaffClinicalNoteCommand request,
        CancellationToken cancellationToken)
    {
        // Step 1 — Resolve staffId from JWT NameIdentifier claim (OWASP A01).
        var staffIdClaim = _httpContextAccessor.HttpContext!.User
            .FindFirstValue(ClaimTypes.NameIdentifier)!;
        var staffId = Guid.Parse(staffIdClaim);
        var ipAddress = _httpContextAccessor.HttpContext.Connection.RemoteIpAddress?.ToString();

        // Step 2 — Verify the target patient exists (404 if not found).
        var patientExists = await _db.Patients
            .AsNoTracking()
            .AnyAsync(p => p.Id == request.PatientId, cancellationToken);

        if (!patientExists)
            throw new KeyNotFoundException(
                $"Patient '{request.PatientId}' was not found.");

        // Step 3 — Optional encounter reference check (sets warning; does NOT reject — US_039 edge case).
        bool encounterWarning = false;
        string? warningMessage = null;

        if (request.EncounterReference is not null)
        {
            var encounterFound = await _db.Appointments
                .AsNoTracking()
                .AnyAsync(a => a.Id.ToString() == request.EncounterReference
                            && a.PatientId == request.PatientId, cancellationToken);

            if (!encounterFound)
            {
                encounterWarning = true;
                warningMessage =
                    "Encounter reference not found — document linked to patient without appointment reference.";

                _logger.LogWarning(
                    "StaffUpload_EncounterWarning: PatientId={PatientId} EncounterReference={EncounterReference} StaffId={StaffId}",
                    request.PatientId, request.EncounterReference, staffId);
            }
        }

        // Step 4 — Read and encrypt file bytes (NFR-004, FR-043).
        await using var stream = request.File.OpenReadStream();
        var fileBytes = new byte[request.File.Length];
        await stream.ReadExactlyAsync(fileBytes, cancellationToken);
        var encryptedBytes = await _encryptionService.EncryptAsync(fileBytes, cancellationToken);

        // Step 5 — Store encrypted bytes; StorageUnavailableException propagates as HTTP 503.
        // No SaveChangesAsync() has been called yet — no partial record will be created.
        var storagePath = await _storageService.StoreAsync(
            encryptedBytes, request.File.FileName, cancellationToken);

        // Step 6 — INSERT ClinicalDocument (sourceType = StaffUpload, processingStatus = Pending).
        var doc = new ClinicalDocument
        {
            Id = Guid.NewGuid(),
            PatientId = request.PatientId,
            UploadedById = staffId,
            SourceType = DocumentSourceType.StaffUpload,
            EncounterReference = request.EncounterReference,
            FileName = request.File.FileName,
            FileSize = request.File.Length,
            StoragePath = storagePath,
            MimeType = "application/pdf",
            ProcessingStatus = DocumentProcessingStatus.Pending,
            UploadedAt = DateTime.UtcNow
        };

        _db.ClinicalDocuments.Add(doc);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "StaffUpload_Success: DocumentId={DocumentId} PatientId={PatientId} StaffId={StaffId} EncounterWarning={EncounterWarning}",
            doc.Id, doc.PatientId, staffId, encounterWarning);

        // Step 7 — Publish notification to trigger AI extraction pipeline (AC-3, AD-3 async).
        await _publisher.Publish(
            new ClinicalDocumentUploadedNotification(doc.Id, doc.PatientId),
            cancellationToken);

        // Step 8 — Write audit log (FR-058, AD-7 — independent context, never rolled back).
        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = staffId,
            PatientId = request.PatientId,
            Role = "Staff",
            Action = "StaffClinicalNoteUploaded",
            EntityType = "ClinicalDocument",
            EntityId = doc.Id,
            Details = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    fileName = doc.FileName,
                    fileSize = doc.FileSize,
                    encounterReference = doc.EncounterReference,
                    encounterWarning
                })),
            IpAddress = ipAddress,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        return new UploadNoteResponseDto(doc.Id, encounterWarning, warningMessage);
    }
}
