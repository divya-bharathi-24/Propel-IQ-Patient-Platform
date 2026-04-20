# Task - TASK_002

## Requirement Reference

- **User Story**: US_032 — High-Risk Appointment Flag with Recommended Interventions
- **Story Location**: `.propel/context/tasks/EP-006/us_032/us_032.md`
- **Acceptance Criteria**:
  - AC-2: Given a High-risk flag is displayed, When I explicitly click "Accept" on a recommended intervention, Then the intervention is marked as accepted with my staff ID and timestamp, and the relevant action (e.g., ad-hoc reminder) is triggered.
  - AC-3: Given a High-risk flag is displayed, When I click "Dismiss", Then the flag is acknowledged with my staff ID and a dismissal reason (optional), and the flag no longer appears as a pending action for that appointment.
  - AC-4: Given a High-risk appointment flag is unacknowledged, When I view the Staff dashboard, Then unacknowledged High-risk flags are prominently surfaced in a "Requires Attention" section sorted by appointment time.
- **Edge Cases**:
  - Score drops to Medium before acknowledgment: `NoShowRiskAssessedEvent` handler auto-clears Pending interventions by setting `status = AutoCleared`; audit history retained.
  - Flag persists when staff is offline: `RiskIntervention` rows remain `Pending` until any Staff member acknowledges on next login — no TTL on pending rows.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | No |
| **Figma URL** | N/A |
| **Wireframe Status** | N/A |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | N/A |
| **Screen Spec** | N/A |
| **UXR Requirements** | N/A |
| **Design Tokens** | N/A |

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Backend | ASP.NET Core Web API | .NET 9 |
| Mediator | MediatR | 12.x |
| Validation | FluentValidation | 11.x |
| ORM | Entity Framework Core | 9.x |
| Database | PostgreSQL | 16+ |
| AI/ML | N/A | N/A |
| Vector Store | N/A | N/A |
| AI Gateway | N/A | N/A |
| Mobile | N/A | N/A |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement the `RiskFlagController` and supporting MediatR feature layer for high-risk appointment intervention management. Three read/write endpoints plus one event-driven handler:

**`GET /api/risk/requires-attention`** — `GetRequiresAttentionQuery` (MediatR); resolves all upcoming appointments (date ≥ today UTC) that have a `NoShowRisk.score > 0.66` AND at least one `RiskIntervention.status = Pending`; `AsNoTracking()` projection; ordered by `appointment.date ASC, appointment.timeSlotStart ASC`; returns `RequiresAttentionItemDto[]` with `appointmentId`, `patientName`, `appointmentTime`, `riskScore`, `pendingInterventionCount`.

**`GET /api/risk/{appointmentId}/interventions`** — `GetInterventionsByAppointmentQuery`; returns all `RiskIntervention` rows for the appointment (any status — for history display); `AsNoTracking()`.

**`PATCH /api/risk/interventions/{interventionId}/accept`** — `AcceptInterventionCommand`; sets `status = Accepted`, `staffId` from JWT `NameIdentifier` claim, `acknowledgedAt = DateTime.UtcNow`; publishes `InterventionAcceptedNotification` (MediatR `INotification`) to trigger the relevant action (e.g., ad-hoc reminder via `INotificationService`); writes audit log `InterventionAccepted` (FR-030); returns `204`.

**`PATCH /api/risk/interventions/{interventionId}/dismiss`** — `DismissInterventionCommand`; sets `status = Dismissed`, `staffId` from JWT, `acknowledgedAt = DateTime.UtcNow`, `dismissalReason` (optional, max 500 chars from request body); writes audit log `InterventionDismissed`; returns `204`.

**`NoShowRiskAssessedEventHandler`** (MediatR `INotificationHandler`) — triggered by the US_031 scoring engine after each `NoShowRisk` UPSERT. Handler logic:
- If `event.Severity == High` (score > 0.66): INSERT two `RiskIntervention` rows: `AdditionalReminder` and `CallbackRequest`, both with `status = Pending`. Only inserts if no `Pending` rows already exist for this appointment (idempotent guard — prevents duplicate rows on re-scoring).
- If `event.Severity != High` (score dropped to Medium/Low): UPDATE any existing `Pending` interventions for the appointment to `status = AutoCleared` (edge case auto-clear).

