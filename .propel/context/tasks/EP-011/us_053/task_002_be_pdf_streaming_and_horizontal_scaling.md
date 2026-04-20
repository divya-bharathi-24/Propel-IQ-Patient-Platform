# Task - task_002_be_pdf_streaming_and_horizontal_scaling

## Requirement Reference

- **User Story:** us_053 — Frontend Performance & Horizontal Scalability Baseline
- **Story Location:** `.propel/context/tasks/EP-011/us_053/us_053.md`
- **Acceptance Criteria:**
  - AC-2: Load test of 100 concurrent users performing booking and slot queries over 5 minutes yields p95 API response ≤2s; no HTTP 5xx errors (NFR-010).
  - AC-3: Stateless backend serves requests correctly from two horizontal instances using shared Redis session state and PostgreSQL; no session-affinity failures (NFR-016).
  - AC-4: PDF documents up to 50 MB processed without HTTP timeout, memory exhaustion, or OOM; processing time logged for SLA monitoring (NFR-019).
- **Edge Cases:**
  - 50 MB PDF processing exceeds AI p95 target of 30s: AI p95 latency tracked separately from upload; progress indicator shown for operations >5s; SLA breach logged via `PerformanceBehavior` (US_051).

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

| Layer    | Technology                            | Version |
| -------- | ------------------------------------- | ------- |
| Backend  | ASP.NET Core Web API / .NET           | 9       |
| ORM      | Entity Framework Core                 | 9.x     |
| Cache    | Upstash Redis (StackExchange.Redis)   | Serverless |
| Logging  | Serilog                               | 4.x     |

**Note:** All code and libraries MUST be compatible with versions listed above.

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

Three backend concerns for US_053:

**1. PDF streaming upload (AC-4, NFR-019)** — The document upload endpoint must stream the incoming `IFormFile` to storage without buffering the entire 50 MB file in memory. Kestrel's default request body buffer limit (28 MB) must be raised to 55 MB for the upload endpoint only. The upload handler reads from `IFormFile.OpenReadStream()` and writes in 4 MB chunks to blob storage (or local disk for Phase 1). A `ClinicalDocumentUploadedEvent` is dispatched after storage write; AI extraction is handled asynchronously and does NOT block the HTTP response. Processing start time and end time are logged via Serilog with correlation ID.

**2. Statelessness verification + `appsettings.json` configuration audit (AC-3, NFR-016)** — Confirm and document that all per-request state is stored in Redis (session tokens) or PostgreSQL (application data) — not in `IMemoryCache` or static fields. Specifically: replace any `IMemoryCache` usage that holds session or user state with the existing Redis-backed pattern (`IDatabase.StringGetAsync`). Add an `appsettings.json` section `Scalability` with explicit documentation of the stateless contract. Register `IDistributedCache` (StackExchange.Redis-backed) for any ASP.NET Core distributed cache uses.

**3. Kestrel and EF Core connection pool tuning (AC-2, NFR-010)** — Configure Kestrel limits and EF Core connection pool settings suitable for 100 concurrent users. EF Core's default pool size matches the underlying `Npgsql` max pool size; this task sets it explicitly and verifies no connection starvation occurs at 100 concurrent requests.

---

## Dependent Tasks

- `EP-011/us_051/task_002_be_mediatr_pipeline_behaviors.md` — `PerformanceBehavior` tracks p95 latency; PDF processing time must be recorded through this behaviour or a separate Serilog entry.
- `EP-010/us_040/` — AI extraction pipeline must exist to wire up the async event after upload.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `ClinicalDocumentsController` (existing) | API | MODIFY — add `[RequestSizeLimit(57_671_680)]` (55 MB) + `[DisableRequestSizeLimit]` on upload action; use `IFormFile.OpenReadStream()` instead of `CopyToAsync(MemoryStream)` |
| `UploadClinicalDocumentCommandHandler` (existing) | Application | MODIFY — stream-copy in 4 MB chunks to storage; dispatch `ClinicalDocumentUploadedEvent` async; log start/end timestamps |
| `PdfStreamingStorageService` (new) | Infrastructure | CREATE — `IDocumentStorageService.SaveAsync(Stream stream, string fileName)`: reads in 4 MB chunks; writes to `IFileStorage` (local path for Phase 1, Azure Blob for Phase 2); returns storage path |
| `Program.cs` (existing) | API | MODIFY — configure Kestrel `MaxRequestBodySize = 57_671_680` (55 MB); `MaxConcurrentConnections = 200`; configure EF Core Npgsql pool `MaxPoolSize = 50` |
| `appsettings.json` (existing) | Config | MODIFY — add `Kestrel`, `EfCore`, and `Scalability` configuration sections with documented stateless contract |

---

## Implementation Plan

