# Task - TASK_002

## Requirement Reference

- **User Story**: US_016 — Patient Dashboard Aggregation
- **Story Location**: `.propel/context/tasks/EP-002/us_016/us_016.md`
- **Acceptance Criteria**:
  - AC-1: Endpoint returns upcoming appointments with date, time, specialty, and status for the authenticated patient
  - AC-2: Endpoint returns whether a pending intake record exists for each booked appointment (no `completedAt` on `IntakeRecord`)
  - AC-3: Endpoint returns document upload history with file names, upload dates, and processing statuses
  - AC-4: Endpoint returns `viewVerified` boolean derived from `patients.view_verified_at IS NOT NULL`
- **Edge Cases**:
  - Patient with no upcoming appointments: `upcomingAppointments = []` (empty array, not null — consumer handles empty state)
  - Patient with a Failed-status document: `processingStatus = 'Failed'` is included in the response so the client can show retry option
  - RBAC: endpoint must reject non-Patient role tokens with HTTP 403 (Patients cannot access other patients' dashboards — AD-4, NFR-006)

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

| Layer              | Technology            | Version    |
| ------------------ | --------------------- | ---------- |
| Backend            | ASP.NET Core Web API  | .net 10    |
| Backend Messaging  | MediatR               | 12.x       |
| Backend Validation | FluentValidation      | 11.x       |
| ORM                | Entity Framework Core | 9.x        |
| Database           | PostgreSQL            | 16+        |
| Cache              | Upstash Redis         | Serverless |
| Logging            | Serilog               | 4.x        |
| AI/ML              | N/A                   | N/A        |
| Mobile             | N/A                   | N/A        |

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

Implement the `GET /api/patient/dashboard` read endpoint following the CQRS query pattern (AD-2) via MediatR. This endpoint is called exactly once per dashboard page load and returns an aggregated `PatientDashboardResponse` combining data from four domain entities: `Appointment`, `IntakeRecord`, `ClinicalDocument`, and `Patient.view_verified_at`.

**Performance target**: Response within 2 seconds at p95 under normal load (NFR-001). Achieved via:

- A single EF Core projection query using `Select()` to fetch only required columns (no `Include()` → avoids N+1 and over-fetching)
- Filtering `Appointment.status NOT IN ('Completed', 'Cancelled')` to limit "upcoming" set
- Left-joining `IntakeRecord` on `appointmentId` to determine `hasPendingIntake` in one query
- Scoping all queries by `patientId` extracted from the authenticated JWT `sub` claim (never from a URL parameter — OWASP A01: Broken Access Control)

**RBAC**: Route decorated with `[Authorize(Roles = "Patient")]`. Non-Patient tokens (Staff, Admin) receive HTTP 403. The `patientId` is always read from the JWT — not from any request parameter — preventing horizontal privilege escalation.

## Dependent Tasks

- **US_016 / TASK_003** — `patients.view_verified_at` column must exist before this handler can query it.
- **US_006** — `Appointment`, `IntakeRecord`, `ClinicalDocument` EF Core entities and migrations must exist.
- **US_011 / TASK_002** — JWT authentication and `SessionAliveMiddleware` must be active.

## Impacted Components

| Component                         | Status | Location                                                                                    |
| --------------------------------- | ------ | ------------------------------------------------------------------------------------------- |
| `PatientController`               | NEW    | `Server/Modules/Patient/PatientController.cs`                                               |
| `GetPatientDashboardQuery`        | NEW    | `Server/Modules/Patient/Queries/GetPatientDashboard/GetPatientDashboardQuery.cs`            |
| `GetPatientDashboardQueryHandler` | NEW    | `Server/Modules/Patient/Queries/GetPatientDashboard/GetPatientDashboardQueryHandler.cs`     |
| `PatientDashboardResponse` (DTO)  | NEW    | `Server/Modules/Patient/Queries/GetPatientDashboard/PatientDashboardResponse.cs`            |
| `UpcomingAppointmentDto` (DTO)    | NEW    | `Server/Modules/Patient/Queries/GetPatientDashboard/PatientDashboardResponse.cs`            |
| `DocumentHistoryDto` (DTO)        | NEW    | `Server/Modules/Patient/Queries/GetPatientDashboard/PatientDashboardResponse.cs`            |
| `Program.cs`                      | MODIFY | Register `PatientController` (if using minimal API) or ensure controller scanning is active |

## Implementation Plan

1. **Route and Controller**:

   ```csharp
   [ApiController]
   [Route("api/patient")]
   [Authorize(Roles = "Patient")]
   public sealed class PatientController : ControllerBase
   {
       private readonly IMediator _mediator;
       public PatientController(IMediator mediator) => _mediator = mediator;

       [HttpGet("dashboard")]
       [ProducesResponseType<PatientDashboardResponse>(200)]
       [ProducesResponseType(401)]
       [ProducesResponseType(403)]
       public async Task<IActionResult> GetDashboard(CancellationToken ct)
       {
           var patientId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
           var result = await _mediator.Send(new GetPatientDashboardQuery(patientId), ct);
           return Ok(result);
       }
   }
   ```

   - `patientId` is always derived from `User.FindFirstValue(ClaimTypes.NameIdentifier)` — never from a URL parameter or request body. This prevents horizontal privilege escalation (OWASP A01).

2. **`GetPatientDashboardQuery`** (MediatR `IRequest<PatientDashboardResponse>`):

   ```csharp
   public sealed record GetPatientDashboardQuery(Guid PatientId)
       : IRequest<PatientDashboardResponse>;
   ```

3. **`GetPatientDashboardQueryHandler`** — single-query aggregation strategy:

   **Step A — Upcoming appointments with pending intake flag:**

   ```csharp
   var upcomingAppointments = await _dbContext.Appointments
       .Where(a => a.PatientId == query.PatientId
               && a.Status != AppointmentStatus.Completed
               && a.Status != AppointmentStatus.Cancelled
               && a.Date >= DateOnly.FromDateTime(DateTime.UtcNow))
       .OrderBy(a => a.Date).ThenBy(a => a.TimeSlotStart)
       .Select(a => new UpcomingAppointmentDto(
           a.Id,
           a.Date,
           a.TimeSlotStart,
           a.Specialty.Name,
           a.Status.ToString(),
           !_dbContext.IntakeRecords.Any(i =>
               i.AppointmentId == a.Id && i.CompletedAt != null)
       ))
       .AsNoTracking()
       .ToListAsync(ct);
   ```

   > The correlated sub-query for `hasPendingIntake` is evaluated server-side by EF Core. For the expected data scale (patients with < 50 appointments), this is well within the 2-second p95 target (NFR-001).

   **Step B — Document upload history:**

   ```csharp
   var documents = await _dbContext.ClinicalDocuments
       .Where(d => d.PatientId == query.PatientId)
       .OrderByDescending(d => d.UploadedAt)
       .Select(d => new DocumentHistoryDto(
           d.Id,
           d.FileName,
           d.UploadedAt,
           d.ProcessingStatus.ToString()
       ))
       .AsNoTracking()
       .ToListAsync(ct);
   ```

   **Step C — 360° view verified status:**

   ```csharp
   var viewVerified = await _dbContext.Patients
       .Where(p => p.Id == query.PatientId)
       .Select(p => p.ViewVerifiedAt != null)
       .SingleAsync(ct);
   ```

   **Step D — Return aggregated response:**

   ```csharp
   return new PatientDashboardResponse(
       UpcomingAppointments: upcomingAppointments,
       Documents: documents,
       ViewVerified: viewVerified
   );
   ```

4. **`PatientDashboardResponse` DTO** (records with `init` properties for immutability):

   ```csharp
   public sealed record PatientDashboardResponse(
       IReadOnlyList<UpcomingAppointmentDto> UpcomingAppointments,
       IReadOnlyList<DocumentHistoryDto> Documents,
       bool ViewVerified
   );

   public sealed record UpcomingAppointmentDto(
       Guid Id,
       DateOnly Date,
       TimeOnly TimeSlotStart,
       string Specialty,
       string Status,
       bool HasPendingIntake
   );

   public sealed record DocumentHistoryDto(
       Guid Id,
       string FileName,
       DateTime UploadedAt,
       string ProcessingStatus
   );
   ```

5. **RBAC enforcement**: `[Authorize(Roles = "Patient")]` on the controller class. The JWT `role` claim is validated by `JwtBearerDefaults` authentication (configured in US_011 TASK_002 `Program.cs`). Staff or Admin tokens receive HTTP 403 automatically via the ASP.NET Core authorization pipeline — no additional code required.

6. **Caching consideration** (NFR-001, NFR-010): Dashboard data is patient-specific and expected to change on each login session. No Redis cache is applied at this endpoint. If performance profiling reveals p95 > 2 s under load, a short-lived (30-second) per-patient Redis cache can be added as a delta task. Document this decision in the `Program.cs` registration comment.

7. **Serialization**: The ASP.NET Core JSON serializer (`System.Text.Json`) will serialize `DateOnly` and `TimeOnly` correctly as ISO 8601 strings in .net 10. Ensure `JsonSerializerOptions` registered in `Program.cs` includes `DateOnly`/`TimeOnly` converters (built-in since .net 10).

## Current Project State

```
Server/
└── Modules/
    └── Patient/                     ← NEW module
        ├── PatientController.cs
        └── Queries/
            └── GetPatientDashboard/ ← NEW CQRS query
```

> Greenfield project. All paths are target locations per scaffold convention.

## Expected Changes

| Action | File Path                                                                               | Description                                                                                                             |
| ------ | --------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| CREATE | `Server/Modules/Patient/PatientController.cs`                                           | `[Authorize(Roles="Patient")]` controller; `GET /api/patient/dashboard` dispatches to MediatR                           |
| CREATE | `Server/Modules/Patient/Queries/GetPatientDashboard/GetPatientDashboardQuery.cs`        | MediatR query record wrapping `PatientId`                                                                               |
| CREATE | `Server/Modules/Patient/Queries/GetPatientDashboard/GetPatientDashboardQueryHandler.cs` | 3-step EF Core projection; returns aggregated `PatientDashboardResponse`                                                |
| CREATE | `Server/Modules/Patient/Queries/GetPatientDashboard/PatientDashboardResponse.cs`        | Immutable C# records: `PatientDashboardResponse`, `UpcomingAppointmentDto`, `DocumentHistoryDto`                        |
| MODIFY | `Server/Program.cs`                                                                     | Ensure controller discovery includes `Patient` module; confirm `DateOnly`/`TimeOnly` JSON serializer options registered |

## External References

- [ASP.NET Core .net 10 — Controller Authorization with Roles](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-9.0) — `[Authorize(Roles = "Patient")]` attribute
- [EF Core 9 — Projections with `Select()` and `AsNoTracking()`](https://learn.microsoft.com/en-us/ef/core/querying/tracking#no-tracking-queries) — Efficient read-only projections without entity materialization
- [EF Core — Correlated subquery in `Select()`](https://learn.microsoft.com/en-us/ef/core/querying/related-data/select) — Nested `Any()` translated to EXISTS subquery in SQL
- [MediatR 12.x — Request/Handler pattern](https://github.com/jbogard/MediatR/wiki) — `IRequest<T>` / `IRequestHandler<TRequest, TResponse>`
- [System.Text.Json — DateOnly/TimeOnly support (.net 10+)](https://learn.microsoft.com/en-us/dotnet/standard/datetime/system-text-json-support) — Built-in ISO 8601 serialization for `DateOnly` and `TimeOnly`
- [OWASP A01 — Broken Access Control](https://owasp.org/Top10/A01_2021-Broken_Access_Control/) — Never use client-supplied IDs for data scoping; always use authenticated identity claims
- [OWASP A03 — Injection](https://owasp.org/Top10/A03_2021-Injection/) — EF Core parameterized queries prevent SQL injection; never interpolate user input into raw SQL
- [AD-2: CQRS within Services (design.md)](../.propel/context/docs/design.md) — Separate query/command handlers; optimized read models for dashboards
- [NFR-001 Performance target (design.md)](../.propel/context/docs/design.md) — 2-second p95 API response; enforce via load test post-implementation

## Build Commands

```bash
# Build backend
dotnet build Server/PropelIQ.Server.csproj

# Run locally
dotnet run --project Server/PropelIQ.Server.csproj

# Test the endpoint manually (replace {TOKEN} with a valid Patient JWT)
curl -H "Authorization: Bearer {TOKEN}" https://localhost:5001/api/patient/dashboard

# Run tests
dotnet test --project Server.Tests/Server.Tests.csproj
```

## Implementation Validation Strategy

- [ ] Unit tests pass (to be planned separately via `plan-unit-test` workflow)
- [ ] `GET /api/patient/dashboard` returns HTTP 200 with all four data fields populated for a Patient with test data
- [ ] Response `upcomingAppointments` is empty array (not null) when patient has no upcoming appointments
- [ ] `hasPendingIntake = true` for appointments with no `completedAt` IntakeRecord; `hasPendingIntake = false` for appointments with a completed intake
- [ ] Documents list includes entries with `processingStatus = 'Failed'` — not filtered out
- [ ] `viewVerified = true` when `patients.view_verified_at IS NOT NULL`; `viewVerified = false` when NULL
- [ ] Request with Staff or Admin JWT returns HTTP 403 (RBAC enforcement)
- [ ] Request without JWT returns HTTP 401
- [ ] `patientId` from a different patient's JWT cannot be used to retrieve another patient's data — horizontal privilege escalation prevented (use two test patients to verify)
- [ ] API p95 response time < 2 seconds for a patient with 20 appointments, 10 documents (NFR-001) — verify via HttpClient integration test timing

## Implementation Checklist

- [x] Create `PatientController` with `[Authorize(Roles = "Patient")]` and `GET /api/patient/dashboard`; extract `patientId` from JWT claims only
- [x] Create `GetPatientDashboardQuery` record with `PatientId` property
- [x] Implement `GetPatientDashboardQueryHandler`: upcoming appointments projection with correlated `hasPendingIntake` sub-query
- [x] Implement documents query: descending order by `uploadedAt`, include all `processingStatus` values including `Failed`
- [x] Implement 360° view status query: `patients.view_verified_at IS NOT NULL`
- [x] Create `PatientDashboardResponse`, `UpcomingAppointmentDto`, `DocumentHistoryDto` immutable record DTOs
- [x] Confirm `DateOnly`/`TimeOnly` JSON serializer options in `Program.cs`; add if missing
