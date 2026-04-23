# Task - TASK_002

## Requirement Reference

- **User Story**: US_039 — Staff Post-Visit Clinical Note Upload
- **Story Location**: `.propel/context/tasks/EP-008-I/us_039/us_039.md`
- **Acceptance Criteria**:
  - AC-1: Given I am authenticated as Staff and navigate to a patient's record, When I upload a PDF clinical note, Then the document is validated (PDF, ≤25 MB), encrypted at rest, and a ClinicalDocument record is created with the patient's ID and the encounter reference.
  - AC-2: Given the clinical note is uploaded, When I view the patient's document history, Then the note appears with a "Staff Upload" indicator, the staff member's name, the upload timestamp, and the encounter reference.
  - AC-3: Given a clinical note is successfully uploaded, When the async processing pipeline runs, Then the document is processed by the AI extraction pipeline, and the extracted data is linked to the patient's 360-degree view.
  - AC-4: Given a Patient-role user attempts to access the Staff upload endpoint, When the request is evaluated, Then HTTP 403 Forbidden is returned and no document is stored.
- **Edge Cases**:
  - Wrong patient uploaded: Staff can soft-delete the incorrect document from the patient record within 24 hours with a documented reason; the deletion event is logged in the audit trail.
  - Encounter reference not found: System accepts the document with a 202 response body containing `encounterWarning: true` and `warningMessage`; no 4xx error raised.

## Design References (Frontend Tasks Only)

| Reference Type         | Value |
| ---------------------- | ----- |
| **UI Impact**          | No    |
| **Figma URL**          | N/A   |
| **Wireframe Status**   | N/A   |
| **Wireframe Type**     | N/A   |
| **Wireframe Path/URL** | N/A   |
| **Screen Spec**        | N/A   |
| **UXR Requirements**   | N/A   |
| **Design Tokens**      | N/A   |

## Applicable Technology Stack

| Layer        | Technology            | Version |
| ------------ | --------------------- | ------- |
| Backend      | ASP.NET Core Web API  | .net 10 |
| Mediator     | MediatR               | 12.x    |
| Validation   | FluentValidation      | 11.x    |
| ORM          | Entity Framework Core | 9.x     |
| Database     | PostgreSQL            | 16+     |
| AI/ML        | N/A                   | N/A     |
| Vector Store | N/A                   | N/A     |
| AI Gateway   | N/A                   | N/A     |
| Mobile       | N/A                   | N/A     |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type           | Value |
| ------------------------ | ----- |
| **AI Impact**            | No    |
| **AIR Requirements**     | N/A   |
| **AI Pattern**           | N/A   |
| **Prompt Template Path** | N/A   |
| **Guardrails Config**    | N/A   |
| **Model Provider**       | N/A   |

## Mobile References (Mobile Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **Mobile Impact**    | No    |
| **Platform Target**  | N/A   |
| **Min OS Version**   | N/A   |
| **Mobile Framework** | N/A   |

## Task Overview

Implement the `StaffDocumentController` and supporting CQRS handlers for staff post-visit clinical note management. Three endpoints form the surface:

**`POST /api/staff/documents/upload`** — `[Authorize(Roles="Staff,Admin")]` — `multipart/form-data`. Processes the staff note upload via `UploadStaffClinicalNoteCommand` (MediatR):

1. Resolves `staffId` from JWT `NameIdentifier` claim (OWASP A01 — never from form body).
2. Validates `patientId` exists in the `Patients` table (404 if not found).
3. FluentValidation: `file` is required, MIME type `application/pdf`, size ≤ 26,214,400 bytes (25 MB); `encounterReference` max 100 chars if provided.
4. Optionally checks if `encounterReference` matches an existing `Appointment.Id`; if not found, sets `encounterWarning = true` (does NOT reject).
5. Encrypts file bytes via `IFileEncryptionService.EncryptAsync()` (from US_038 TASK_002).
6. Persists to `IDocumentStorageService.StoreAsync()` (from US_038 TASK_002) → returns `storagePath`.
7. INSERTs `ClinicalDocument { patientId, uploadedById = staffId, sourceType = StaffUpload, encounterReference, fileName, fileSize, storagePath, mimeType = application/pdf, processingStatus = Pending, uploadedAt = UtcNow }`.
8. Publishes `ClinicalDocumentUploadedNotification` (MediatR `INotification`) → triggers AI extraction pipeline asynchronously (AC-3, AD-3).
9. Writes audit log `StaffClinicalNoteUploaded` via `IAuditLogRepository` (AD-7, FR-058).
10. Returns `201 Created` with `UploadNoteResponseDto { id, encounterWarning, warningMessage }`. If document storage unavailable → `503 Service Unavailable` (edge case — storage down); no partial record created.

