# Task - task_002_be_noshow_risk_engine

## Requirement Reference

- **User Story:** us_031 — No-Show Risk Score Calculation & Color-Coded Staff Display
- **Story Location:** `.propel/context/tasks/EP-006/us_031/us_031.md`
- **Acceptance Criteria:**
  - AC-1: When the calculation job runs after a new booking, a `NoShowRisk` record is created with `score` (decimal 0–1), `severity` (Low/Medium/High), `factors` (JSONB listing each contributing indicator), and `calculatedAt` timestamp
  - AC-3: When the AI augmentation service is unavailable, the rule-based engine runs independently and produces a score; degraded mode is logged
  - AC-4: When risk factors change (e.g., intake completed), the recalculation job updates the existing `NoShowRisk` record with a new `score` and `calculatedAt`
- **Edge Cases:**
  - No prior appointment history: `priorNoShowHistory` factor defaults to `neutral (0.5)`; overall score defaults to Medium until history accumulates
  - Missing required data fields: score defaults to `0.5` (Medium); a `factors` entry is added: `{ "name": "DataAvailability", "note": "Insufficient data for full assessment", "contribution": 0 }`

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

| Layer              | Technology            | Version |
| ------------------ | --------------------- | ------- |
| Backend            | ASP.NET Core Web API  | .NET 9  |
| Background Jobs    | .NET BackgroundService (IHostedService) | .NET 9 |
| Backend Messaging  | MediatR               | 12.x    |
| Backend Validation | FluentValidation      | 11.x    |
| ORM                | Entity Framework Core | 9.x     |
| Logging            | Serilog               | 4.x     |
| Testing — Unit     | xUnit + Moq           | 2.x     |
| Database           | PostgreSQL            | 16+     |
| AI/ML              | N/A (delegates to `IAiNoShowRiskAugmenter` defined in task_003) | N/A |
| Mobile             | N/A                   | N/A     |

> All code and libraries MUST be compatible with versions above.

---

## AI References (AI Tasks Only)

| Reference Type           | Value |
| ------------------------ | ----- |
| **AI Impact**            | Yes (via `IAiNoShowRiskAugmenter` interface — implemented in task_003) |
| **AIR Requirements**     | AIR-007 (rule-based factors augmented by AI); AC-3 (AI unavailable → rule-based runs independently) |
| **AI Pattern**           | N/A (AI integration via interface; degraded-mode handled in this layer) |
| **Prompt Template Path** | N/A (managed in task_003) |
| **Guardrails Config**    | N/A (managed in task_003) |
| **Model Provider**       | N/A (managed in task_003) |

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

Implement the no-show risk calculation engine and its supporting infrastructure in the ASP.NET Core .NET 9 backend (Risk/Appointment module).

**Key deliverables:**

1. **`RuleBasedNoShowRiskCalculator`** — pure .NET class computing a weighted score from five behavioral factors (prior no-show history, booking lead time, appointment type, intake completion, reminder engagement). Returns `RiskScoreResult { Score, Severity, Factors }`.

2. **`NoShowRiskCalculationBackgroundService`** (inherits `BackgroundService`) — periodic job running every hour, querying upcoming appointments (`status = Booked`, `date > UtcNow`), calling the risk calculator for each, and upserting `NoShowRisk` records.

3. **`CalculateNoShowRiskCommandHandler`** — MediatR command handler called both by the background service (batch) and immediately after a new booking is created. Handles UPSERT on `NoShowRisk`.

4. **`GET /api/staff/appointments`** — Staff-only endpoint returning appointments with embedded `noShowRisk` DTO; accepts `date` query parameter.

5. **DB addendum**: `no_show_risks` table needs a `severity VARCHAR(10) NOT NULL DEFAULT 'Medium'` column. If US_006 foundational migration did not include `severity`, this task adds an `AddSeverityToNoShowRisks` EF Core migration.

---

## Dependent Tasks

