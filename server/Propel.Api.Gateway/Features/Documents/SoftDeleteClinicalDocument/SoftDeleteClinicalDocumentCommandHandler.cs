using System.Security.Claims;
using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Propel.Api.Gateway.Data;
using Propel.Api.Gateway.Features.Documents.Exceptions;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Features.Documents.SoftDeleteClinicalDocument;

/// <summary>
/// Handles <see cref="SoftDeleteClinicalDocumentCommand"/> for
/// <c>DELETE /api/staff/documents/{id}</c> (US_039 edge case — wrong patient uploaded, FR-058).
/// <list type="number">
///   <item><b>Step 1 — Resolve staffId from JWT</b> (OWASP A01 — never from request body).</item>
///   <item><b>Step 2 — Load document</b> (404 if not found or already soft-deleted).</item>
///   <item><b>Step 3 — Guard: sourceType must be StaffUpload</b> (403 — staff cannot delete patient self-uploads).</item>
///   <item><b>Step 4 — Guard: 24-hour window</b> (400 if document was uploaded more than 24 hours ago).</item>
///   <item><b>Step 5 — Soft-delete</b>: set <c>deletedAt = UtcNow</c>, <c>deletionReason = reason</c>.</item>
///   <item><b>Step 6 — Audit log</b> with before-state <c>{ fileName, patientId, encounterReference }</c> (FR-058, AD-7).</item>
/// </list>
/// </summary>
public sealed class SoftDeleteClinicalDocumentCommandHandler
    : IRequestHandler<SoftDeleteClinicalDocumentCommand, Unit>
{
    private readonly AppDbContext _db;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SoftDeleteClinicalDocumentCommandHandler> _logger;

    public SoftDeleteClinicalDocumentCommandHandler(
        AppDbContext db,
        IAuditLogRepository auditLogRepo,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SoftDeleteClinicalDocumentCommandHandler> logger)
    {
        _db = db;
        _auditLogRepo = auditLogRepo;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<Unit> Handle(
        SoftDeleteClinicalDocumentCommand request,
        CancellationToken cancellationToken)
    {
        // Step 1 — Resolve staffId from JWT NameIdentifier claim (OWASP A01).
        var staffIdClaim = _httpContextAccessor.HttpContext!.User
            .FindFirstValue(ClaimTypes.NameIdentifier)!;
        var staffId = Guid.Parse(staffIdClaim);
        var ipAddress = _httpContextAccessor.HttpContext.Connection.RemoteIpAddress?.ToString();
        var utcNow = DateTime.UtcNow;

        // Step 2 — Load document (NOT AsNoTracking — requires UPDATE via SaveChangesAsync).
        var doc = await _db.ClinicalDocuments
            .FirstOrDefaultAsync(
                d => d.Id == request.DocumentId && d.DeletedAt == null,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Clinical document '{request.DocumentId}' was not found or has already been deleted.");

        // Step 3 — Guard: only staff-uploaded documents can be soft-deleted via this endpoint.
        if (doc.SourceType != DocumentSourceType.StaffUpload)
        {
            _logger.LogWarning(
                "SoftDelete_Forbidden: DocumentId={DocumentId} SourceType={SourceType} StaffId={StaffId}",
                doc.Id, doc.SourceType, staffId);
            throw new DocumentForbiddenException(
                "Only staff-uploaded documents can be soft-deleted via this endpoint.");
        }

        // Step 4 — Guard: 24-hour upload window (US_039 edge case).
        if (doc.UploadedAt < utcNow.AddHours(-24))
        {
            _logger.LogWarning(
                "SoftDelete_WindowExpired: DocumentId={DocumentId} UploadedAt={UploadedAt} StaffId={StaffId}",
                doc.Id, doc.UploadedAt, staffId);
            throw new ValidationException(
                "Documents can only be deleted within 24 hours of upload.");
        }

        // Capture before-state for audit log (FR-058 before-state capture).
        var beforeState = JsonSerializer.Serialize(new
        {
            fileName = doc.FileName,
            patientId = doc.PatientId,
            encounterReference = doc.EncounterReference
        });

        // Step 5 — Soft-delete.
        doc.DeletedAt = utcNow;
        doc.DeletionReason = request.Reason;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "SoftDelete_Success: DocumentId={DocumentId} PatientId={PatientId} StaffId={StaffId}",
            doc.Id, doc.PatientId, staffId);

        // Step 6 — Audit log with before-state (FR-058, AD-7 — independent context, never rolled back).
        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = staffId,
            PatientId = doc.PatientId,
            Role = "Staff",
            Action = "StaffClinicalNoteDeleted",
            EntityType = "ClinicalDocument",
            EntityId = doc.Id,
            Details = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    before = JsonSerializer.Deserialize<object>(beforeState),
                    reason = request.Reason
                })),
            IpAddress = ipAddress,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        return Unit.Value;
    }
}