`staffId` is always sourced from JWT `NameIdentifier` claim — never from request body or URL (OWASP A01). All endpoints carry `[Authorize(Roles = "Staff,Admin")]`.

## Dependent Tasks

- **US_032 / TASK_003** — `risk_interventions` table and `RiskIntervention` entity must exist before this BE feature can be implemented.
- **US_031 (EP-006)** — `NoShowRiskAssessedEvent` MediatR notification must be published by the US_031 scoring engine with `AppointmentId`, `Score`, and `Severity`; `TASK_002` implements the handler.
- **US_007 (EP-DATA)** — `NoShowRisk` entity and `no_show_risks` table must exist.
- **US_013 / TASK_001** — `IAuditLogRepository` write-only pattern must be in place.

## Impacted Components

| Component | Status | Location |
|-----------|--------|----------|
| `RiskFlagController` | NEW | `Server/Controllers/RiskFlagController.cs` |
| `GetRequiresAttentionQuery` + `GetRequiresAttentionQueryHandler` | NEW | `Server/Features/RiskFlag/GetRequiresAttention/` |
| `RequiresAttentionItemDto` | NEW | `Server/Features/RiskFlag/GetRequiresAttention/RequiresAttentionItemDto.cs` |
| `GetInterventionsByAppointmentQuery` + `GetInterventionsByAppointmentQueryHandler` | NEW | `Server/Features/RiskFlag/GetInterventions/` |
| `RiskInterventionDto` | NEW | `Server/Features/RiskFlag/GetInterventions/RiskInterventionDto.cs` |
| `AcceptInterventionCommand` + `AcceptInterventionCommandHandler` | NEW | `Server/Features/RiskFlag/AcceptIntervention/` |
| `DismissInterventionCommand` + `DismissInterventionCommandValidator` + `DismissInterventionCommandHandler` | NEW | `Server/Features/RiskFlag/DismissIntervention/` |
| `NoShowRiskAssessedEvent` + `NoShowRiskAssessedEventHandler` | NEW | `Server/Features/RiskFlag/NoShowRiskAssessedEvent/` |
| `InterventionAcceptedNotification` + handler stub | NEW | `Server/Features/RiskFlag/InterventionAcceptedNotification/` |

## Implementation Plan

1. **Enums and shared types** (in `RiskFlag` feature folder):

   ```csharp
   public enum InterventionType   { AdditionalReminder, CallbackRequest }
   public enum InterventionStatus { Pending, Accepted, Dismissed, AutoCleared }
   ```

2. **`GetRequiresAttentionQueryHandler`** — CQRS read model (AD-2):

   ```csharp
   var today = DateOnly.FromDateTime(DateTime.UtcNow);

   var results = await _dbContext.NoShowRisks
       .AsNoTracking()
       .Where(r => r.Score > 0.66m
           && r.Appointment.Date >= today
           && r.RiskInterventions.Any(i => i.Status == InterventionStatus.Pending))
       .OrderBy(r => r.Appointment.Date)
       .ThenBy(r => r.Appointment.TimeSlotStart)
       .Select(r => new RequiresAttentionItemDto(
           r.AppointmentId,
           r.Appointment.Patient.Name,
           r.Appointment.Date,
           r.Appointment.TimeSlotStart,
           r.Score,
           r.RiskInterventions.Count(i => i.Status == InterventionStatus.Pending)
       ))
       .ToListAsync(cancellationToken);
   ```

