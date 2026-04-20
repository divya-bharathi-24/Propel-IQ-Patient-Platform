# Task - TASK_001

## Requirement Reference

- User Story: [us_008] (extracted from input)
- Story Location: [.propel/context/tasks/EP-DATA/us_008/us_008.md]
- Acceptance Criteria:
  - **AC-1**: Given the AuditLog entity is configured, When the EF Core context initializes, Then a PostgreSQL trigger is in place that rejects any UPDATE or DELETE against the `audit_logs` table.
  - **AC-2**: Given the Notification entity is persisted, When I store a notification delivery event, Then all required fields (channel, templateType, status, sentAt, retryCount) are stored correctly and queryable with the linked patientId and appointmentId.
  - **AC-3**: Given the CalendarSync entity is configured, When I retrieve a sync record, Then it returns provider, externalEventId, syncStatus, syncedAt, patientId, and appointmentId with FK integrity maintained.
- Edge Case:
  - What happens when an AuditLog row is modified post-insert? A PostgreSQL trigger (task_003) raises an exception before the operation completes. At the C# level, all `AuditLog` properties are declared with `init` accessor, preventing property mutation after construction.
  - What happens if a migration fails midway? Each migration is wrapped in a transaction; partial migrations rollback automatically. (DR-012)

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

## Applicable Technology Stack

| Layer    | Technology           | Version |
| -------- | -------------------- | ------- |
| Backend  | ASP.NET Core Web API | .NET 9  |
| ORM      | Entity Framework Core | 9.x    |
| Database | PostgreSQL           | 16+     |
| Language | C#                   | 13      |
| AI/ML    | N/A                  | N/A     |
| Mobile   | N/A                  | N/A     |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **AI Impact**        | No    |
| **AIR Requirements** | N/A   |
| **AI Pattern**       | N/A   |
| **Prompt Template Path** | N/A |
| **Guardrails Config**| N/A   |
| **Model Provider**   | N/A   |

## Mobile References (Mobile Tasks Only)

| Reference Type      | Value |
| ------------------- | ----- |
| **Mobile Impact**   | No    |
| **Platform Target** | N/A   |
| **Min OS Version**  | N/A   |
| **Mobile Framework**| N/A   |

## Task Overview

Create the C# POCO entity classes and enum types for the four new US_008 entities: `AuditLog`, `Notification`, `InsuranceValidation`, and `CalendarSync`. These classes live in `PropelIQ.Domain/Entities/` and carry no EF Core data annotations — all mapping is deferred to fluent configurations in `PropelIQ.Infrastructure/`.

The `AuditLog` entity is the most critical: per AD-7, it is immutable by design. All properties use the `init` accessor rather than `set` to enforce immutability at the C# level. There are no EF navigation properties on `AuditLog` — only raw `Guid` columns — to enforce the write-only repository pattern and prevent accidental lazy-load or cascade scenarios. The `Details` column is typed as `JsonDocument` (JSONB) matching the design.md specification.

Enum types are defined in `PropelIQ.Domain/Enums/` following the same convention used for US_006 and US_007 entities.

## Dependent Tasks

- US_006 `task_001_be_core_entity_classes.md` — `Patient`, `User`, and `Appointment` entity classes must exist; `AuditLog.UserId` and `Notification.PatientId` reference these

## Impacted Components

| Component | Action | Notes |
| --------- | ------ | ----- |
| `server/src/PropelIQ.Domain/Entities/AuditLog.cs` | CREATE | Immutable POCO — `init`-only properties, `JsonDocument` Details, no navigation properties |
| `server/src/PropelIQ.Domain/Entities/Notification.cs` | CREATE | Mutable POCO — channel, templateType, status, sentAt, retryCount |
| `server/src/PropelIQ.Domain/Entities/InsuranceValidation.cs` | CREATE | Mutable POCO — providerName, insuranceId, validationResult, validatedAt |
| `server/src/PropelIQ.Domain/Entities/CalendarSync.cs` | CREATE | Mutable POCO — provider, externalEventId, syncStatus, syncedAt |
| `server/src/PropelIQ.Domain/Enums/NotificationChannel.cs` | CREATE | SMS, Email, Push |
| `server/src/PropelIQ.Domain/Enums/NotificationStatus.cs` | CREATE | Pending, Sent, Failed, Delivered |
| `server/src/PropelIQ.Domain/Enums/InsuranceValidationResult.cs` | CREATE | Matched, NotMatched, Pending |
| `server/src/PropelIQ.Domain/Enums/CalendarProvider.cs` | CREATE | Google, Apple, Outlook |
| `server/src/PropelIQ.Domain/Enums/CalendarSyncStatus.cs` | CREATE | Synced, Failed, Pending |