**`GET /api/staff/patients/{patientId}/documents`** — `[Authorize(Roles="Staff,Admin")]`. Loads document history via `GetPatientDocumentsQuery` (MediatR):

- `AsNoTracking()` query of `ClinicalDocuments` filtered by `patientId`, `deletedAt == null` (excludes soft-deleted).
- Joins `Staff` table to resolve `uploadedByName` for `sourceType = StaffUpload` rows.
- Returns `DocumentHistoryItemDto[]` ordered by `uploadedAt DESC`.
- `isDeletable` = `sourceType == StaffUpload && uploadedAt >= UtcNow.AddHours(-24) && deletedAt == null`.

**`DELETE /api/staff/documents/{id}`** — `[Authorize(Roles="Staff,Admin")]`. Soft-deletes via `SoftDeleteClinicalDocumentCommand` (MediatR):

- Resolves `staffId` from JWT.
- Loads `ClinicalDocument` where `id = request.Id` (not AsNoTracking — requires update).
- Guard: `sourceType` must be `StaffUpload` (staff cannot soft-delete patient self-uploads).
- Guard: `uploadedAt >= UtcNow.AddHours(-24)` (24-hour window — edge case).
- Guard: `deletedAt == null` (idempotent delete).
- Sets `deletedAt = UtcNow`, `deletionReason = reason` (validated: 10–500 chars).
- Writes audit log `StaffClinicalNoteDeleted` with `before = { fileName, patientId }` (FR-058 before-state capture).
- Returns `204 No Content`.

**`ClinicalDocumentUploadedNotification` handler** (published inline, handled by AI module separately): `INotificationHandler<ClinicalDocumentUploadedNotification>` is declared in the AI pipeline task. This task only publishes — ensuring decoupled async trigger (AD-3).

## Dependent Tasks

- **US_039 / TASK_003** — DB migration must apply `source_type`, `uploaded_by_id`, `encounter_reference`, `deleted_at`, `deletion_reason` columns to `clinical_documents`.
- **US_038 / TASK_002** — `IDocumentStorageService` and `IFileEncryptionService` interfaces and implementations must exist.
- **US_007 (EP-DATA)** — `ClinicalDocument` entity and `clinical_documents` table must exist with base columns.
- **US_013 / TASK_001** — `IAuditLogRepository` write-only pattern must be in place.

## Impacted Components

| Component                                       | Status | Location                                                                          |
| ----------------------------------------------- | ------ | --------------------------------------------------------------------------------- |
| `StaffDocumentController`                       | NEW    | `Server/Controllers/StaffDocumentController.cs`                                   |
| `UploadStaffClinicalNoteCommand` + `Handler`    | NEW    | `Server/Features/Documents/UploadStaffClinicalNote/`                              |
| `GetPatientDocumentsQuery` + `Handler`          | NEW    | `Server/Features/Documents/GetPatientDocuments/`                                  |
| `SoftDeleteClinicalDocumentCommand` + `Handler` | NEW    | `Server/Features/Documents/SoftDeleteClinicalDocument/`                           |
| `ClinicalDocumentUploadedNotification`          | NEW    | `Server/Features/Documents/Notifications/ClinicalDocumentUploadedNotification.cs` |
| `DocumentHistoryItemDto`                        | NEW    | `Server/Features/Documents/Dtos/DocumentHistoryItemDto.cs`                        |
| `UploadNoteResponseDto`                         | NEW    | `Server/Features/Documents/Dtos/UploadNoteResponseDto.cs`                         |

