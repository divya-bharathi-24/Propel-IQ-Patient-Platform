# Task - task_002_be_patient_profile_api

## Requirement Reference

- **User Story:** us_015 — Patient Profile View & Structured Demographic Edit
- **Story Location:** `.propel/context/tasks/EP-002/us_015/us_015.md`
- **Acceptance Criteria:**
  - AC-1: `GET /api/patients/me` returns the authenticated patient's full demographic profile including all locked and non-locked fields
  - AC-2: `PATCH /api/patients/me` persists changes to non-locked fields, returns the updated profile, and writes an immutable AuditLog entry with action `"PatientProfileUpdated"`, userId, IP, and UTC timestamp
  - AC-3: `PATCH /api/patients/me` ignores any payload values targeting locked fields (`name`, `dateOfBirth`, `biologicalSex`) — they are stripped by the command before persistence
  - AC-4: `PATCH /api/patients/me` returns HTTP 400 with per-field errors for invalid input (e.g., malformed phone); returns HTTP 409 with `{ message: "Conflict", currentETag: "..." }` if `If-Match` header does not match the current row version (optimistic concurrency)
- **Edge Cases:**
  - Session expiry: JWT middleware returns 401; client handles draft preservation (frontend responsibility)
  - Non-Patient JWT role calling `GET /api/patients/me` or `PATCH /api/patients/me` → HTTP 403 (NFR-006 RBAC)

---

## Design References (Frontend Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **UI Impact**        | No    |
| **Figma URL**        | N/A   |
| **Wireframe Status** | N/A   |
| **Wireframe Type**   | N/A   |
| **Wireframe Path/URL** | N/A |
| **Screen Spec**      | N/A   |
| **UXR Requirements** | N/A   |
| **Design Tokens**    | N/A   |

---

## Applicable Technology Stack

| Layer              | Technology                  | Version |
| ------------------ | --------------------------- | ------- |
| Backend            | ASP.NET Core Web API        | .net 10  |
| Backend Messaging  | MediatR                     | 12.x    |
| Backend Validation | FluentValidation            | 11.x    |
| ORM                | Entity Framework Core       | 9.x     |
| Encryption         | `IPhiEncryptionService` (from US_014 task_002) | — |
| Logging            | Serilog                     | 4.x     |
| Testing — Unit     | xUnit + Moq                 | 2.x     |
| Database           | PostgreSQL                  | 16+     |
| AI/ML              | N/A                         | N/A     |
| Mobile             | N/A                         | N/A     |

> All code and libraries MUST be compatible with versions above.

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

Implement two Patient Module endpoints in the ASP.NET Core .net 10 modular monolith:

1. **`GET /api/patients/me`** — reads the authenticated patient's full demographic record from PostgreSQL (PHI fields decrypted via `IPhiEncryptionService`), serialises to `PatientProfileDto`, and returns with an `ETag` header derived from the patient's `RowVersion` (used for optimistic concurrency on PATCH).

2. **`PATCH /api/patients/me`** — validates the `If-Match` header against the current `RowVersion`, rejects locked-field mutations silently (only non-locked fields are applied), validates the payload via FluentValidation, persists changes, writes an AuditLog entry, and returns the updated `PatientProfileDto` with a fresh `ETag`.

RBAC: both endpoints are decorated `[Authorize(Roles = "Patient")]` — non-Patient callers receive HTTP 403 (NFR-006).

---

## Dependent Tasks

- **task_003_db_patient_demographics_schema** (EP-002/us_015) — extended `patients` columns (address, biologicalSex, emergencyContact, communicationPreferences, insurerName, memberId, groupNumber, rowVersion) must exist
- **US_014 task_002_be_argon2_phi_encryption_service** (EP-001) — `IPhiEncryptionService` must be registered to decrypt PHI fields on read
- **US_011** (EP-001) — JWT auth middleware must be active so `[Authorize]` resolves patient identity from token claims

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `PatientController` (or extend if exists) | `Server/Modules/Patient/` |
| CREATE | `GetPatientProfileQuery` + `GetPatientProfileQueryHandler` | Patient Module — Application Layer |
| CREATE | `UpdatePatientProfileCommand` + `UpdatePatientProfileCommandHandler` | Patient Module — Application Layer |
| CREATE | `UpdatePatientProfileValidator` (FluentValidation) | Patient Module — Application Layer |
| CREATE | `PatientProfileDto` + `UpdatePatientProfileDto` | Patient Module — Application Layer |
| MODIFY | `IPatientRepository` + EF Core implementation | Add `GetByIdAsync`, `UpdateAsync` with row-version check |
| MODIFY | `AuditLogRepository` | Reuse INSERT-only pattern |
| MODIFY | `Program.cs` | Register new MediatR handlers |

---

## Implementation Plan