## Implementation Plan

1. **Create `AuditLog.cs`** in `PropelIQ.Domain/Entities/` — Properties: `Id` (`Guid`, `init`), `UserId` (`Guid`, `init`), `PatientId` (`Guid?`, `init`), `Action` (`required string`, `init`), `EntityType` (`required string`, `init`), `EntityId` (`Guid`, `init`), `Details` (`JsonDocument?`, `init`), `IpAddress` (`string?`, `init`), `CorrelationId` (`string?`, `init`), `Timestamp` (`DateTime`, `init`). All `init` — no `set` anywhere. No navigation properties. Add `using System.Text.Json;` import.

2. **Create `Notification.cs`** in `PropelIQ.Domain/Entities/` — Properties: `Id` (`Guid`), `PatientId` (`Guid`), `AppointmentId` (`Guid?`), `Channel` (`NotificationChannel`), `TemplateType` (`required string`), `Status` (`NotificationStatus`), `SentAt` (`DateTime?`), `RetryCount` (`int`, default `0`), `ErrorMessage` (`string?`), `CreatedAt` (`DateTime`), `UpdatedAt` (`DateTime`). Navigation: `Patient Patient` (no collection). Note: `AppointmentId` is nullable — notifications may be sent before an appointment exists.

3. **Create `InsuranceValidation.cs`** in `PropelIQ.Domain/Entities/` — Properties: `Id` (`Guid`), `PatientId` (`Guid`), `AppointmentId` (`Guid?`), `ProviderName` (`required string`), `InsuranceId` (`required string`), `ValidationResult` (`InsuranceValidationResult`), `ValidationMessage` (`string?`), `ValidatedAt` (`DateTime?`), `CreatedAt` (`DateTime`). Navigation: `Patient Patient`.

4. **Create `CalendarSync.cs`** in `PropelIQ.Domain/Entities/` — Properties: `Id` (`Guid`), `PatientId` (`Guid`), `AppointmentId` (`Guid`), `Provider` (`CalendarProvider`), `ExternalEventId` (`required string`), `SyncStatus` (`CalendarSyncStatus`), `SyncedAt` (`DateTime?`), `ErrorMessage` (`string?`), `CreatedAt` (`DateTime`), `UpdatedAt` (`DateTime`). Navigation: `Patient Patient`, `Appointment Appointment`.

