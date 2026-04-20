# Task - TASK_002

## Requirement Reference

- User Story: [us_008] (extracted from input)
- Story Location: [.propel/context/tasks/EP-DATA/us_008/us_008.md]
- Acceptance Criteria:
  - **AC-1**: Given the AuditLog entity is configured, When the EF Core context initializes, Then a PostgreSQL trigger or row-level security policy is in place that rejects any UPDATE or DELETE against the `audit_logs` table.
  - **AC-2**: Given the Notification entity is persisted, When I store a notification delivery event, Then all required fields (channel, templateType, status, sentAt, retryCount) are stored correctly and queryable with the linked patientId and appointmentId.
  - **AC-3**: Given the CalendarSync entity is configured, When I retrieve a sync record, Then it returns provider, externalEventId, syncStatus, syncedAt, patientId, and appointmentId with FK integrity maintained.
  - **AC-4**: Given versioned migrations are applied, When I run `dotnet ef database update` from a clean database, Then all migrations apply in sequence without errors and the schema matches the current entity models.
- Edge Case:
  - What happens when an AuditLog row is modified post-insert? The PostgreSQL trigger (added via `migrationBuilder.Sql()` in task_003) raises `SQLSTATE 55000` before the operation completes. The EF Core configuration provides no `DeleteBehavior` cascade to AuditLog, so no ORM-level cascade can inadvertently delete rows.
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

| Layer    | Technology                  | Version |
| -------- | --------------------------- | ------- |
| Backend  | ASP.NET Core Web API        | .NET 9  |
| ORM      | Entity Framework Core       | 9.x     |
| Database | PostgreSQL                  | 16+     |
| DB Driver| Npgsql EF Core Provider     | 9.x     |
| AI/ML    | N/A                         | N/A     |
| Mobile   | N/A                         | N/A     |

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

Create four `IEntityTypeConfiguration<T>` classes — `AuditLogConfiguration`, `NotificationConfiguration`, `InsuranceValidationConfiguration`, and `CalendarSyncConfiguration` — in `PropelIQ.Infrastructure/Persistence/Configurations/`, and register the four new `DbSet<T>` properties on `AppDbContext`.

The critical design element is `AuditLogConfiguration`. Because `audit_logs` is INSERT-only (AD-7), EF Core must never issue `UPDATE` or `DELETE` against this table. The fluent configuration achieves this by:
1. Declaring no `DeleteBehavior` cascade from any parent to `AuditLog` (raw Guid columns, no navigation properties configured).
2. Ensuring `AppDbContext.SaveChangesAsync()` override does NOT flip `EntityState.Deleted` to a soft-delete for `AuditLog` entries — AuditLog does not implement the soft-deletable interface and should never appear in `ChangeTracker` as `Deleted`.
3. The actual INSERT-only enforcement trigger DDL is applied in the migration (task_003) via `migrationBuilder.Sql()`.

All enum properties across all four configurations use `HasConversion<string>()` — consistent with the pattern established for US_006 and US_007 entities.

## Dependent Tasks

- US_008 `task_001_be_audit_notification_entity_classes.md` — all 4 POCO classes and 5 enum types must exist before fluent configs can be written

## Impacted Components

| Component | Action | Notes |
| --------- | ------ | ----- |
| `server/src/PropelIQ.Infrastructure/Persistence/Configurations/AuditLogConfiguration.cs` | CREATE | No delete behavior, JSONB Details column, 3 performance indexes |
| `server/src/PropelIQ.Infrastructure/Persistence/Configurations/NotificationConfiguration.cs` | CREATE | String enum conversions, FK Restrict to Patient/Appointment, status + patientId indexes |
| `server/src/PropelIQ.Infrastructure/Persistence/Configurations/InsuranceValidationConfiguration.cs` | CREATE | String enum conversions, FK Restrict to Patient, patientId + result indexes |
| `server/src/PropelIQ.Infrastructure/Persistence/Configurations/CalendarSyncConfiguration.cs` | CREATE | String enum conversions, unique composite index (Provider + ExternalEventId), FK Restrict |
| `server/src/PropelIQ.Infrastructure/Persistence/AppDbContext.cs` | MODIFY | Add 4 new `DbSet<T>` properties; confirm `SaveChangesAsync` override does not soft-delete AuditLog |

## Implementation Plan

1. **Create `AuditLogConfiguration.cs`** — `HasKey(x => x.Id)`; map table to `"audit_logs"` using snake_case convention; `Details` column → `HasColumnType("jsonb")`; `Action` and `EntityType` → `IsRequired().HasMaxLength(100)`; `IpAddress` → `HasMaxLength(45)` (fits IPv6); `CorrelationId` → `HasMaxLength(64)`; index `ix_audit_logs_user_id` on `UserId`; index `ix_audit_logs_patient_id` on `PatientId`; index `ix_audit_logs_timestamp` on `Timestamp` (descending). No FK navigation configuration — UserId/PatientId are raw column values only (no `HasOne`/`WithMany`). Do NOT add `.OnDelete(DeleteBehavior.Cascade)` or any delete behavior.

