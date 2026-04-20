# Task - task_002_be_document_upload_api

## Requirement Reference

- **User Story:** us_038 — Patient Clinical Document Upload with Encrypted Storage
- **Story Location:** `.propel/context/tasks/EP-008-I/us_038/us_038.md`
- **Acceptance Criteria:**
  - AC-2: Each uploaded file is validated server-side; encrypted with AES-256 before persisted to document storage; `ClinicalDocument` record created with `processingStatus = Pending`
  - AC-4: Per-file validation errors returned in response body; valid files in the same batch are stored successfully (partial batch)
- **Edge Cases:**
  - Partial batch: each file processed independently — already-persisted files are NOT rolled back if a subsequent file fails validation; failed files returned in response with `success=false` + `errorMessage`
  - Storage unavailable: `IDocumentStorageService` throws `StorageUnavailableException` → HTTP 503, no `ClinicalDocument` rows created, no partial records

## Traceability

| Tag | Requirement |
| --- | ----------- |
| FR-041 | Authenticated Patient uploads PDF clinical documents |
| FR-042 | Max 20 files per batch, max 25 MB per file |
| FR-043 | AES-256 encryption at rest; TLS 1.2+ in transit (enforced by hosting layer / HTTPS) |
| NFR-004 | All patient data encrypted at rest using AES-256 |
| NFR-005 | All data in transit using TLS 1.2+ |
| FR-057 | Audit log every document upload event |
| FR-058 | Log all clinical data modification events |
| DR-005 | `ClinicalDocument` stored as encrypted binary with metadata |

---

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

---

## Applicable Technology Stack

| Layer              | Technology                            | Version |
| ------------------ | ------------------------------------- | ------- |
| Backend            | ASP.NET Core Web API                  | .net 10  |
| Messaging          | MediatR                               | 12.x    |
| Validation         | FluentValidation                      | 11.x    |
| ORM                | Entity Framework Core                 | 9.x     |
| Database           | PostgreSQL 16+ (Neon)                 | 16+     |
| Encryption         | ASP.NET Core Data Protection API      | .net 10  |
| Logging            | Serilog                               | 4.x     |
| Testing            | xUnit + Moq                           | 2.x     |
| AI/ML              | N/A                                   | N/A     |
| Mobile             | N/A                                   | N/A     |

---

## AI References (AI Tasks Only)

| Reference Type           | Value |
| ------------------------ | ----- |
| **AI Impact**            | No    |
| **AIR Requirements**     | N/A   |
| **AI Pattern**           | N/A   |
| **Prompt Template Path** | N/A   |
| **Guardrails Config**    | N/A   |
| **Model Provider**       | N/A   |

---

## Mobile References (Mobile Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **Mobile Impact**    | No    |
| **Platform Target**  | N/A   |
| **Min OS Version**   | N/A   |
| **Mobile Framework** | N/A   |

---

## Task Overview

Implement the server-side clinical document upload pipeline:

1. `POST /api/documents/upload` — multipart/form-data endpoint (Patient role only)
2. `UploadClinicalDocumentsCommandHandler` — per-file loop: validate → encrypt → persist → INSERT `ClinicalDocument` → AuditLog
3. `IDocumentStorageService` / `LocalDocumentStorageService` — AES-256 encryption via Data Protection API; write to configured storage path
4. Per-file `UploadFileResult` response enabling partial batch recovery on the FE
5. 503 path: `StorageUnavailableException` → no records created, `GlobalExceptionFilter` returns 503

---

## Dependent Tasks