1. **`GetPatientProfileQuery`** (MediatR `IRequest<PatientProfileDto>`):
   - Extract `patientId` from `ClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier)` in the controller (never from request body — AC security)
   - Handler: `IPatientRepository.GetByIdAsync(patientId)` → decrypt PHI fields via `IPhiEncryptionService.Decrypt()`
   - Map to `PatientProfileDto`: all fields including locked (name, dateOfBirth, biologicalSex) and non-locked (phone, address, emergencyContact, communicationPreferences, insurerName, memberId, groupNumber)
   - Controller action: return 200 with `PatientProfileDto` body + `ETag: "{Convert.ToBase64String(patient.RowVersion)}"` response header

2. **`UpdatePatientProfileCommand`** (MediatR `IRequest<PatientProfileDto>`):
   - Input: `PatientId` (from JWT claim), `IfMatchETag` (from `If-Match` request header), `UpdatePatientProfileDto` (payload)
   - Handler steps:
     a. Load patient: `IPatientRepository.GetByIdAsync(patientId)` → throw `NotFoundException` if not found
     b. Concurrency check: compare `Convert.ToBase64String(patient.RowVersion)` with `IfMatchETag` → throw `ConcurrencyConflictException` (→ HTTP 409 with `currentETag`) if mismatch
     c. Apply only non-locked fields from DTO: `phone`, `address`, `emergencyContact`, `communicationPreferences`, `insurerName`, `memberId`, `groupNumber` — locked fields (`name`, `dateOfBirth`, `biologicalSex`) are never touched regardless of payload content (AC-3)
     d. Re-encrypt updated PHI fields (`phone`, `address` street data) via `IPhiEncryptionService.Encrypt()`
     e. `IPatientRepository.UpdateAsync(patient)` — EF Core handles `xmin` row-version update atomically
     f. INSERT `AuditLog`: `action = "PatientProfileUpdated"`, `entityType = "Patient"`, `entityId = patientId`, `details = { updatedFields: [...non-null keys in DTO] }` (no PHI values in audit details), `IpAddress`, `Timestamp = UtcNow`
     g. Return updated `PatientProfileDto` with refreshed `ETag`

3. **`UpdatePatientProfileValidator`** (FluentValidation):
   - `Phone`: optional; when provided, `Matches(@"^\+?[1-9]\d{1,14}$")` with message "Phone must be in international format (e.g. +1-202-555-0123)"
   - `Address.Street`, `Address.City`, `Address.State`, `Address.PostalCode`, `Address.Country`: each `MaximumLength(200)` when provided
   - `EmergencyContact.Name`: `MaximumLength(200)` when provided
   - `EmergencyContact.Phone`: same phone regex as above when provided
   - `EmergencyContact.Relationship`: `MaximumLength(100)` when provided
   - `InsurerName`, `MemberId`, `GroupNumber`: `MaximumLength(200)` each when provided
   - No field is required (PATCH semantics — partial update)

4. **Optimistic concurrency via PostgreSQL `xmin`**:
   - EF Core 9 supports PostgreSQL `xmin` system column as a row-version concurrency token via Npgsql
   - `PatientConfiguration.cs`: `.UseXminAsConcurrencyToken()` on the `Patient` entity
   - On `SaveChangesAsync()` conflict, EF Core throws `DbUpdateConcurrencyException` → catch in handler → throw `ConcurrencyConflictException` with current `RowVersion`

5. **`PatientController`** endpoint wiring:
   - `[HttpGet("me")]` → dispatch `GetPatientProfileQuery`; set `ETag` header on response
   - `[HttpPatch("me")]` → read `If-Match` header → dispatch `UpdatePatientProfileCommand`; set `ETag` on success response
   - Both decorated `[Authorize(Roles = "Patient")]`
   - `[HttpPatch]` decorated `[Consumes("application/merge-patch+json")]` for semantic correctness