## Implementation Plan

1. **`UploadNoteResponseDto`** and **`DocumentHistoryItemDto`**:

   ```csharp
   public record UploadNoteResponseDto(
       Guid Id,
       bool EncounterWarning,
       string? WarningMessage
   );

   public record DocumentHistoryItemDto(
       Guid Id,
       string FileName,
       long FileSize,
       string SourceType,
       string? UploadedByName,
       string? EncounterReference,
       string ProcessingStatus,
       DateTimeOffset UploadedAt,
       bool IsDeletable
   );
   ```

2. **`UploadStaffClinicalNoteCommand` + FluentValidation**:

   ```csharp
   public record UploadStaffClinicalNoteCommand(
       Guid PatientId,
       IFormFile File,
       string? EncounterReference
   ) : IRequest<UploadNoteResponseDto>;

   public class UploadStaffClinicalNoteCommandValidator : AbstractValidator<UploadStaffClinicalNoteCommand>
   {
       public UploadStaffClinicalNoteCommandValidator()
       {
           RuleFor(x => x.File).NotNull()
               .Must(f => f.ContentType == "application/pdf").WithMessage("Only PDF files are accepted.")
               .Must(f => f.Length <= 26_214_400).WithMessage("File too large — maximum 25 MB.");
           RuleFor(x => x.EncounterReference)
               .MaximumLength(100).When(x => x.EncounterReference != null);
       }
   }
   ```

3. **`UploadStaffClinicalNoteCommandHandler.Handle()`**:

   ```csharp
   var staffId = GetStaffIdFromJwt();   // OWASP A01 — from NameIdentifier claim

   // Verify patient exists
   var patient = await _db.Patients.FindAsync(request.PatientId, cancellationToken)
       ?? throw new NotFoundException(nameof(Patient), request.PatientId);

   // Optional encounter reference validation
   bool encounterWarning = false;
   string? warningMessage = null;
   if (request.EncounterReference is not null)
   {
       bool found = await _db.Appointments
           .AnyAsync(a => a.Id.ToString() == request.EncounterReference
                       || a.PatientId == request.PatientId, cancellationToken);
       if (!found)
       {
           encounterWarning = true;
           warningMessage = "Encounter reference not found — document linked to patient without appointment reference";
       }
   }

   // Encrypt and store
   await using var stream = request.File.OpenReadStream();
   var fileBytes = new byte[request.File.Length];
   await stream.ReadExactlyAsync(fileBytes, cancellationToken);
   var encrypted = await _encryptionService.EncryptAsync(fileBytes, cancellationToken);
   var storagePath = await _storageService.StoreAsync(
       encrypted, request.File.FileName, cancellationToken);

   // Persist ClinicalDocument record
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
       ProcessingStatus = ProcessingStatus.Pending,
       UploadedAt = DateTimeOffset.UtcNow
   };
   _db.ClinicalDocuments.Add(doc);
   await _db.SaveChangesAsync(cancellationToken);

   // Publish notification → AI extraction pipeline (AD-3 async, AC-3)
   await _publisher.Publish(new ClinicalDocumentUploadedNotification(doc.Id, doc.PatientId), cancellationToken);

   // Audit log (FR-058, AD-7)
   await _auditLog.WriteAsync(new AuditLogEntry
   {
       UserId = staffId,
       Action = "StaffClinicalNoteUploaded",
       EntityType = "ClinicalDocument",
       EntityId = doc.Id,
       IpAddress = _httpContext.Connection.RemoteIpAddress?.ToString()
   });

   return new UploadNoteResponseDto(doc.Id, encounterWarning, warningMessage);
   ```