- **task_003_db_clinical_document_schema.md** — `ClinicalDocument` EF Core entity and `clinical_documents` table must exist before this handler is wired

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `ClinicalDocumentsController` | `Server/Modules/Clinical/ClinicalDocumentsController.cs` |
| CREATE | `UploadClinicalDocumentsCommand` + `UploadClinicalDocumentsCommandValidator` | `Server/Modules/Clinical/Commands/UploadClinicalDocuments/` |
| CREATE | `UploadClinicalDocumentsCommandHandler` | same folder |
| CREATE | `IDocumentStorageService` interface | `Server/Modules/Clinical/Services/IDocumentStorageService.cs` |
| CREATE | `LocalDocumentStorageService` | `Server/Modules/Clinical/Services/LocalDocumentStorageService.cs` |
| CREATE | `StorageUnavailableException` | `Server/Common/Exceptions/StorageUnavailableException.cs` |
| MODIFY | `GlobalExceptionFilter` | Map `StorageUnavailableException` → HTTP 503 |
| MODIFY | `Program.cs` / DI registration | Register `IDocumentStorageService`, `DocumentStorageSettings` |

---

## Implementation Plan

1. **`IDocumentStorageService`** interface:
   ```csharp
   public interface IDocumentStorageService
   {
       Task<string> StoreEncryptedAsync(
           byte[] fileBytes,
           string fileName,
           Guid patientId,
           CancellationToken ct = default);

       Task<byte[]> RetrieveDecryptedAsync(
           string storagePath,
           CancellationToken ct = default);
   }
   ```

2. **`LocalDocumentStorageService`** implements `IDocumentStorageService`:
   ```csharp
   public class LocalDocumentStorageService : IDocumentStorageService
   {
       private readonly IDataProtector _protector;
       private readonly DocumentStorageSettings _settings;

       // OWASP A02: IDataProtector injected — key ring managed by Data Protection API
       public LocalDocumentStorageService(
           IDataProtectionProvider dpProvider,
           IOptions<DocumentStorageSettings> settings)
       {
           _protector = dpProvider.CreateProtector("ClinicalDocuments.v1");
           _settings  = settings.Value;
       }

       public async Task<string> StoreEncryptedAsync(
           byte[] fileBytes, string fileName, Guid patientId,
           CancellationToken ct)
       {
           var encrypted = _protector.Protect(fileBytes);        // AES-256 via DPAPI
           var fileId    = Guid.NewGuid();
           var dir       = Path.Combine(_settings.BasePath, patientId.ToString());
           Directory.CreateDirectory(dir);                        // idempotent
           var filePath  = Path.Combine(dir, $"{fileId}.bin");
           await File.WriteAllBytesAsync(filePath, encrypted, ct);
           return Path.GetRelativePath(_settings.BasePath, filePath); // relative path stored in DB
       }

       public async Task<byte[]> RetrieveDecryptedAsync(string storagePath, CancellationToken ct)
       {
           var fullPath  = Path.Combine(_settings.BasePath, storagePath);
           var encrypted = await File.ReadAllBytesAsync(fullPath, ct);
           return _protector.Unprotect(encrypted);
       }
   }
   ```
   - Throws `StorageUnavailableException` if `IOException` or `UnauthorizedAccessException` is caught writing to the storage path — indicates the storage volume is unavailable.

3. **`UploadClinicalDocumentsCommand`**:
   ```csharp
   public record UploadClinicalDocumentsCommand(
       Guid PatientId,                              // from JWT — NEVER from request body (OWASP A01)
       IReadOnlyList<IFormFile> Files
   ) : IRequest<UploadBatchResult>;
   ```

4. **`UploadClinicalDocumentsCommandValidator`** (FluentValidation):
   ```csharp
   RuleFor(c => c.Files)
       .NotEmpty().WithMessage("At least one file is required.")
       .Must(f => f.Count <= 20).WithMessage("Maximum 20 files per upload.");

   RuleForEach(c => c.Files)
       .Must(f => f.ContentType == "application/pdf" && f.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
           .WithMessage(f => $"'{f.FileName}': Only PDF files are accepted.")
       .Must(f => f.Length <= 25 * 1024 * 1024)
           .WithMessage(f => $"'{f.FileName}': File too large.");
   ```
   Validation maps each file error to the `UploadFileResult` of that specific file — NOT a 400 on the entire request (to support partial batch). Handler bypasses the FluentValidation pipeline-wide 400 and handles per-file validation inline.