5. **Create `NotificationChannel.cs`** enum in `PropelIQ.Domain/Enums/` — Values: `Sms`, `Email`, `Push`. (Use `Sms` PascalCase, not `SMS`, per C# naming conventions.)

6. **Create `NotificationStatus.cs`** enum in `PropelIQ.Domain/Enums/` — Values: `Pending`, `Sent`, `Failed`, `Delivered`.

7. **Create `InsuranceValidationResult.cs`** enum in `PropelIQ.Domain/Enums/` — Values: `Matched`, `NotMatched`, `Pending`.

8. **Create `CalendarProvider.cs`** and **`CalendarSyncStatus.cs`** enums in `PropelIQ.Domain/Enums/` — `CalendarProvider`: `Google`, `Apple`, `Outlook`; `CalendarSyncStatus`: `Synced`, `Failed`, `Pending`.

## Current Project State

```
server/src/PropelIQ.Domain/
├── Entities/
│   ├── Patient.cs              # From US_006 task_001
│   ├── User.cs                 # From US_006 task_001
│   ├── Appointment.cs          # From US_006 task_001
│   ├── WaitlistEntry.cs        # From US_006 task_001
│   ├── Specialty.cs            # From US_006 task_001
│   ├── IntakeRecord.cs         # From US_007 task_001
│   ├── ClinicalDocument.cs     # From US_007 task_001
│   ├── ExtractedData.cs        # From US_007 task_001
│   ├── DataConflict.cs         # From US_007 task_001
│   ├── MedicalCode.cs          # From US_007 task_001
│   ├── NoShowRisk.cs           # From US_007 task_001
│   ├── QueueEntry.cs           # From US_007 task_001
│   ├── AuditLog.cs             # To be created
│   ├── Notification.cs         # To be created
│   ├── InsuranceValidation.cs  # To be created
│   └── CalendarSync.cs         # To be created
└── Enums/
    ├── PatientStatus.cs        # From US_006
    ├── UserRole.cs             # From US_006
    ├── AppointmentStatus.cs    # From US_006
    ├── WaitlistStatus.cs       # From US_006
    ├── IntakeSource.cs         # From US_007
    ├── DocumentStatus.cs       # From US_007
    ├── ExtractedDataType.cs    # From US_007
    ├── ConflictStatus.cs       # From US_007
    ├── QueueStatus.cs          # From US_007
    ├── RiskLevel.cs            # From US_007
    ├── NotificationChannel.cs  # To be created
    ├── NotificationStatus.cs   # To be created
    ├── InsuranceValidationResult.cs # To be created
    ├── CalendarProvider.cs     # To be created
    └── CalendarSyncStatus.cs   # To be created
```

_Update this tree during execution based on the completion of dependent tasks._

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `server/src/PropelIQ.Domain/Entities/AuditLog.cs` | Immutable POCO — `init`-only properties, `JsonDocument?` Details (JSONB), no navigation properties (AD-7 write-only pattern) |
| CREATE | `server/src/PropelIQ.Domain/Entities/Notification.cs` | Notification delivery POCO — channel, templateType, status, sentAt, retryCount (DR-015) |
| CREATE | `server/src/PropelIQ.Domain/Entities/InsuranceValidation.cs` | Insurance validation record POCO — providerName, insuranceId, validationResult, validatedAt (DR-014) |
| CREATE | `server/src/PropelIQ.Domain/Entities/CalendarSync.cs` | Calendar sync POCO — provider, externalEventId, syncStatus, syncedAt (DR-017) |
| CREATE | `server/src/PropelIQ.Domain/Enums/NotificationChannel.cs` | Enum: Sms, Email, Push |
| CREATE | `server/src/PropelIQ.Domain/Enums/NotificationStatus.cs` | Enum: Pending, Sent, Failed, Delivered |
| CREATE | `server/src/PropelIQ.Domain/Enums/InsuranceValidationResult.cs` | Enum: Matched, NotMatched, Pending |
| CREATE | `server/src/PropelIQ.Domain/Enums/CalendarProvider.cs` | Enum: Google, Apple, Outlook |
| CREATE | `server/src/PropelIQ.Domain/Enums/CalendarSyncStatus.cs` | Enum: Synced, Failed, Pending |

## External References

- [C# 13 `init` accessor — immutable properties](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/init)
- [AD-7: Immutable Append-Only Audit Log (design.md)](../.propel/context/docs/design.md#ad-7)
- [NFR-009: Immutable audit log requirement (design.md)](../.propel/context/docs/design.md#nfr-009)
- [DR-014: Insurance validation storage (design.md)](../.propel/context/docs/design.md#dr-014)
- [DR-015: Notification delivery records (design.md)](../.propel/context/docs/design.md#dr-015)
- [DR-017: Calendar sync records (design.md)](../.propel/context/docs/design.md#dr-017)
- [System.Text.Json.JsonDocument — JSONB mapping in EF Core 9 + Npgsql](https://www.npgsql.org/efcore/mapping/json.html)

## Build Commands

```bash
# Build Domain project to verify entity classes compile without errors
cd server
dotnet build src/PropelIQ.Domain/PropelIQ.Domain.csproj

# Build entire solution to catch cross-project reference errors
dotnet build PropelIQ.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build src/PropelIQ.Domain` exits with code 0 — no compile errors
- [ ] `AuditLog.cs` has ZERO `set` accessors — all properties use `init` (AC-1 / AD-7)
- [ ] `AuditLog.Details` is typed `JsonDocument?` — enables JSONB mapping in task_002
- [ ] `AuditLog` has no EF navigation properties (`Patient`, `User` navigations are absent)
- [ ] `Notification` contains all AC-2 fields: `Channel`, `TemplateType`, `Status`, `SentAt`, `RetryCount`
- [ ] `CalendarSync` contains all AC-3 fields: `Provider`, `ExternalEventId`, `SyncStatus`, `SyncedAt`, `PatientId`, `AppointmentId`
- [ ] All 5 enum files created in `PropelIQ.Domain/Enums/`
- [ ] Solution builds cleanly — `dotnet build PropelIQ.sln` exits 0

## Implementation Checklist

- [ ] Create `AuditLog.cs` — verify all properties use `init`, no `set`, no navigation properties
- [ ] Create `Notification.cs` — verify `RetryCount` defaults to `0`, `AppointmentId` is nullable
- [ ] Create `InsuranceValidation.cs` — verify `ValidatedAt` is nullable (validation may be async/deferred)
- [ ] Create `CalendarSync.cs` — verify `AppointmentId` is non-nullable (sync always tied to an appointment)
- [ ] Create enum files: `NotificationChannel`, `NotificationStatus`, `InsuranceValidationResult`, `CalendarProvider`, `CalendarSyncStatus`
- [ ] Run `dotnet build src/PropelIQ.Domain` — confirm zero errors
- [ ] Confirm no data annotations (`[Required]`, `[MaxLength]`, etc.) appear in any of the 4 entity classes
- [ ] Run `dotnet build PropelIQ.sln` — confirm solution-level compile success