4. **`GetPatientDocumentsQueryHandler.Handle()`** (AD-2 CQRS read model):

   ```csharp
   var staffId = GetStaffIdFromJwt();   // OWASP A01 — staff must be authenticated
   var utcNow = DateTimeOffset.UtcNow;

   return await _db.ClinicalDocuments
       .AsNoTracking()
       .Where(d => d.PatientId == request.PatientId && d.DeletedAt == null)
       .OrderByDescending(d => d.UploadedAt)
       .Select(d => new DocumentHistoryItemDto(
           d.Id,
           d.FileName,
           d.FileSize,
           d.SourceType.ToString(),
           d.UploadedBy != null ? d.UploadedBy.FullName : null,
           d.EncounterReference,
           d.ProcessingStatus.ToString(),
           d.UploadedAt,
           d.SourceType == DocumentSourceType.StaffUpload
               && d.UploadedAt >= utcNow.AddHours(-24)
               && d.DeletedAt == null
       ))
       .ToListAsync(cancellationToken);
   ```

5. **`SoftDeleteClinicalDocumentCommandHandler.Handle()`**:

   ```csharp
   var staffId = GetStaffIdFromJwt();   // OWASP A01
   var utcNow = DateTimeOffset.UtcNow;

   var doc = await _db.ClinicalDocuments
       .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.DeletedAt == null, cancellationToken)
       ?? throw new NotFoundException(nameof(ClinicalDocument), request.DocumentId);

   if (doc.SourceType != DocumentSourceType.StaffUpload)
       throw new ForbiddenException("Only staff-uploaded documents can be soft-deleted via this endpoint.");

   if (doc.UploadedAt < utcNow.AddHours(-24))
       throw new ValidationException("Documents can only be deleted within 24 hours of upload.");

   doc.DeletedAt = utcNow;
   doc.DeletionReason = request.Reason;  // validated 10–500 chars in FluentValidation
   await _db.SaveChangesAsync(cancellationToken);

   await _auditLog.WriteAsync(new AuditLogEntry
   {
       UserId = staffId,
       Action = "StaffClinicalNoteDeleted",
       EntityType = "ClinicalDocument",
       EntityId = doc.Id,
       Before = JsonSerializer.Serialize(new { doc.FileName, doc.PatientId, doc.EncounterReference }),
       IpAddress = _httpContext.Connection.RemoteIpAddress?.ToString()
   });
   ```

6. **`StaffDocumentController`**:

   ```csharp
   [ApiController]
   [Route("api/staff")]
   [Authorize(Roles = "Staff,Admin")]
   public class StaffDocumentController : ControllerBase
   {
       [HttpPost("documents/upload")]
       [RequestSizeLimit(27_262_976)]  // 26 MB absolute max (25 MB file + form overhead)
       [Consumes("multipart/form-data")]
       public async Task<IActionResult> Upload([FromForm] UploadStaffClinicalNoteCommand cmd, ISender mediator)
           => CreatedAtAction(nameof(GetDocuments), new { patientId = cmd.PatientId },
               await mediator.Send(cmd));

       [HttpGet("patients/{patientId}/documents")]
       public async Task<IActionResult> GetDocuments(Guid patientId, ISender mediator)
           => Ok(await mediator.Send(new GetPatientDocumentsQuery(patientId)));

       [HttpDelete("documents/{id}")]
       public async Task<IActionResult> SoftDelete(Guid id, [FromBody] SoftDeleteRequest req, ISender mediator)
       {
           await mediator.Send(new SoftDeleteClinicalDocumentCommand(id, req.Reason));
           return NoContent();
       }
   }
   ```

   > **Security note**: `[Authorize(Roles = "Staff,Admin")]` is at controller level — all three endpoints require Staff or Admin role. A Patient-role JWT will receive HTTP 403 Forbidden at the ASP.NET Core authorization middleware before any handler executes (AC-4, OWASP A01).

## Current Project State