3. **`AcceptInterventionCommandHandler.Handle()`**:

   ```csharp
   // Resolve staffId from JWT — OWASP A01
   var staffId = Guid.Parse(_httpContextAccessor.HttpContext!.User
       .FindFirstValue(ClaimTypes.NameIdentifier)!);

   var intervention = await _dbContext.RiskInterventions
       .FirstOrDefaultAsync(i => i.Id == request.InterventionId, cancellationToken)
       ?? throw new NotFoundException(nameof(RiskIntervention), request.InterventionId);

   if (intervention.Status != InterventionStatus.Pending)
       throw new BusinessRuleException("Intervention has already been acknowledged.");

   intervention.Status = InterventionStatus.Accepted;
   intervention.StaffId = staffId;
   intervention.AcknowledgedAt = DateTime.UtcNow;

   await _dbContext.SaveChangesAsync(cancellationToken);

   // Trigger the relevant action (e.g., ad-hoc reminder via INotification)
   await _mediator.Publish(new InterventionAcceptedNotification(
       intervention.AppointmentId,
       intervention.Type,
       staffId
   ), cancellationToken);

   await _auditLogRepository.WriteAsync(new AuditLogEntry
   {
       UserId = staffId,
       Action = "InterventionAccepted",
       EntityType = "RiskIntervention",
       EntityId = intervention.Id,
       IpAddress = _httpContextAccessor.HttpContext!.Connection.RemoteIpAddress?.ToString()
   });
   ```

4. **`DismissInterventionCommandValidator`** (FluentValidation):

   ```csharp
   RuleFor(x => x.InterventionId).NotEmpty();
   RuleFor(x => x.DismissalReason).MaximumLength(500)
       .When(x => x.DismissalReason is not null);
   ```

5. **`NoShowRiskAssessedEventHandler.Handle()`** — idempotent intervention generation:

   ```csharp
   if (notification.Score > 0.66m)   // High severity
   {
       // Idempotent: only insert if no Pending interventions already exist
       var hasPending = await _dbContext.RiskInterventions.AnyAsync(
           i => i.AppointmentId == notification.AppointmentId
             && i.Status == InterventionStatus.Pending,
           cancellationToken);

       if (!hasPending)
       {
           _dbContext.RiskInterventions.AddRange(
               new RiskIntervention { AppointmentId = notification.AppointmentId, NoShowRiskId = notification.NoShowRiskId,
                   Type = InterventionType.AdditionalReminder, Status = InterventionStatus.Pending },
               new RiskIntervention { AppointmentId = notification.AppointmentId, NoShowRiskId = notification.NoShowRiskId,
                   Type = InterventionType.CallbackRequest, Status = InterventionStatus.Pending }
           );
           await _dbContext.SaveChangesAsync(cancellationToken);
       }
   }
   else   // Score dropped to Medium/Low — auto-clear (edge case)
   {
       var pendingInterventions = await _dbContext.RiskInterventions
           .Where(i => i.AppointmentId == notification.AppointmentId
                    && i.Status == InterventionStatus.Pending)
           .ToListAsync(cancellationToken);

       pendingInterventions.ForEach(i => i.Status = InterventionStatus.AutoCleared);
       if (pendingInterventions.Count > 0)
           await _dbContext.SaveChangesAsync(cancellationToken);
   }
   ```

   > **AG-6 Compliance**: `NoShowRiskAssessedEventHandler` MUST NOT throw — wrap the entire handler body in try/catch, log Warning on failure, and return to avoid disrupting the primary risk-scoring transaction.

6. **`RiskFlagController`**:

   ```csharp
   [ApiController]
   [Route("api/risk")]
   [Authorize(Roles = "Staff,Admin")]
   public class RiskFlagController : ControllerBase
   {
       [HttpGet("requires-attention")]
       public async Task<IActionResult> GetRequiresAttention(ISender mediator)
           => Ok(await mediator.Send(new GetRequiresAttentionQuery()));

       [HttpGet("{appointmentId:guid}/interventions")]
       public async Task<IActionResult> GetInterventions(Guid appointmentId, ISender mediator)
           => Ok(await mediator.Send(new GetInterventionsByAppointmentQuery(appointmentId)));

       [HttpPatch("interventions/{interventionId:guid}/accept")]
       public async Task<IActionResult> Accept(Guid interventionId, ISender mediator)
       {
           await mediator.Send(new AcceptInterventionCommand(interventionId));
           return NoContent();
       }

       [HttpPatch("interventions/{interventionId:guid}/dismiss")]
       public async Task<IActionResult> Dismiss(Guid interventionId,
           [FromBody] DismissInterventionCommand command, ISender mediator)
       {
           await mediator.Send(command with { InterventionId = interventionId });
           return NoContent();
       }
   }
   ```