- **EP-006/us_031 task_003_ai_risk_augmentation** — `IAiNoShowRiskAugmenter` interface and `AiNoShowRiskUnavailableException` must be defined; this task calls the interface and catches the exception for AC-3 degraded mode
- **US_006 (Foundational)** — `NoShowRisk` entity with `id`, `appointmentId`, `score`, `factors` (JSONB), `calculatedAt` must exist; `severity` column may need migration addendum
- **US_011 (EP-001)** — JWT middleware active; `[Authorize(Roles="Staff")]` on appointment endpoint

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `RuleBasedNoShowRiskCalculator` | `Server/Modules/Risk/Services/RuleBasedNoShowRiskCalculator.cs` |
| CREATE | `INoShowRiskCalculator` interface | `Server/Modules/Risk/Interfaces/INoShowRiskCalculator.cs` |
| CREATE | `IAiNoShowRiskAugmenter` interface | `Server/Modules/Risk/Interfaces/IAiNoShowRiskAugmenter.cs` (stub; implemented in task_003) |
| CREATE | `NoShowRiskCalculationBackgroundService` | `Server/Modules/Risk/BackgroundServices/NoShowRiskCalculationBackgroundService.cs` |
| CREATE | `CalculateNoShowRiskCommand` + `CalculateNoShowRiskCommandHandler` | `Server/Modules/Risk/Commands/` |
| CREATE | `GetStaffAppointmentsQuery` + `GetStaffAppointmentsQueryHandler` | `Server/Modules/Risk/Queries/` |
| CREATE | `StaffAppointmentsController` | `Server/Modules/Staff/StaffAppointmentsController.cs` (or extend existing) |
| CREATE | `RiskScoreResult` record | `Server/Modules/Risk/Models/RiskScoreResult.cs` |
| CREATE | `StaffAppointmentDto` + `NoShowRiskDto` | `Server/Modules/Risk/Dtos/` |
| CREATE | EF Core migration `AddSeverityToNoShowRisks` (if `severity` column absent from US_006) | `Server/Infrastructure/Migrations/` |
| MODIFY | `Program.cs` | Register `INoShowRiskCalculator`, `IAiNoShowRiskAugmenter`, `NoShowRiskCalculationBackgroundService`, MediatR handlers |

---

## Implementation Plan

1. **Factor scoring model** (`RuleBasedNoShowRiskCalculator`):

   | Factor | Data Source | Weight | Rules |
   |--------|-------------|--------|-------|
   | Prior no-show history | `appointments WHERE patientId = @p AND status = 'NoShow'` | 0.35 | 0 prior = 0.0; 1 = 0.5; 2+ = 1.0; absent history = neutral 0.5 |
   | Booking lead time | `appointment.date - UtcNow.Date` (days) | 0.25 | >14 days = 0.2 (low risk); 7–14 = 0.5; 3–6 = 0.7; <3 = 1.0 |
   | Appointment type | `specialty.type` (e.g., Routine / Specialist / Emergency) | 0.15 | Routine = 0.5; Specialist = 0.3; Emergency = 0.1; unknown = 0.5 |
   | Intake completion | `intakeRecords WHERE appointmentId = @a` | 0.15 | Completed = 0.0; Not completed = 0.8; No record = 0.5 |
   | Reminder engagement | `notifications WHERE appointmentId = @a AND deliveredAt IS NOT NULL` | 0.10 | Any delivery confirmed = 0.2; No reminders sent = 0.5; Sent but not delivered = 0.8 |

   - `Score = Σ(factorScore × weight)` — result clamped to `[0.0, 1.0]`
   - `Severity`: score < 0.35 → `"Low"`; 0.35–0.70 → `"Medium"`; > 0.70 → `"High"`
   - Missing data default: any factor unavailable → use neutral value (0.5); add `{ "name": "DataAvailability", "note": "Insufficient data for full assessment", "contribution": 0 }` to JSONB factors list

2. **AI augmentation integration** (optional delta):
   - Call `await _aiAugmenter.GetAugmentationDeltaAsync(patientId, appointmentId, baseScore)` — returns `double delta` in range `[-0.15, +0.15]`
   - `finalScore = Math.Clamp(baseScore + delta, 0.0, 1.0)`
   - Catch `AiNoShowRiskUnavailableException` (circuit open) → `delta = 0.0`; log `Serilog.Warning("NoShowRisk_AiDegraded {@AppointmentId}", appointmentId)` (AC-3)

3. **`CalculateNoShowRiskCommandHandler`** UPSERT logic:
   - Query `NoShowRisk WHERE appointmentId = @a` — if exists, UPDATE `score`, `severity`, `factors`, `calculatedAt`; if not, INSERT new record
   - Uses `SaveChangesAsync()` — single transaction
   - AuditLog: `action = "NoShowRiskCalculated"`, `entityType = "NoShowRisk"`, `details = { score, severity, degradedMode }`