2. **Create `NotificationConfiguration.cs`** — `HasKey(x => x.Id)`; `TemplateType` → `IsRequired().HasMaxLength(150)`; `ErrorMessage` → `HasMaxLength(500)`; `Channel` → `HasConversion<string>().HasMaxLength(20)`; `Status` → `HasConversion<string>().HasMaxLength(30)`; index `ix_notifications_patient_id` on `PatientId`; index `ix_notifications_status` on `Status`; index `ix_notifications_appointment_id` on `AppointmentId`; FK `HasOne(n => n.Patient).WithMany().HasForeignKey(n => n.PatientId).OnDelete(DeleteBehavior.Restrict)`; FK for `AppointmentId` as optional (`IsRequired(false)`) with `OnDelete(DeleteBehavior.Restrict)` — configured separately via `HasOne<Appointment>().WithMany().HasForeignKey(n => n.AppointmentId).IsRequired(false).OnDelete(DeleteBehavior.Restrict)`.

3. **Create `InsuranceValidationConfiguration.cs`** — `HasKey(x => x.Id)`; `ProviderName` → `IsRequired().HasMaxLength(200)`; `InsuranceId` → `IsRequired().HasMaxLength(100)`; `ValidationMessage` → `HasMaxLength(500)`; `ValidationResult` → `HasConversion<string>().HasMaxLength(20)`; index `ix_insurance_validations_patient_id` on `PatientId`; index `ix_insurance_validations_result` on `ValidationResult`; FK `HasOne(iv => iv.Patient).WithMany().HasForeignKey(iv => iv.PatientId).OnDelete(DeleteBehavior.Restrict)`.

4. **Create `CalendarSyncConfiguration.cs`** — `HasKey(x => x.Id)`; `ExternalEventId` → `IsRequired().HasMaxLength(255)`; `ErrorMessage` → `HasMaxLength(500)`; `Provider` → `HasConversion<string>().HasMaxLength(20)`; `SyncStatus` → `HasConversion<string>().HasMaxLength(20)`; unique composite index `ix_calendar_sync_provider_external_id` on `(Provider, ExternalEventId)` — prevents duplicate sync records for the same external event; index `ix_calendar_sync_appointment_id` on `AppointmentId`; FK `HasOne(cs => cs.Patient).WithMany().HasForeignKey(cs => cs.PatientId).OnDelete(DeleteBehavior.Restrict)`; FK `HasOne(cs => cs.Appointment).WithMany().HasForeignKey(cs => cs.AppointmentId).OnDelete(DeleteBehavior.Restrict)`.

5. **Modify `AppDbContext.cs`** — Add 4 new `DbSet<T>` properties: `public DbSet<AuditLog> AuditLogs => Set<AuditLog>();`, `public DbSet<Notification> Notifications => Set<Notification>();`, `public DbSet<InsuranceValidation> InsuranceValidations => Set<InsuranceValidation>();`, `public DbSet<CalendarSync> CalendarSyncs => Set<CalendarSync>();`. Use expression-bodied `Set<T>()` form consistent with existing properties.

6. **Verify `AppDbContext.SaveChangesAsync()` override excludes `AuditLog`** — Review the existing override that intercepts `EntityState.Deleted` and converts it to a soft-delete status flip. Confirm it uses a guard (interface check or explicit entity type exclusion) so that `AuditLog` entries in `ChangeTracker` are NOT modified to appear as soft-deleted. Since `AuditLog` has no `Status` or `IsDeleted` property, the override must not attempt to modify it. If the override checks for an interface (e.g., `ISoftDeletable`), confirm `AuditLog` does not implement that interface.

7. **Verify `modelBuilder.ApplyConfigurationsFromAssembly`** auto-discovers all 4 new configurations — no manual registration needed. Confirm the assembly reference in `OnModelCreating` covers `PropelIQ.Infrastructure` where the new config files reside.

8. **Run `dotnet build` and `dotnet ef migrations list`** — Build must exit 0; `dotnet ef migrations list` should list `Initial`, `AddClinicalEntities` as existing, with a pending migration detectable for the new entities. (Actual migration creation is deferred to task_003.)

## Current Project State

```
server/src/PropelIQ.Infrastructure/
├── Persistence/
│   ├── AppDbContext.cs                          # Modify: add 4 DbSet<T>; verify SaveChangesAsync guard
│   └── Configurations/
│       ├── PatientConfiguration.cs             # From US_006 task_002
│       ├── UserConfiguration.cs                # From US_006 task_002
│       ├── AppointmentConfiguration.cs         # From US_006 task_002
│       ├── WaitlistEntryConfiguration.cs       # From US_006 task_002
│       ├── SpecialtyConfiguration.cs           # From US_006 task_002
│       ├── IntakeRecordConfiguration.cs        # From US_007 task_002
│       ├── ClinicalDocumentConfiguration.cs    # From US_007 task_002
│       ├── ExtractedDataConfiguration.cs       # From US_007 task_002
│       ├── DataConflictConfiguration.cs        # From US_007 task_002
│       ├── MedicalCodeConfiguration.cs         # From US_007 task_002
│       ├── NoShowRiskConfiguration.cs          # From US_007 task_002
│       ├── QueueEntryConfiguration.cs          # From US_007 task_002
│       ├── AuditLogConfiguration.cs            # To be created
│       ├── NotificationConfiguration.cs        # To be created
│       ├── InsuranceValidationConfiguration.cs # To be created
│       └── CalendarSyncConfiguration.cs        # To be created
```