## Current Project State

```
Server/
├── Controllers/
│   └── RiskFlagController.cs                        ← NEW
├── Features/
│   ├── Auth/                                        (US_011, US_013 — completed)
│   ├── Booking/                                     (US_019 — completed)
│   ├── Queue/                                       (US_027 — completed)
│   └── RiskFlag/                                    ← NEW
│       ├── GetRequiresAttention/
│       │   ├── GetRequiresAttentionQuery.cs
│       │   ├── GetRequiresAttentionQueryHandler.cs
│       │   └── RequiresAttentionItemDto.cs
│       ├── GetInterventions/
│       │   ├── GetInterventionsByAppointmentQuery.cs
│       │   ├── GetInterventionsByAppointmentQueryHandler.cs
│       │   └── RiskInterventionDto.cs
│       ├── AcceptIntervention/
│       │   ├── AcceptInterventionCommand.cs
│       │   └── AcceptInterventionCommandHandler.cs
│       ├── DismissIntervention/
│       │   ├── DismissInterventionCommand.cs
│       │   ├── DismissInterventionCommandValidator.cs
│       │   └── DismissInterventionCommandHandler.cs
│       ├── NoShowRiskAssessedEvent/
│       │   ├── NoShowRiskAssessedEvent.cs            (INotification record published by US_031)
│       │   └── NoShowRiskAssessedEventHandler.cs     ← NEW (handler lives here)
│       └── InterventionAcceptedNotification/
│           ├── InterventionAcceptedNotification.cs
│           └── InterventionAcceptedNotificationHandler.cs  (triggers ad-hoc reminder)
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Controllers/RiskFlagController.cs` | `[Authorize(Roles="Staff,Admin")]` controller: 4 endpoints |
| CREATE | `Server/Features/RiskFlag/GetRequiresAttention/GetRequiresAttentionQueryHandler.cs` | `AsNoTracking()` EF Core query: upcoming appointments, score > 0.66, Pending interventions; ordered by date + time ASC (AD-2) |
| CREATE | `Server/Features/RiskFlag/GetRequiresAttention/RequiresAttentionItemDto.cs` | DTO: `AppointmentId`, `PatientName`, `AppointmentTime`, `RiskScore`, `PendingInterventionCount` |
| CREATE | `Server/Features/RiskFlag/GetInterventions/GetInterventionsByAppointmentQueryHandler.cs` | `AsNoTracking()` load all `RiskInterventions` for appointment by `appointmentId` |
| CREATE | `Server/Features/RiskFlag/AcceptIntervention/AcceptInterventionCommandHandler.cs` | Sets `Accepted` + `staffId` from JWT + `acknowledgedAt`; publishes `InterventionAcceptedNotification`; audit log `InterventionAccepted` |
| CREATE | `Server/Features/RiskFlag/DismissIntervention/DismissInterventionCommandValidator.cs` | FluentValidation: `DismissalReason.MaximumLength(500)` |
| CREATE | `Server/Features/RiskFlag/DismissIntervention/DismissInterventionCommandHandler.cs` | Sets `Dismissed` + `staffId` from JWT + `acknowledgedAt` + `dismissalReason`; audit log `InterventionDismissed` |
| CREATE | `Server/Features/RiskFlag/NoShowRiskAssessedEvent/NoShowRiskAssessedEventHandler.cs` | Idempotent: INSERT 2 intervention rows if score > 0.66 and no Pending rows exist; AUTO-CLEAR Pending rows if score ≤ 0.66; AG-6 try/catch (never throws) |
| CREATE | `Server/Features/RiskFlag/InterventionAcceptedNotification/InterventionAcceptedNotificationHandler.cs` | Stub: triggers ad-hoc reminder via `INotificationService.SendAdHocReminderAsync()` when `Type = AdditionalReminder` |