```
Server/
├── Controllers/
│   └── StaffDocumentController.cs                        ← NEW
├── Features/
│   └── Documents/
│       ├── UploadStaffClinicalNote/
│       │   ├── UploadStaffClinicalNoteCommand.cs
│       │   ├── UploadStaffClinicalNoteCommandValidator.cs
│       │   └── UploadStaffClinicalNoteCommandHandler.cs
│       ├── GetPatientDocuments/
│       │   ├── GetPatientDocumentsQuery.cs
│       │   └── GetPatientDocumentsQueryHandler.cs
│       ├── SoftDeleteClinicalDocument/
│       │   ├── SoftDeleteClinicalDocumentCommand.cs
│       │   ├── SoftDeleteClinicalDocumentCommandValidator.cs
│       │   └── SoftDeleteClinicalDocumentCommandHandler.cs
│       ├── Notifications/
│       │   └── ClinicalDocumentUploadedNotification.cs   ← NEW (published here; handled by AI module)
│       └── Dtos/
│           ├── DocumentHistoryItemDto.cs
│           └── UploadNoteResponseDto.cs
```

## Expected Changes

| Action | File Path                                                                                            | Description                                                                                                              |
| ------ | ---------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| CREATE | `Server/Features/Documents/Dtos/UploadNoteResponseDto.cs`                                            | `record UploadNoteResponseDto(Guid Id, bool EncounterWarning, string? WarningMessage)`                                   |
| CREATE | `Server/Features/Documents/Dtos/DocumentHistoryItemDto.cs`                                           | `record DocumentHistoryItemDto(...)` with `IsDeletable` computed flag                                                    |
| CREATE | `Server/Features/Documents/UploadStaffClinicalNote/UploadStaffClinicalNoteCommand.cs`                | MediatR command with `IFormFile`                                                                                         |
| CREATE | `Server/Features/Documents/UploadStaffClinicalNote/UploadStaffClinicalNoteCommandValidator.cs`       | FluentValidation: PDF MIME, 25 MB size limit, encounter ref max length                                                   |
| CREATE | `Server/Features/Documents/UploadStaffClinicalNote/UploadStaffClinicalNoteCommandHandler.cs`         | Encrypt → store → INSERT `ClinicalDocument` → publish notification → audit log; encounter warning on unmatched ref       |
| CREATE | `Server/Features/Documents/GetPatientDocuments/GetPatientDocumentsQuery.cs`                          | `record GetPatientDocumentsQuery(Guid PatientId) : IRequest<List<DocumentHistoryItemDto>>`                               |
| CREATE | `Server/Features/Documents/GetPatientDocuments/GetPatientDocumentsQueryHandler.cs`                   | `AsNoTracking()` projection with `IsDeletable` 24h window logic (AD-2 CQRS)                                              |
| CREATE | `Server/Features/Documents/SoftDeleteClinicalDocument/SoftDeleteClinicalDocumentCommand.cs`          | `record SoftDeleteClinicalDocumentCommand(Guid DocumentId, string Reason)`                                               |
| CREATE | `Server/Features/Documents/SoftDeleteClinicalDocument/SoftDeleteClinicalDocumentCommandValidator.cs` | Reason: `MinimumLength(10).MaximumLength(500)`                                                                           |
| CREATE | `Server/Features/Documents/SoftDeleteClinicalDocument/SoftDeleteClinicalDocumentCommandHandler.cs`   | `sourceType = StaffUpload` guard; 24h upload-time guard; set `DeletedAt` + `DeletionReason`; audit log with before-state |
| CREATE | `Server/Features/Documents/Notifications/ClinicalDocumentUploadedNotification.cs`                    | `record ClinicalDocumentUploadedNotification(Guid DocumentId, Guid PatientId) : INotification`                           |
| CREATE | `Server/Controllers/StaffDocumentController.cs`                                                      | 3 endpoints: `POST /upload`, `GET /{patientId}/documents`, `DELETE /{id}`; `[RequestSizeLimit(27_262_976)]` on upload    |

## External References