4. **`NoShowRiskCalculationBackgroundService`**:
   - Runs with a 1-hour periodic timer (`PeriodicTimer` .NET 8+ API — preferred over `Task.Delay`)
   - On each tick: query `appointments WHERE status = 'Booked' AND date >= CURRENT_DATE` (using a dedicated `IServiceScope` — `BackgroundService` cannot inject scoped EF Core `DbContext` directly)
   - For each appointment: dispatch `CalculateNoShowRiskCommand` via `IMediator` from the scoped service provider
   - Log `Serilog.Information("NoShowRisk_BatchCompleted {@Count} appointments processed", count)`
   - Also triggered immediately after a new booking is created (hook into `CreateBookingCommandHandler` post-save — fire-and-forget `Task.Run` or via a domain event queue)

5. **`GetStaffAppointmentsQueryHandler`**:
   - Query: `appointments JOIN noShowRisk (left join — nullable) WHERE date = @d ORDER BY timeSlotStart`
   - `patientId` for display name fetched via `patients.name` join
   - Returns `IReadOnlyList<StaffAppointmentDto>`

6. **`StaffAppointmentsController`**:
   - `[HttpGet]` `[Authorize(Roles = "Staff")]` — accepts `date` query param (required, validates `DateOnly.TryParse`)
   - Returns `IReadOnlyList<StaffAppointmentDto>` — each with embedded `NoShowRiskDto` or `null`
   - `NoShowRiskDto`: `{ Score, Severity, Factors, CalculatedAt }`

7. **DB migration `AddSeverityToNoShowRisks`** (only if US_006 migration omitted `severity`):
   ```sql
   -- Up()
   ALTER TABLE no_show_risks ADD COLUMN severity VARCHAR(10) NOT NULL DEFAULT 'Medium';
   -- Down()
   ALTER TABLE no_show_risks DROP COLUMN severity;
   ```
   - EF Core entity: add `public string Severity { get; set; } = "Medium";` to `NoShowRisk`
   - `NoShowRiskConfiguration`: `builder.Property(r => r.Severity).HasMaxLength(10).IsRequired()`

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no Server/ scaffold yet — greenfield ASP.NET Core project)
```

> Update with actual `Server/Modules/Risk/` tree after scaffold is complete.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Modules/Risk/Interfaces/INoShowRiskCalculator.cs` | `CalculateAsync(appointmentId): Task<RiskScoreResult>` |
| CREATE | `Server/Modules/Risk/Interfaces/IAiNoShowRiskAugmenter.cs` | `GetAugmentationDeltaAsync(patientId, appointmentId, baseScore): Task<double>` — stub implemented in task_003 |
| CREATE | `Server/Modules/Risk/Services/RuleBasedNoShowRiskCalculator.cs` | Five-factor weighted rule engine; neutral defaults; missing data handling |
| CREATE | `Server/Modules/Risk/Models/RiskScoreResult.cs` | `{ Score, Severity, Factors: List<RiskFactor>, DegradedMode }` |
| CREATE | `Server/Modules/Risk/BackgroundServices/NoShowRiskCalculationBackgroundService.cs` | `BackgroundService`; 1-hour `PeriodicTimer`; scoped service provider for EF Core; dispatches `CalculateNoShowRiskCommand` |
| CREATE | `Server/Modules/Risk/Commands/CalculateNoShowRiskCommand.cs` | MediatR command + handler: compute score, UPSERT `NoShowRisk`, AuditLog |
| CREATE | `Server/Modules/Risk/Queries/GetStaffAppointmentsQuery.cs` | MediatR query + handler: LEFT JOIN appointments with noShowRisk for a given date |
| CREATE | `Server/Modules/Staff/StaffAppointmentsController.cs` | `GET /api/staff/appointments?date=` with `[Authorize(Roles="Staff")]` |
| CREATE | `Server/Modules/Risk/Dtos/StaffAppointmentDto.cs` | Appointment + embedded `NoShowRiskDto?` |
| CREATE | `Server/Infrastructure/Migrations/<timestamp>_AddSeverityToNoShowRisks.cs` | `Up()`: ADD COLUMN severity; `Down()`: DROP COLUMN |
| MODIFY | `Server/Program.cs` | Register `INoShowRiskCalculator → RuleBasedNoShowRiskCalculator`; register `NoShowRiskCalculationBackgroundService`; register MediatR handlers |

---

## External References