1. **Kestrel upload size limit** — per-action attribute (not global) to keep the limit scoped to the upload endpoint:

   ```csharp
   [HttpPost("upload")]
   [Authorize(Roles = "Staff,Admin")]
   [RequestSizeLimit(57_671_680)]  // 55 MB = 55 * 1024 * 1024
   [RequestFormLimits(MultipartBodyLengthLimit = 57_671_680)]
   public async Task<IActionResult> Upload(
       [FromForm] IFormFile file, CancellationToken ct)
   {
       if (file.Length > 57_671_680)
           return BadRequest("File exceeds 50 MB limit.");

       if (!string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
           return BadRequest("Only PDF files are accepted.");

       var result = await _sender.Send(
           new UploadClinicalDocumentCommand(file, CurrentUserId), ct);
       return Ok(result);
   }
   ```

   **Security:** File size validated twice — once at Kestrel level (prevents OOM from oversized payload) and once in the action (returns clear 400). Content-type check prevents non-PDF uploads.

2. **`PdfStreamingStorageService`** — 4 MB chunk stream-copy:

   ```csharp
   public sealed class PdfStreamingStorageService : IDocumentStorageService
   {
       private const int ChunkSize = 4 * 1024 * 1024; // 4 MB

       public async Task<string> SaveAsync(
           Stream inputStream, string fileName, CancellationToken ct = default)
       {
           var storagePath = Path.Combine(_options.Value.StorageBasePath,
               $"{Guid.NewGuid():N}_{fileName}");

           await using var outputStream = new FileStream(
               storagePath, FileMode.Create, FileAccess.Write,
               FileShare.None, bufferSize: ChunkSize, useAsync: true);

           var buffer = new byte[ChunkSize];
           int bytesRead;
           while ((bytesRead = await inputStream.ReadAsync(buffer, ct)) > 0)
               await outputStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);

           return storagePath;
       }
   }
   ```

   Uses `FileStream` with `useAsync: true` — ensures the OS uses async I/O (completion ports on Windows, io_uring on Linux) rather than blocking thread-pool threads. The 4 MB buffer balances memory pressure against syscall frequency.

3. **`UploadClinicalDocumentCommandHandler`** — streaming + async dispatch:

   ```csharp
   public async Task<UploadClinicalDocumentResponse> Handle(
       UploadClinicalDocumentCommand request, CancellationToken ct)
   {
       var sw = Stopwatch.StartNew();
       await using var stream = request.File.OpenReadStream();
       var storagePath = await _storage.SaveAsync(stream, request.File.FileName, ct);
       sw.Stop();

       Log.Information(
           "PDF upload stored in {DurationMs}ms [FileName={FileName} SizeBytes={SizeBytes}]",
           sw.ElapsedMilliseconds, request.File.FileName, request.File.Length);

       var document = new ClinicalDocument
       {
           Id = Guid.NewGuid(),
           PatientId = request.PatientId,
           FileName = request.File.FileName,
           FileSize = request.File.Length,
           StoragePath = storagePath,
           MimeType = "application/pdf",
           ProcessingStatus = "Pending",
           UploadedAt = DateTimeOffset.UtcNow
       };
       _context.ClinicalDocuments.Add(document);
       await _context.SaveChangesAsync(ct);

       // Async dispatch — does NOT await; extraction runs independently
       _ = _publisher.Publish(
           new ClinicalDocumentUploadedEvent(document.Id), CancellationToken.None);

       return new UploadClinicalDocumentResponse(document.Id, "Pending");
   }
   ```

   The `ClinicalDocumentUploadedEvent` publish uses `CancellationToken.None` — the HTTP request's cancellation token must not cancel the extraction pipeline after the response is returned.

4. **Kestrel and Npgsql tuning in `Program.cs`**:

   ```csharp
   builder.WebHost.ConfigureKestrel(options =>
   {
       // Global limit for non-upload endpoints
       options.Limits.MaxRequestBodySize = 1_048_576; // 1 MB default

       // Connection limits for 100 concurrent users
       options.Limits.MaxConcurrentConnections = 200;
       options.Limits.MaxConcurrentUpgradedConnections = 50; // WebSocket budget
       options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
       options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
   });
   ```

   Npgsql connection pool via EF Core connection string:
   ```
   Host=...;Database=propeliq;Username=...;Password=...;
   Maximum Pool Size=50;Minimum Pool Size=5;
   Connection Idle Lifetime=300;Connection Pruning Interval=60
   ```

   `Maximum Pool Size=50` supports 100 concurrent HTTP requests where each request holds a connection for ≤50ms. At 100 RPS with 50ms average query time, Little's Law: `L = λW = 100 × 0.05 = 5 concurrent connections`. Pool of 50 provides 10× headroom.

5. **Statelessness audit — `appsettings.json` `Scalability` section**:

   ```json
   "Scalability": {
     "SessionStore": "Redis",
     "CacheStore": "Redis",
     "StatelessContract": "No in-process state. All session tokens in Redis (TTL 15min). All application state in PostgreSQL. IMemoryCache only for read-through caches with TTL ≤60s (model version, feature flags).",
     "HorizontalScalingNotes": "Deploy additional instances with same environment variables. No sticky sessions required. Redis Upstash is shared across instances."
   }
   ```

   Scan codebase for `IMemoryCache` usages: the only allowed `IMemoryCache` use is `RedisLiveAiModelConfig` (60-second in-memory cache per instance — acceptable; model version is eventually consistent, not session-critical). All other `IMemoryCache` usages that hold user or session data must be replaced with Redis-backed equivalents.