5. **`UploadClinicalDocumentsCommandHandler`** — per-file loop:
   ```csharp
   var results = new List<UploadFileResult>();

   foreach (var file in command.Files)
   {
       // 1. Per-file validation (inline — supports partial batch)
       if (file.ContentType != "application/pdf" || !file.FileName.EndsWith(".pdf"))
       {
           results.Add(new(file.FileName, false, "Only PDF files are accepted."));
           continue;
       }
       if (file.Length > 25 * 1024 * 1024)
       {
           results.Add(new(file.FileName, false, "File too large."));
           continue;
       }

       // 2. Read bytes + magic bytes check (OWASP A05 — prevent content sniffing)
       using var ms = new MemoryStream();
       await file.CopyToAsync(ms, ct);
       var fileBytes = ms.ToArray();
       if (!IsPdfMagicBytes(fileBytes))
       {
           results.Add(new(file.FileName, false, "Only PDF files are accepted."));
           continue;
       }

       // 3. Encrypt + persist to storage (FR-043, NFR-004)
       // StorageUnavailableException propagates up — caught in controller → 503
       var storagePath = await _storageService.StoreEncryptedAsync(
           fileBytes, file.FileName, command.PatientId, ct);

       // 4. INSERT ClinicalDocument
       var document = new ClinicalDocument
       {
           Id               = Guid.NewGuid(),
           PatientId        = command.PatientId,
           FileName         = file.FileName,
           FileSize         = file.Length,
           StoragePath      = storagePath,
           MimeType         = "application/pdf",
           ProcessingStatus = ProcessingStatus.Pending,
           UploadedAt       = DateTime.UtcNow
       };
       _db.ClinicalDocuments.Add(document);
       await _db.SaveChangesAsync(ct);

       // 5. AuditLog (FR-057, FR-058)
       _logger.Information(
           "DocumentUploaded {PatientId} {DocumentId} {FileName} {FileSize}",
           command.PatientId, document.Id, document.FileName, document.FileSize);

       results.Add(new(file.FileName, true, null, document.Id));
   }

   return new UploadBatchResult(results);
   ```

6. **Magic bytes check** (OWASP A05 — defence against MIME spoofing):
   ```csharp
   private static bool IsPdfMagicBytes(byte[] bytes) =>
       bytes.Length >= 5 &&
       bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 &&
       bytes[3] == 0x46 && bytes[4] == 0x2D; // %PDF-
   ```

7. **`ClinicalDocumentsController`**:
   ```csharp
   [ApiController]
   [Route("api/documents")]
   [Authorize(Roles = "Patient")]
   public class ClinicalDocumentsController : ControllerBase
   {
       [HttpPost("upload")]
       [RequestSizeLimit(500 * 1024 * 1024)]   // 20 × 25 MB ceiling
       [RequestFormLimits(MultipartBodyLengthLimit = 500 * 1024 * 1024)]
       public async Task<IActionResult> Upload(
           [FromForm] IFormFileCollection files,
           [FromServices] IMediator mediator,
           CancellationToken ct)
       {
           var patientId = Guid.Parse(
               User.FindFirstValue(ClaimTypes.NameIdentifier)!); // OWASP A01 — from JWT only

           var result = await mediator.Send(
               new UploadClinicalDocumentsCommand(patientId, files.ToList()), ct);

           // 207 Multi-Status if any file failed, 200 if all succeeded
           var statusCode = result.Files.Any(f => !f.Success) ? 207 : 200;
           return StatusCode(statusCode, result);
       }

       [HttpGet]
       public async Task<IActionResult> GetHistory(
           [FromServices] IMediator mediator,
           CancellationToken ct)
       {
           var patientId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
           var result    = await mediator.Send(new GetClinicalDocumentsQuery(patientId), ct);
           return Ok(result);
       }
   }
   ```