6. **`PatientProfileDto`** shape:
   ```csharp
   record PatientProfileDto(
       Guid Id, string Name, DateOnly DateOfBirth, string BiologicalSex,
       string Email, string? Phone,
       AddressDto? Address,
       EmergencyContactDto? EmergencyContact,
       CommunicationPreferencesDto? CommunicationPreferences,
       string? InsurerName, string? MemberId, string? GroupNumber
   );
   ```
   Locked fields are included in the read DTO (for display) but never accepted in the write DTO.

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no Server/ scaffold yet — greenfield ASP.NET Core project)
```

> Update with actual `Server/` tree after scaffold is complete.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Modules/Patient/PatientController.cs` | Endpoints: `GET /api/patients/me`, `PATCH /api/patients/me` |
| CREATE | `Server/Modules/Patient/Queries/GetPatientProfileQuery.cs` | MediatR query + result |
| CREATE | `Server/Modules/Patient/Queries/GetPatientProfileQueryHandler.cs` | Load patient, decrypt PHI, map to DTO, return with ETag |
| CREATE | `Server/Modules/Patient/Commands/UpdatePatientProfileCommand.cs` | MediatR command + result |
| CREATE | `Server/Modules/Patient/Commands/UpdatePatientProfileCommandHandler.cs` | Concurrency check, field filter, encrypt, persist, audit log |
| CREATE | `Server/Modules/Patient/Validators/UpdatePatientProfileValidator.cs` | FluentValidation: phone regex, MaxLength fields |
| CREATE | `Server/Modules/Patient/Dtos/PatientProfileDto.cs` | Read DTO with all fields |
| CREATE | `Server/Modules/Patient/Dtos/UpdatePatientProfileDto.cs` | Write DTO with non-locked fields only |
| CREATE | `Server/Modules/Patient/Dtos/AddressDto.cs` | Nested address value object |
| CREATE | `Server/Modules/Patient/Dtos/EmergencyContactDto.cs` | Nested emergency contact value object |
| CREATE | `Server/Modules/Patient/Dtos/CommunicationPreferencesDto.cs` | Nested communication preferences |
| CREATE | `Server/Modules/Patient/Exceptions/ConcurrencyConflictException.cs` | Domain exception for optimistic concurrency conflict |
| MODIFY | `Server/Infrastructure/Repositories/PatientRepository.cs` | Add `GetByIdAsync()`, `UpdateAsync()` methods |
| MODIFY | `Server/Program.cs` | Register new MediatR handlers and validators |

---

## External References

- [ASP.NET Core — ClaimsPrincipal.FindFirstValue](https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claimsprincipal.findfirstvalue)
- [EF Core + Npgsql — xmin Concurrency Token](https://www.npgsql.org/efcore/modeling/concurrency.html)
- [HTTP ETag and If-Match — RFC 7232](https://httpwg.org/specs/rfc7232.html)
- [ASP.NET Core — Reading Request Headers](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/request-response?view=aspnetcore-9.0#read-headers)
- [FluentValidation 11 — Optional / Conditional Rules](https://docs.fluentvalidation.net/en/latest/conditions.html)
- [MediatR 12 — Query Pattern](https://github.com/jbogard/MediatR/wiki)
- [OWASP A01 — Broken Access Control (user accesses only own record)](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)
- [HIPAA — Minimum Necessary Standard (audit log contains no PHI values)](https://www.hhs.gov/hipaa/for-professionals/privacy/guidance/minimum-necessary-requirement/index.html)

---

## Build Commands

```bash
# Restore packages
dotnet restore

# Build
dotnet build

# Run API
dotnet run --project Server/Server.csproj

# Run unit tests
dotnet test

# Apply EF Core migrations
dotnet ef database update --project Server/Server.csproj
```

---

## Implementation Validation Strategy

- [ ] Unit tests pass for `GetPatientProfileQueryHandler` (happy path, patient not found → 404)
- [ ] Unit tests pass for `UpdatePatientProfileCommandHandler` (happy path, ETag mismatch → 409, locked field in payload is silently ignored)
- [ ] Unit tests pass for `UpdatePatientProfileValidator` (valid phone passes, invalid phone → 400 with expected message, oversized string → 400)
- [ ] `GET /api/patients/me` response includes `ETag` header
- [ ] `PATCH /api/patients/me` with matching `If-Match` returns 200 and updated `ETag`
- [ ] `PATCH /api/patients/me` with stale `If-Match` returns 409 with `currentETag` in body
- [ ] `PATCH /api/patients/me` with `name` in payload: `name` is unchanged in DB after request
- [ ] `GET /api/patients/me` returns 403 when called with Staff or Admin JWT role
- [ ] AuditLog entry created on successful PATCH with no PHI field values in `details` JSONB
- [ ] PHI fields (phone, address) returned from `GET` are decrypted plaintext (not ciphertext)
- [ ] Integration tests pass (if applicable)

---

## Implementation Checklist

- [ ] Create `PatientController` with `[Authorize(Roles = "Patient")]`; wire `GET /api/patients/me` and `PATCH /api/patients/me`
- [ ] Create `GetPatientProfileQueryHandler`: extract patientId from JWT claim, load patient, decrypt PHI, map to `PatientProfileDto`, return with ETag
- [ ] Create `UpdatePatientProfileCommandHandler`: load patient, compare ETag (xmin), apply non-locked fields only, encrypt PHI, save, write AuditLog
- [ ] Create `UpdatePatientProfileValidator`: phone regex, MaxLength on all string fields (all optional — PATCH semantics)
- [ ] Create `ConcurrencyConflictException`; map to HTTP 409 in `GlobalExceptionFilter` (from US_014 task_001)
- [ ] Map `patientId` from `ClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier)` — never from request body
- [ ] Ensure `locked fields` (name, dateOfBirth, biologicalSex) are absent from `UpdatePatientProfileDto` type definition
- [ ] Register new MediatR handlers and validators in `Program.cs`