## External References

- [MediatR — `INotification` / `INotificationHandler`](https://github.com/jbogard/MediatR/wiki)
- [FluentValidation — `MaximumLength` rule](https://docs.fluentvalidation.net/en/latest/built-in-validators.html#maximumlength-validator)
- [EF Core — `AsNoTracking()` read projections](https://learn.microsoft.com/en-us/ef/core/querying/tracking#no-tracking-queries)
- [FR-030 — High-risk flagging, Staff accept/dismiss (spec.md#FR-030)](spec.md#FR-030)
- [OWASP A01:2021 — staffId from JWT claims only](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)

## Build Commands

- Refer to: `.propel/build/backend-build.md`

## Implementation Validation Strategy

- [ ] Unit tests pass: `GetRequiresAttentionQueryHandler` returns only appointments with score > 0.66 AND Pending interventions AND date ≥ today
- [ ] Unit tests pass: `AcceptInterventionCommandHandler` sets `Accepted` status and publishes `InterventionAcceptedNotification`
- [ ] Unit tests pass: `DismissInterventionCommandHandler` sets `Dismissed` with optional `dismissalReason`
- [ ] Unit tests pass: `NoShowRiskAssessedEventHandler` inserts 2 rows on High-risk and does NOT insert if Pending rows already exist (idempotent)
- [ ] Unit tests pass: `NoShowRiskAssessedEventHandler` sets `AutoCleared` when score ≤ 0.66
- [ ] `PATCH /accept` or `/dismiss` with Patient JWT returns `403 Forbidden`
- [ ] `staffId` sourced exclusively from JWT `NameIdentifier` claim in both write handlers (OWASP A01)

## Implementation Checklist

- [ ] `RiskFlagController` with `[Authorize(Roles="Staff,Admin")]`; 4 endpoints: `GET /requires-attention` (200+list), `GET /{appointmentId}/interventions` (200+list), `PATCH /interventions/{id}/accept` (204), `PATCH /interventions/{id}/dismiss` (204); Patient JWT → 403 on all (FR-030 Staff-only restriction)
- [ ] `GetRequiresAttentionQueryHandler`: `AsNoTracking()` EF Core navigation join — `NoShowRisks` WHERE `score > 0.66 AND appointment.date >= today AND ANY pending intervention`; ORDER BY `date ASC, timeSlotStart ASC`; project to `RequiresAttentionItemDto` (AD-2 CQRS read model, AC-4)
- [ ] `AcceptInterventionCommandHandler`: `staffId` from JWT (OWASP A01); guard `Status == Pending` (throws `BusinessRuleException` if already acknowledged); set `Accepted + staffId + acknowledgedAt`; `SaveChangesAsync()`; publish `InterventionAcceptedNotification`; audit log `InterventionAccepted` via `IAuditLogRepository` (AC-2)
- [ ] `DismissInterventionCommandHandler`: same guard pattern; `DismissInterventionCommandValidator` — `DismissalReason.MaximumLength(500)`; set `Dismissed + staffId + acknowledgedAt + dismissalReason`; audit log `InterventionDismissed` (AC-3)
- [ ] `NoShowRiskAssessedEventHandler` (`INotificationHandler`): if High (score > 0.66) AND no existing Pending rows → INSERT `AdditionalReminder` + `CallbackRequest` (idempotent); else if score ≤ 0.66 → UPDATE Pending → `AutoCleared`; entire handler wrapped in try/catch; logs Warning on failure; NEVER throws (AG-6 — event handler safety, edge case auto-clear)
- [ ] `InterventionAcceptedNotificationHandler`: stub — when `Type == AdditionalReminder`, call `INotificationService.SendAdHocReminderAsync(appointmentId, staffId)`; when `Type == CallbackRequest`, log the callback request for Staff follow-up (full reminder integration delivered by EP-006/us_033)
- [ ] `DismissInterventionCommand` validator registered via `AddFluentValidation()` in `Program.cs`; returns `422` with field error on `DismissalReason` exceeding 500 chars