8. **`GlobalExceptionFilter`** extension:
   ```csharp
   case StorageUnavailableException:
       context.Result = new ObjectResult(new { error = "Document storage is temporarily unavailable. Please try again shortly." })
           { StatusCode = 503 };
       break;
   ```

9. **`DocumentStorageSettings`** (configuration):
   ```json
   // appsettings.json (development only — non-PHI path)
   "DocumentStorage": {
     "BasePath": "/tmp/clinical-documents"
   }
   ```
   Production: `DOCUMENT_STORAGE_PATH` environment variable overrides `BasePath`. Never hardcoded (OWASP A02).

10. **DI registration** (`Program.cs`):
    ```csharp
    builder.Services.Configure<DocumentStorageSettings>(
        builder.Configuration.GetSection("DocumentStorage"));
    builder.Services.AddScoped<IDocumentStorageService, LocalDocumentStorageService>();
    builder.Services.AddDataProtection()
        .SetApplicationName("PropelIQ.Clinical");
    // In production: .PersistKeysToStackExchangeRedis(redisConnection, "DataProtection-Keys")
    ```

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no Server/ scaffold yet — greenfield ASP.NET Core project)
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Modules/Clinical/ClinicalDocumentsController.cs` | POST /api/documents/upload + GET /api/documents; `[Authorize(Roles="Patient")]`; patientId from JWT |
| CREATE | `Server/Modules/Clinical/Commands/UploadClinicalDocuments/UploadClinicalDocumentsCommand.cs` | MediatR command record + `UploadBatchResult`, `UploadFileResult` DTOs |
| CREATE | `Server/Modules/Clinical/Commands/UploadClinicalDocuments/UploadClinicalDocumentsCommandValidator.cs` | FluentValidation: max 20 files, PDF MIME, 25 MB per file |
| CREATE | `Server/Modules/Clinical/Commands/UploadClinicalDocuments/UploadClinicalDocumentsCommandHandler.cs` | Per-file loop: validate → magic bytes → encrypt → store → INSERT → AuditLog |
| CREATE | `Server/Modules/Clinical/Queries/GetClinicalDocuments/GetClinicalDocumentsQuery.cs` | `GET /api/documents` for upload history |
| CREATE | `Server/Modules/Clinical/Services/IDocumentStorageService.cs` | Interface: `StoreEncryptedAsync`, `RetrieveDecryptedAsync` |
| CREATE | `Server/Modules/Clinical/Services/LocalDocumentStorageService.cs` | Data Protection API encrypt/decrypt + local filesystem I/O |
| CREATE | `Server/Common/Exceptions/StorageUnavailableException.cs` | Thrown by storage service on IO failure |
| MODIFY | `Server/Common/Filters/GlobalExceptionFilter.cs` | Add `StorageUnavailableException` → 503 case |
| MODIFY | `Server/Program.cs` | Register `IDocumentStorageService`, `DocumentStorageSettings`, `AddDataProtection()` |

---

## External References

- [ASP.NET Core .net 10 — File uploads (IFormFile, RequestSizeLimit, multipart)](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-9.0)
- [ASP.NET Core Data Protection — Protect/Unprotect byte arrays](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/using-data-protection?view=aspnetcore-9.0)
- [ASP.NET Core Data Protection — Key storage in Redis (production)](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers?view=aspnetcore-9.0#redis)
- [FluentValidation 11.x — RuleForEach](https://docs.fluentvalidation.net/en/latest/collections.html)
- [MediatR 12.x — IRequest/IRequestHandler](https://github.com/jbogard/MediatR/wiki)
- [OWASP File Upload Cheat Sheet — Magic bytes, MIME validation, storage path](https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html)
- [OWASP A01 — Broken Access Control: patientId from JWT only](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)
- [OWASP A02 — Cryptographic Failures: AES-256 at rest, TLS 1.2+ in transit](https://owasp.org/Top10/A02_2021-Cryptographic_Failures/)
- [FR-041, FR-042, FR-043, FR-057, FR-058 — spec.md lines 183–185, 212](spec.md)
- [DR-005 — ClinicalDocument encrypted binary storage (design.md line 75)](design.md)
- [UC-007 sequence diagram — per-file encrypt → INSERT → AuditLog (models.md)](models.md)

---

## Build Commands

```bash
# Build backend
dotnet build Server/Server.csproj