_Update this tree during execution based on the completion of dependent tasks._

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `server/src/PropelIQ.Infrastructure/Persistence/Configurations/AuditLogConfiguration.cs` | `IEntityTypeConfiguration<AuditLog>` — JSONB Details, 3 indexes, no FK navigation, no delete behavior |
| CREATE | `server/src/PropelIQ.Infrastructure/Persistence/Configurations/NotificationConfiguration.cs` | `IEntityTypeConfiguration<Notification>` — string enum conversions, FK Restrict, status/patient indexes |
| CREATE | `server/src/PropelIQ.Infrastructure/Persistence/Configurations/InsuranceValidationConfiguration.cs` | `IEntityTypeConfiguration<InsuranceValidation>` — string enum conversion, FK Restrict, patient/result indexes |
| CREATE | `server/src/PropelIQ.Infrastructure/Persistence/Configurations/CalendarSyncConfiguration.cs` | `IEntityTypeConfiguration<CalendarSync>` — string enum conversions, unique composite index, FK Restrict |
| MODIFY | `server/src/PropelIQ.Infrastructure/Persistence/AppDbContext.cs` | Add 4 `DbSet<T>` properties; verify `SaveChangesAsync` override excludes AuditLog |

## External References

- [EF Core 9 — `IEntityTypeConfiguration<T>`](https://learn.microsoft.com/en-us/ef/core/modeling/entity-types#applying-configuration-from-an-assembly)
- [Npgsql EF Core — JSONB column mapping (`HasColumnType("jsonb")`)](https://www.npgsql.org/efcore/mapping/json.html)
- [EF Core 9 — `HasConversion<string>()` for enums](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions#built-in-converters)
- [EF Core 9 — `DeleteBehavior.Restrict`](https://learn.microsoft.com/en-us/ef/core/saving/cascade-delete#restricting-related-data)
- [EF Core 9 — Unique composite indexes (`HasIndex` with multiple properties)](https://learn.microsoft.com/en-us/ef/core/modeling/indexes#composite-indexes)
- [AD-7: Immutable Append-Only Audit Log (design.md)](../.propel/context/docs/design.md#ad-7)

## Build Commands

```bash
# Build Infrastructure project to validate all 4 configuration classes
cd server
dotnet build src/PropelIQ.Infrastructure/PropelIQ.Infrastructure.csproj

# Confirm EF migrations list runs without model snapshot errors
DATABASE_URL="<connection-string>" \
  dotnet ef migrations list \
  --project src/PropelIQ.Infrastructure \
  --startup-project src/PropelIQ.Api

# Full solution build
dotnet build PropelIQ.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build src/PropelIQ.Infrastructure` exits 0 — no compile errors
- [ ] `AuditLogConfiguration.cs` has no `HasOne`/`WithMany` FK configuration — raw columns only (AC-1 / AD-7)
- [ ] `AuditLogConfiguration.cs` has no `OnDelete(DeleteBehavior.*)` call — no cascade to audit_logs
- [ ] `Details` column mapped as `HasColumnType("jsonb")` in `AuditLogConfiguration`
- [ ] All 4 enum properties across all 4 configurations use `HasConversion<string>()` (AC-2, AC-3)
- [ ] `ix_calendar_sync_provider_external_id` is a unique composite index on `(Provider, ExternalEventId)` (AC-3)
- [ ] `AppDbContext` has 4 new `DbSet<T>` properties
- [ ] `AppDbContext.SaveChangesAsync()` override guard confirmed — AuditLog cannot be soft-deleted
- [ ] `dotnet ef migrations list` succeeds — no model snapshot exception
- [ ] `dotnet build PropelIQ.sln` exits 0

## Implementation Checklist

- [ ] Create `AuditLogConfiguration.cs` — confirm JSONB Details, no FK navigation, no delete behavior, 3 indexes
- [ ] Create `NotificationConfiguration.cs` — confirm enum conversions, optional AppointmentId FK, status index
- [ ] Create `InsuranceValidationConfiguration.cs` — confirm enum conversion, ProviderName max 200, patientId FK Restrict
- [ ] Create `CalendarSyncConfiguration.cs` — confirm unique composite index (Provider, ExternalEventId), both FK Restrict
- [ ] Modify `AppDbContext.cs` — add 4 `DbSet<T>` using expression-bodied `Set<T>()` form
- [ ] Verify `SaveChangesAsync` override: confirm `AuditLog` is not intercepted; only soft-deletable entities are affected
- [ ] Run `dotnet build src/PropelIQ.Infrastructure` — confirm 0 errors
- [ ] Run `dotnet ef migrations list` — confirm `Initial` and `AddClinicalEntities` still listed; no snapshot error
