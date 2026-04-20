# Task - TASK_003

## Requirement Reference

- **User Story**: US_033 — Automated Multi-Channel Reminders with Configurable Intervals
- **Story Location**: `.propel/context/tasks/EP-006/us_033/us_033.md`
- **Acceptance Criteria**:
  - AC-3: Given a Staff or Admin user changes the default reminder intervals in system settings, when the new intervals are saved, then future reminder schedules are recalculated using the new intervals; already-queued reminders are updated if the appointment time has not passed.

## Design References (Frontend Tasks Only)

| Reference Type         | Value |
|------------------------|-------|
| **UI Impact**          | No    |
| **Figma URL**          | N/A   |
| **Wireframe Status**   | N/A   |
| **Wireframe Type**     | N/A   |
| **Wireframe Path/URL** | N/A   |
| **Screen Spec**        | N/A   |
| **UXR Requirements**   | N/A   |
| **Design Tokens**      | N/A   |

## Applicable Technology Stack

| Layer      | Technology                  | Version |
|------------|-----------------------------|---------|
| Backend    | ASP.NET Core Web API        | .net 10  |
| Messaging  | MediatR                     | 12.x    |
| Validation | FluentValidation            | 11.x    |
| ORM        | Entity Framework Core       | 9.x     |
| Database   | PostgreSQL                  | 16+     |
| Auth       | JWT + RBAC                  | —       |
| Logging    | Serilog                     | 4.x     |
| AI/ML      | N/A                         | N/A     |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type       | Value |
|----------------------|-------|
| **AI Impact**        | No    |
| **AIR Requirements** | N/A   |
| **AI Pattern**       | N/A   |
| **Prompt Template**  | N/A   |
| **Guardrails Config**| N/A   |
| **Model Provider**   | N/A   |

## Mobile References (Mobile Tasks Only)

| Reference Type      | Value |
|---------------------|-------|
| **Mobile Impact**   | No    |
| **Platform Target** | N/A   |
| **Min OS Version**  | N/A   |
| **Mobile Framework**| N/A   |

## Task Overview

Implement the backend REST API endpoints that allow Staff and Admin users to retrieve and update reminder interval configuration. When intervals are updated, the handler recalculates `scheduledAt` for all Pending `Notification` records whose associated appointment has not yet passed, ensuring AC-3 compliance. The implementation follows MediatR CQRS pattern with FluentValidation and RBAC enforcement (Staff/Admin roles only) per NFR-006.

## Dependent Tasks

- **task_005_db_reminder_schema_migration.md** — `SystemSettings` table must exist with interval data.

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `GetReminderSettingsQuery` / Handler | `PropelIQ.Notification` | CREATE |
| `UpdateReminderIntervalsCommand` / Handler | `PropelIQ.Notification` | CREATE |
| `ReminderSettingsController` | `PropelIQ.Api` | CREATE |
| `UpdateReminderIntervalsValidator` | `PropelIQ.Notification` | CREATE |
| `ISystemSettingsRepository` | `PropelIQ.Shared` | CREATE |
| `SystemSettingsRepository` | `PropelIQ.Infrastructure` | CREATE |
| `INotificationRepository` | `PropelIQ.Notification` | MODIFY (add `UpdateScheduledAtBatchAsync`) |

## Implementation Plan

1. **Create `ISystemSettingsRepository`** with `GetReminderIntervalsAsync()` returning `int[]` and `SetReminderIntervalsAsync(int[] intervals)` updating the `SystemSettings` key-value records.
2. **Create `GetReminderSettingsQuery`** (MediatR IRequest) and its handler — reads current intervals from `ISystemSettingsRepository` and returns a `ReminderSettingsDto { int[] IntervalHours }`.
3. **Create `UpdateReminderIntervalsCommand`** with property `int[] IntervalHours` and MediatR handler. Handler logic:
   - Validate intervals via `UpdateReminderIntervalsValidator`.
   - Persist new intervals to `SystemSettings`.
   - Query all `Notification` records with `status=Pending AND appointment.appointmentStart > UtcNow`.
   - For each record, recalculate new `scheduledAt = appointmentStart - newInterval`. If no matching interval exists for the old `templateType`, delete the Notification record. Create new Notification records for any new intervals not yet represented.
   - Log the settings change to AuditLog with before/after values (NFR-009).
4. **Create `UpdateReminderIntervalsValidator`** (FluentValidation) — enforce: each value > 0 (positive hours), no duplicate values, maximum 10 intervals, values ≤ 168 hours (7 days max lead time).
5. **Create `ReminderSettingsController`** with:
   - `GET /api/settings/reminders` — returns current intervals (RBAC: Staff + Admin).
   - `PUT /api/settings/reminders` — accepts `{ intervalHours: int[] }`, triggers `UpdateReminderIntervalsCommand` (RBAC: Staff + Admin).
6. **RBAC enforcement** — apply `[Authorize(Roles = "Staff,Admin")]` attribute. Patient role receives HTTP 403.
7. **Idempotent update** — if the same intervals are submitted, no Notification records are changed; return 200 with current state.
8. **Return 400 with structured validation errors** for invalid input (FluentValidation pipeline middleware integration).

### Pseudocode