---

## Current Project State

```
Server/
  API/
    Controllers/
      ClinicalDocumentsController.cs     ← EXISTS — MODIFY
    Program.cs                           ← EXISTS — MODIFY
  Application/
    Commands/
      UploadClinicalDocumentCommand.cs   ← EXISTS — MODIFY handler
  Infrastructure/
    Storage/                             ← create new folder
appsettings.json                         ← EXISTS — MODIFY
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Infrastructure/Storage/PdfStreamingStorageService.cs` | 4 MB chunked stream-copy; `FileStream` with `useAsync: true`; returns storage path |
| MODIFY | `Server/API/Controllers/ClinicalDocumentsController.cs` | Add `[RequestSizeLimit(57_671_680)]` + `[RequestFormLimits]`; file size + content-type validation; 400 on violation |
| MODIFY | `Server/Application/Commands/UploadClinicalDocumentCommandHandler.cs` | `IFormFile.OpenReadStream()` (not `CopyToAsync(MemoryStream)`); `Stopwatch` timing; log upload duration; async event publish with `CancellationToken.None` |
| MODIFY | `Server/API/Program.cs` | Kestrel limits: `MaxRequestBodySize = 1MB` global, `MaxConcurrentConnections = 200`; Npgsql pool `Maximum Pool Size=50` in connection string |
| MODIFY | `appsettings.json` | Add `Scalability` section; add `Kestrel` and storage path config |

---

## External References

- [ASP.NET Core 9 — Large File Uploads (Streaming)](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-9.0#upload-large-files-with-streaming) — `IFormFile.OpenReadStream()` vs buffered `MemoryStream`
- [ASP.NET Core 9 — `RequestSizeLimit` attribute](https://learn.microsoft.com/en-us/aspnet/core/mvc/filters?view=aspnetcore-9.0#action-filters) — per-action limit override for upload endpoint
- [Kestrel — Connection Limits](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options?view=aspnetcore-9.0) — `MaxConcurrentConnections`, `MaxRequestBodySize`
- [Npgsql — Connection Pooling](https://www.npgsql.org/doc/connection-string-parameters.html) — `Maximum Pool Size`, `Minimum Pool Size`, `Connection Idle Lifetime`
- [NFR-016 (design.md)](../../../docs/design.md) — Horizontal scaling without code changes; stateless backend
- [NFR-019 (design.md)](../../../docs/design.md) — PDF up to 50 MB without timeout or memory exhaustion

---

## Build Commands

- Refer to [`.propel/build/`](../../../../build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [ ] Upload a 50 MB PDF file — verify the endpoint returns HTTP 200; `ClinicalDocument` row created with `processingStatus = "Pending"`; no OOM or timeout
- [ ] Upload a 56 MB file — verify HTTP 400 with "File exceeds 50 MB limit." message
- [ ] Upload a non-PDF file — verify HTTP 400 with "Only PDF files are accepted."
- [ ] Verify `UploadClinicalDocumentCommandHandler` uses `OpenReadStream()` — no `MemoryStream` allocation of the full file
- [ ] Verify `ClinicalDocumentUploadedEvent` is published with `CancellationToken.None` — HTTP response returns before extraction completes
- [ ] Verify `MaxConcurrentConnections = 200` and Npgsql `Maximum Pool Size=50` in `Program.cs` Kestrel config
- [ ] Run two instances in Docker Compose with shared Redis + PostgreSQL — verify session tokens work across both instances (no 401 on instance switch)

---

## Implementation Checklist

- [ ] Create `PdfStreamingStorageService`: 4 MB chunk loop using `ReadAsync`/`WriteAsync` on `FileStream(useAsync: true)`; register as `IDocumentStorageService` scoped
- [ ] Modify `ClinicalDocumentsController`: `[RequestSizeLimit(57_671_680)]` + `[RequestFormLimits(MultipartBodyLengthLimit=57_671_680)]`; file size guard (return 400 > 55 MB); content-type guard (return 400 if not `application/pdf`)
- [ ] Modify `UploadClinicalDocumentCommandHandler`: `OpenReadStream()` not `MemoryStream`; `Stopwatch` timing with Serilog log; async publish `ClinicalDocumentUploadedEvent` with `CancellationToken.None`
- [ ] Modify `Program.cs`: `ConfigureKestrel` with `MaxRequestBodySize = 1MB` (global), `MaxConcurrentConnections = 200`; add Npgsql pool size to connection string in `appsettings.json`
- [ ] Audit `IMemoryCache` usages: only allow per-instance read-through caches with ≤60s TTL (model version); replace any session/user state `IMemoryCache` with Redis-backed equivalent
- [ ] Modify `appsettings.json`: add `Scalability` section documenting stateless contract; add `Kestrel` limits section; add storage base path config