- [.NET 9 BackgroundService + PeriodicTimer](https://learn.microsoft.com/en-us/dotnet/core/extensions/timer-service)
- [.NET BackgroundService — scoped services in IHostedService (IServiceScopeFactory)](https://learn.microsoft.com/en-us/dotnet/core/extensions/scoped-service)
- [EF Core 9 — JSONB mapping with Npgsql (List<T> column)](https://www.npgsql.org/efcore/mapping/json.html)
- [EF Core 9 — UPSERT via ExecuteUpdateAsync / AddOrUpdate](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete)
- [MediatR 12.x — IRequest/IRequestHandler](https://github.com/jbogard/MediatR/wiki)
- [Serilog 4.x — structured logging](https://serilog.net/)
- [OWASP A01 — Broken Access Control: `[Authorize(Roles="Staff")]` on all endpoints](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)
- [DR-018 — NoShowRisk storage requirement (design.md line 82)](design.md)
- [AIR-007 — Rule-based + AI augmentation (design.md line 155)](design.md)

---

## Build Commands

```bash
# Restore packages
dotnet restore

# Build
dotnet build

# Add migration (if severity column needed)
dotnet ef migrations add AddSeverityToNoShowRisks --project Server/Server.csproj --output-dir Infrastructure/Migrations

# Apply migration
dotnet ef database update --project Server/Server.csproj

# Run unit tests
dotnet test
```

---

## Implementation Validation Strategy

- [ ] `RuleBasedNoShowRiskCalculator` with patient having 2 prior no-shows, 2-day lead time → `severity = "High"`; factors JSONB contains all five entries
- [ ] `RuleBasedNoShowRiskCalculator` with no prior history → `priorNoShowHistory` factor note = "neutral (no history)"; score defaults appropriately
- [ ] Missing intake record: `DataAvailability` factor entry present in JSONB; score = 0.5 (Medium)
- [ ] `CalculateNoShowRiskCommandHandler` with no existing `NoShowRisk` for an appointment → INSERT (new record)
- [ ] `CalculateNoShowRiskCommandHandler` with existing `NoShowRisk` for an appointment → UPDATE `score`, `severity`, `calculatedAt` (AC-4)
- [ ] `IAiNoShowRiskAugmenter` throws `AiNoShowRiskUnavailableException` → `delta = 0.0`; Serilog warning logged with `NoShowRisk_AiDegraded` (AC-3)
- [ ] `GET /api/staff/appointments?date=2026-04-20` with Staff JWT → returns appointments with `noShowRisk` embedded
- [ ] `GET /api/staff/appointments` with Patient JWT → HTTP 403
- [ ] `BackgroundService` runs once on start; re-runs after 1 hour (unit-test with injected `IServiceScopeFactory` mock)

---

## Implementation Checklist

- [ ] Create `RuleBasedNoShowRiskCalculator`: five-factor weighted scoring (prior no-show 0.35, lead time 0.25, appt type 0.15, intake 0.15, reminder 0.10); neutral defaults for missing data; JSONB factor list; severity threshold Low/Medium/High
- [ ] Create `CalculateNoShowRiskCommandHandler`: call `INoShowRiskCalculator.CalculateAsync()`, call `IAiNoShowRiskAugmenter.GetAugmentationDeltaAsync()` (catch `AiNoShowRiskUnavailableException` → delta=0, log degraded); UPSERT `NoShowRisk`; AuditLog
- [ ] Create `NoShowRiskCalculationBackgroundService`: `PeriodicTimer` 1-hour interval; `IServiceScopeFactory` scoped EF access; dispatch `CalculateNoShowRiskCommand` per upcoming booked appointment; log batch completion count
- [ ] Create `GetStaffAppointmentsQueryHandler`: LEFT JOIN appointments → noShowRisk for given `date`; return `List<StaffAppointmentDto>` with embedded `NoShowRiskDto?`
- [ ] Create `StaffAppointmentsController`: `GET /api/staff/appointments?date=` with `[Authorize(Roles="Staff")]`; validate `date` param (reject invalid formats with 400)
- [ ] Create EF Core migration `AddSeverityToNoShowRisks` if `severity` column absent from US_006; update `NoShowRisk` entity and `NoShowRiskConfiguration`
- [ ] Register `INoShowRiskCalculator → RuleBasedNoShowRiskCalculator` (scoped), `IAiNoShowRiskAugmenter` stub (or task_003 impl), `NoShowRiskCalculationBackgroundService` (hosted service) in `Program.cs`
- [ ] Hook `CalculateNoShowRiskCommand` dispatch into `CreateBookingCommandHandler` post-`SaveChangesAsync` (fire-and-forget via `IServiceScopeFactory`) so new bookings get a risk score immediately