```csharp
// UpdateReminderIntervalsCommandHandler.cs
public async Task<Unit> Handle(UpdateReminderIntervalsCommand request, CancellationToken ct)
{
    // 1. Persist new intervals
    var previousIntervals = await _settingsRepo.GetReminderIntervalsAsync(ct);
    await _settingsRepo.SetReminderIntervalsAsync(request.IntervalHours, ct);

    // 2. Recalculate Pending Notifications for future appointments
    var pendingNotifs = await _notifRepo.GetPendingForFutureAppointmentsAsync(ct);

    foreach (var notif in pendingNotifs)
    {
        var apptStart = notif.Appointment.AppointmentStart;
        var newScheduledAt = apptStart.AddHours(-ExtractIntervalFromTemplateType(notif.TemplateType));

        // If interval no longer configured, remove the notification
        if (!request.IntervalHours.Contains(ExtractHours(notif.TemplateType)))
            await _notifRepo.DeleteAsync(notif.Id, ct);
        else
            notif.ScheduledAt = newScheduledAt;
    }

    // 3. Add Notification records for newly added intervals
    var newIntervals = request.IntervalHours.Except(previousIntervals);
    foreach (var newHour in newIntervals)
    {
        var futureAppts = await _apptRepo.GetBookedFutureAppointmentsAsync(ct);
        foreach (var appt in futureAppts)
        {
            await _notifRepo.CreateAsync(BuildNotification(appt, newHour), ct);
        }
    }

    // 4. Audit log
    await _auditLog.LogAsync(request.RequestedByUserId,
        "ReminderIntervalsUpdated",
        $"Before: [{string.Join(",", previousIntervals)}] After: [{string.Join(",", request.IntervalHours)}]",
        ct);

    return Unit.Value;
}
```

## Current Project State

```
Server/
├── PropelIQ.Api/
│   └── Controllers/
│       └── (no settings controllers yet)
├── PropelIQ.Notification/
│   ├── Queries/
│   │   └── (empty)
│   └── Commands/
│       └── (empty)
└── PropelIQ.Infrastructure/
    └── Repositories/
        └── (SystemSettings not yet implemented)
```

> Placeholder — update with actual paths as dependent tasks complete.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/PropelIQ.Notification/Queries/GetReminderSettingsQuery.cs` | MediatR query + handler |
| CREATE | `Server/PropelIQ.Notification/Commands/UpdateReminderIntervalsCommand.cs` | MediatR command + handler |
| CREATE | `Server/PropelIQ.Notification/Validators/UpdateReminderIntervalsValidator.cs` | FluentValidation rules |
| CREATE | `Server/PropelIQ.Shared/Settings/ISystemSettingsRepository.cs` | Interface for SystemSettings CRUD |
| CREATE | `Server/PropelIQ.Infrastructure/Repositories/SystemSettingsRepository.cs` | EF Core implementation |
| CREATE | `Server/PropelIQ.Api/Controllers/ReminderSettingsController.cs` | REST endpoints GET + PUT |
| CREATE | `Server/PropelIQ.Notification/DTOs/ReminderSettingsDto.cs` | Response DTO |
| MODIFY | `Server/PropelIQ.Notification/Repositories/INotificationRepository.cs` | Add `GetPendingForFutureAppointmentsAsync`, `DeleteAsync` |

## External References

- [MediatR CQRS pattern in .net 10](https://github.com/jbogard/MediatR/wiki)
- [FluentValidation — ASP.NET Core integration](https://docs.fluentvalidation.net/en/latest/aspnet.html)
- [ASP.NET Core RBAC authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-9.0)
- [NFR-006 — Role-based access control at API level](../docs/design.md)

## Build Commands

```bash
cd Server
dotnet restore
dotnet build PropelIQ.sln

# Verify Swagger output after endpoint creation
dotnet run --project PropelIQ.Api
# Navigate to /swagger to verify GET /api/settings/reminders and PUT /api/settings/reminders
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] `GET /api/settings/reminders` returns HTTP 200 with `{ intervalHours: [48, 24, 2] }` for Staff and Admin roles
- [ ] `GET /api/settings/reminders` returns HTTP 403 for Patient role
- [ ] `PUT /api/settings/reminders` with valid body updates SystemSettings and returns HTTP 200
- [ ] `PUT /api/settings/reminders` with duplicate interval values returns HTTP 400 with validation error
- [ ] `PUT /api/settings/reminders` with negative values returns HTTP 400
- [ ] After successful PUT, Pending Notification records for future appointments have updated `scheduledAt`
- [ ] AuditLog contains before/after interval values after successful PUT

## Implementation Checklist

- [ ] Create `ISystemSettingsRepository` with `GetReminderIntervalsAsync` and `SetReminderIntervalsAsync` methods
- [ ] Implement `SystemSettingsRepository` using EF Core against `SystemSettings` table (key-value pattern)
- [ ] Create `GetReminderSettingsQuery` MediatR query and handler returning `ReminderSettingsDto`
- [ ] Create `UpdateReminderIntervalsCommand` MediatR command and handler with full recalculation logic
- [ ] Create `UpdateReminderIntervalsValidator` — validate positive integers, no duplicates, max 10, ≤ 168h
- [ ] Create `ReminderSettingsController` with `GET /api/settings/reminders` and `PUT /api/settings/reminders`
- [ ] Apply `[Authorize(Roles = "Staff,Admin")]` to controller — Patient role returns 403 (NFR-006)
- [ ] Write AuditLog entry with before/after interval values on successful update (NFR-009)