- [ASP.NET Core — File uploads with `IFormFile`](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads)
- [ASP.NET Core — `[RequestSizeLimit]` attribute](https://learn.microsoft.com/en-us/aspnet/core/mvc/filters#result-filters)
- [MediatR 12.x — `INotification` publish pattern](https://github.com/jbogard/MediatR/wiki)
- [FluentValidation 11.x — Validators for `IFormFile`](https://docs.fluentvalidation.net/en/latest/built-in-validators.html)
- [OWASP A01:2021 — staffId from JWT `NameIdentifier` only](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)
- [HIPAA Security Rule — AES-256 encryption at rest (FR-043, NFR-004)](spec.md#FR-043)
- [FR-044 — Staff uploads post-visit clinical notes (spec.md#FR-044)](spec.md#FR-044)
- [FR-058 — Clinical data modification event logging (spec.md#FR-058)](spec.md#FR-058)
- [AD-3 — Event-Driven Async Processing (design.md#AD-3)](design.md#AD-3)
- [AD-7 — Immutable Append-Only Audit Log (design.md#AD-7)](design.md#AD-7)

## Build Commands

- Refer to: `.propel/build/backend-build.md`

## Implementation Validation Strategy

- [ ] Unit tests pass: `UploadStaffClinicalNoteCommandValidator` rejects non-PDF MIME type with "Only PDF files are accepted"
- [ ] Unit tests pass: Validator rejects files > 26,214,400 bytes with "File too large — maximum 25 MB"
- [ ] Unit tests pass: `UploadStaffClinicalNoteCommandHandler` returns `EncounterWarning = true` when encounter ref not found in `Appointments` table
- [ ] Unit tests pass: `SoftDeleteClinicalDocumentCommandHandler` throws `ValidationException` for documents uploaded > 24 hours ago
- [ ] Unit tests pass: `SoftDeleteClinicalDocumentCommandHandler` throws `ForbiddenException` for `sourceType = PatientUpload` documents
- [ ] `GET /api/staff/patients/{patientId}/documents` excludes soft-deleted documents (`deletedAt IS NOT NULL`)
- [ ] `staffId` is always sourced from JWT `NameIdentifier` claim — never from request body or URL (OWASP A01)
- [ ] Patient-role JWT receives HTTP 403 on all three endpoints (AC-4 — `[Authorize(Roles="Staff,Admin")]`)
- [ ] `ClinicalDocumentUploadedNotification` is published after `SaveChangesAsync()` succeeds (AC-3 trigger)

## Implementation Checklist

- [x] `[Authorize(Roles = "Staff,Admin")]` at controller class level; `staffId` from JWT `NameIdentifier` only (OWASP A01); `[RequestSizeLimit(27_262_976)]` on upload endpoint; Patient-role receives 403 before handler executes (AC-4)
- [x] `UploadStaffClinicalNoteCommandValidator`: reject non-`application/pdf` MIME type; reject `IFormFile.Length > 26_214_400`; `EncounterReference` max 100 chars if provided; 422 `UnprocessableEntity` on validation failure (TR-020)
- [x] Encounter reference check: `AnyAsync` lookup against `Appointments` by reference string; if not found set `encounterWarning = true` in response body (202); NEVER reject (edge case — encounter ref not found)
- [x] `IFileEncryptionService.EncryptAsync()` + `IDocumentStorageService.StoreAsync()` called before INSERT; if storage throws → propagate 503, no `SaveChangesAsync` called (no partial records — edge case storage unavailable)
- [x] INSERT `ClinicalDocument` with `sourceType = StaffUpload`, `uploadedById = staffId`, `encounterReference`; publish `ClinicalDocumentUploadedNotification` after `SaveChangesAsync()` (AC-3, AD-3 async trigger); audit log `StaffClinicalNoteUploaded` (FR-058, AD-7)
- [x] `GetPatientDocumentsQueryHandler`: `AsNoTracking()`, filter `deletedAt == null`, `OrderByDescending(uploadedAt)`, project `IsDeletable = sourceType == StaffUpload && uploadedAt >= UtcNow.AddHours(-24)` (AC-2, AD-2)
- [x] `SoftDeleteClinicalDocumentCommandHandler`: guard `sourceType == StaffUpload`; guard `uploadedAt >= UtcNow.AddHours(-24)` (24h window); set `deletedAt = UtcNow`, `deletionReason = reason`; audit log `StaffClinicalNoteDeleted` with before-state `{ fileName, patientId, encounterReference }` (FR-058 before-state, edge case wrong patient)