# Run backend
dotnet run --project Server/Server.csproj

# Run unit tests
dotnet test Server.Tests/Server.Tests.csproj

# Test upload endpoint (curl example — dev only)
curl -X POST https://localhost:5001/api/documents/upload \
  -H "Authorization: Bearer <token>" \
  -F "files=@./test.pdf" \
  -F "files=@./test2.pdf"
```

---

## Implementation Validation Strategy

- [ ] `POST /api/documents/upload` with a valid JWT (Patient role) and 2 valid PDF files returns HTTP 200 with 2 `success=true` results; 2 `ClinicalDocument` rows inserted with `processingStatus=Pending`
- [ ] Uploading a `.docx` file in a batch with a valid PDF: `.docx` result has `success=false`, `errorMessage="Only PDF files are accepted."`; PDF is uploaded and stored; response is HTTP 207
- [ ] Uploading a PDF with content `%PDF-` magic bytes replaced by zeroes returns `success=false`, `errorMessage="Only PDF files are accepted."` (magic bytes guard active)
- [ ] Uploading a PDF >25 MB returns `success=false`, `errorMessage="File too large."`
- [ ] Uploading >20 files returns FluentValidation error for the batch (400 before per-file processing)
- [ ] `patientId` extracted from JWT only — passing a different `patientId` in request body has no effect on stored documents
- [ ] `StorageUnavailableException` from `IDocumentStorageService` (mock via Moq) → controller returns HTTP 503; no `ClinicalDocument` rows written; Serilog error emitted
- [ ] Serilog audit log entry `"DocumentUploaded"` emitted for each successfully persisted file (check log output)

---

## Implementation Checklist

- [ ] Create `POST /api/documents/upload` in `ClinicalDocumentsController`; `[Authorize(Roles="Patient")]`; `[RequestSizeLimit(500MB)]`; `patientId` from `ClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier)` only (OWASP A01)
- [ ] Create `IDocumentStorageService` and `LocalDocumentStorageService`: encrypt file bytes via `IDataProtector.Protect()` (AES-256 Data Protection API, NFR-004, FR-043); write to `{BasePath}/{patientId}/{guid}.bin`; configure `BasePath` from `appsettings.json` + env var `DOCUMENT_STORAGE_PATH` (OWASP A02 — no hardcoded paths)
- [ ] Implement per-file magic bytes check (`%PDF-` prefix) in handler in addition to MIME type — prevents MIME-spoofed file injection (OWASP A05, File Upload OWASP Cheat Sheet)
- [ ] `UploadClinicalDocumentsCommandHandler` processes each file independently: validate → encrypt → `StoreEncryptedAsync` → INSERT `ClinicalDocument {status=Pending}` → Serilog AuditLog `"DocumentUploaded"` (FR-057, FR-058); catch `StorageUnavailableException` propagation to controller
- [ ] Return `UploadBatchResult { Files: UploadFileResult[] }` per file; controller returns 207 Multi-Status if any file failed, 200 if all succeeded (enables partial batch recovery on FE)
- [ ] Register `StorageUnavailableException` in `GlobalExceptionFilter` → HTTP 503 with `{ error: "Document storage is temporarily unavailable. Please try again shortly." }`; no partial `ClinicalDocument` records created when storage is down (edge case)
- [ ] Add `GET /api/documents` endpoint (`GetClinicalDocumentsQuery`) returning `ClinicalDocumentDto[]` for upload history panel (AC-3)
- [ ] Register `IDocumentStorageService → LocalDocumentStorageService`; `DocumentStorageSettings`; `AddDataProtection().SetApplicationName("PropelIQ.Clinical")` in `Program.cs`
