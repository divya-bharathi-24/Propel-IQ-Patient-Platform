# Task - task_003_db_walkin_schema

## Requirement Reference

- **User Story:** us_026 — Staff Walk-In Booking with Optional Patient Account Creation
- **Story Location:** `.propel/context/tasks/EP-004/us_026/us_026.md`
- **Acceptance Criteria:**
  - AC-3: An Appointment record can be created with `patientId = null` and a non-null `anonymousVisitId` (UUID) for anonymous walk-in visits
  - AC-3 (QueueEntry): A `QueueEntry` can be created for an anonymous visit — `patientId` on `queue_entries` must also be nullable
- **Edge Cases:**
  - Walk-in for existing patient: `patientId` populated normally (nullable column is backwards-compatible with all prior bookings)
  - Fully booked slot walk-in: Appointment has `timeSlotStart = null` and `timeSlotEnd = null`; existing NOT NULL constraints on those columns must be relaxed or confirmed as already nullable

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

| Layer      | Technology                            | Version |
| ---------- | ------------------------------------- | ------- |
| Database   | PostgreSQL                            | 16+     |
| ORM        | Entity Framework Core                 | 9.x     |
| EF Driver  | Npgsql.EntityFrameworkCore.PostgreSQL | 9.x     |
| DB Hosting | Neon PostgreSQL (free tier)           | —       |
| Testing    | xUnit                                 | 2.x     |
| AI/ML      | N/A                                   | N/A     |
| Mobile     | N/A                                   | N/A     |

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

The existing `appointments` and `queue_entries` tables (created by US_006 foundational schema) assume `patient_id` is a non-nullable foreign key. US_026 requires anonymous walk-in visits where no patient is linked. This migration makes `patient_id` nullable on both tables and adds an `anonymous_visit_id` column to `appointments`. It also ensures `time_slot_start` and `time_slot_end` are nullable on `appointments` (needed for queue-only walk-ins with no assigned slot).

All changes are additive or column-constraint relaxations — no data is deleted, and existing `Booked` appointments retain their `patient_id` unchanged. Zero-downtime migration on PostgreSQL 16.

---

## Dependent Tasks

- **US_006 (foundational)** — `appointments` and `queue_entries` tables must exist before this migration runs

---

## Impacted Components

| Status | Component / Module                                                              | Project                                                                                                                  |
| ------ | ------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| MODIFY | `Appointment` EF Core entity                                                    | `Server/Infrastructure/Persistence/Entities/Appointment.cs` — `PatientId` becomes `Guid?`; add `AnonymousVisitId: Guid?` |
| MODIFY | `QueueEntry` EF Core entity                                                     | `Server/Infrastructure/Persistence/Entities/QueueEntry.cs` — `PatientId` becomes `Guid?`                                 |
| MODIFY | `AppointmentConfiguration.cs`                                                   | Update FK constraint to `IsRequired(false)`                                                                              |
| MODIFY | `QueueEntryConfiguration.cs`                                                    | Update FK constraint to `IsRequired(false)`                                                                              |
| CREATE | EF Core migration: `AllowAnonymousWalkIn`                                       | `Server/Infrastructure/Migrations/`                                                                                      |
| CREATE | `Server/Infrastructure/Migrations/<timestamp>_AllowAnonymousWalkIn.Designer.cs` | Migration snapshot                                                                                                       |

---

## Implementation Plan

1. **Modify `Appointment` entity**:

   ```csharp
   public Guid? PatientId { get; set; }          // was Guid (non-nullable FK)
   public Guid? AnonymousVisitId { get; set; }   // NEW — populated only for anonymous walk-ins
   public TimeOnly? TimeSlotStart { get; set; }  // confirm nullable (queue-only walk-ins have no slot)
   public TimeOnly? TimeSlotEnd { get; set; }    // confirm nullable
   ```

2. **Modify `QueueEntry` entity**:

   ```csharp
   public Guid? PatientId { get; set; }          // was Guid (non-nullable FK)
   ```

3. **Update `AppointmentConfiguration.cs`**:

   ```csharp
   builder.HasOne(a => a.Patient)
       .WithMany(p => p.Appointments)
       .HasForeignKey(a => a.PatientId)
       .IsRequired(false)                        // allows NULL patientId
       .OnDelete(DeleteBehavior.Restrict);

   builder.Property(a => a.AnonymousVisitId).IsRequired(false);
   builder.Property(a => a.TimeSlotStart).IsRequired(false);
   builder.Property(a => a.TimeSlotEnd).IsRequired(false);
   ```

4. **Update `QueueEntryConfiguration.cs`**:

   ```csharp
   builder.HasOne(q => q.Patient)
       .WithMany()
       .HasForeignKey(q => q.PatientId)
       .IsRequired(false)
       .OnDelete(DeleteBehavior.Restrict);
   ```

5. **EF Core migration `AllowAnonymousWalkIn`**:
   - `Up()`:
     ```sql
     ALTER TABLE appointments ALTER COLUMN patient_id DROP NOT NULL;
     ALTER TABLE appointments ADD COLUMN anonymous_visit_id UUID NULL;
     ALTER TABLE appointments ALTER COLUMN time_slot_start DROP NOT NULL;
     ALTER TABLE appointments ALTER COLUMN time_slot_end DROP NOT NULL;
     ALTER TABLE queue_entries ALTER COLUMN patient_id DROP NOT NULL;
     ```
   - `Down()`:
     ```sql
     -- Note: Down() can only safely run if no NULL patient_id / anonymous rows exist
     ALTER TABLE queue_entries ALTER COLUMN patient_id SET NOT NULL;
     ALTER TABLE appointments DROP COLUMN anonymous_visit_id;
     ALTER TABLE appointments ALTER COLUMN patient_id SET NOT NULL;
     -- time_slot_start/end: restore NOT NULL only if original schema had them non-nullable
     ```
   - All `ALTER COLUMN ... DROP NOT NULL` operations are non-blocking on PostgreSQL 16

6. **Index**: add a partial unique index on `anonymous_visit_id` where non-null:
   ```sql
   CREATE UNIQUE INDEX idx_appointments_anonymous_visit_id
       ON appointments (anonymous_visit_id)
       WHERE anonymous_visit_id IS NOT NULL;
   ```
   Ensures two anonymous walk-ins never share the same visit ID.

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no Server/ scaffold yet — greenfield ASP.NET Core project)
```

> Update with actual `Server/Infrastructure/Persistence/` tree after scaffold is complete.

---

## Expected Changes

| Action | File Path                                                                       | Description                                                                                                                                           |
| ------ | ------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| MODIFY | `Server/Infrastructure/Persistence/Entities/Appointment.cs`                     | `PatientId` → `Guid?`; add `AnonymousVisitId: Guid?`; `TimeSlotStart`/`TimeSlotEnd` → nullable                                                        |
| MODIFY | `Server/Infrastructure/Persistence/Entities/QueueEntry.cs`                      | `PatientId` → `Guid?`                                                                                                                                 |
| MODIFY | `Server/Infrastructure/Persistence/Configurations/AppointmentConfiguration.cs`  | FK `IsRequired(false)`, `AnonymousVisitId` nullable property, time slot nullable properties                                                           |
| MODIFY | `Server/Infrastructure/Persistence/Configurations/QueueEntryConfiguration.cs`   | FK `IsRequired(false)`                                                                                                                                |
| CREATE | `Server/Infrastructure/Migrations/<timestamp>_AllowAnonymousWalkIn.cs`          | `Up()`: DROP NOT NULL on patient_id, ADD COLUMN anonymous_visit_id, DROP NOT NULL on time slots, DROP NOT NULL on queue patient_id; `Down()` reversal |
| CREATE | `Server/Infrastructure/Migrations/<timestamp>_AllowAnonymousWalkIn.Designer.cs` | Migration snapshot                                                                                                                                    |

---

## External References

- [PostgreSQL 16 — ALTER TABLE ALTER COLUMN DROP NOT NULL (non-blocking)](https://www.postgresql.org/docs/16/sql-altertable.html)
- [EF Core 9 — Optional relationships / nullable FK (IsRequired(false))](https://learn.microsoft.com/en-us/ef/core/modeling/relationships/navigations#optional-relationships)
- [EF Core 9 — Migrations (Up / Down)](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli)
- [Npgsql EF Core — UUID column mapping](https://www.npgsql.org/efcore/mapping/general.html)
- [PostgreSQL — Partial unique index (WHERE ... IS NOT NULL)](https://www.postgresql.org/docs/16/indexes-partial.html)

---

## Build Commands

```bash
# Add migration
dotnet ef migrations add AllowAnonymousWalkIn --project Server/Server.csproj --output-dir Infrastructure/Migrations

# Apply migration
dotnet ef database update --project Server/Server.csproj

# Rollback migration (only safe if no anonymous rows exist)
dotnet ef database update <PreviousMigrationName> --project Server/Server.csproj

# Generate SQL script for review before applying to Neon PostgreSQL
dotnet ef migrations script --project Server/Server.csproj --output migration_allow_anonymous_walkin.sql
```

---

## Implementation Validation Strategy

- [ ] Migration `Up()` runs without error on Neon PostgreSQL 16 after all prior migrations
- [ ] Existing Appointment rows (patient-linked bookings) remain valid — `patient_id` values unchanged
- [ ] New anonymous Appointment row can be inserted with `patient_id = NULL` and non-null `anonymous_visit_id`
- [ ] Two anonymous Appointments with the same `anonymous_visit_id` are rejected by the unique partial index
- [ ] New QueueEntry row can be inserted with `patient_id = NULL` (linked to anonymous Appointment)
- [ ] Appointment with `time_slot_start = NULL` can be inserted (queue-only walk-in)
- [ ] Migration `Down()` removes `anonymous_visit_id` column without affecting existing data (only safe on fresh schema)
- [ ] EF Core `AppDbContext` resolves updated `Appointment` and `QueueEntry` entities without startup errors

---

## Implementation Checklist

- [x] Update `Appointment` entity: `PatientId → Guid?`, add `AnonymousVisitId: Guid?`, confirm `TimeSlotStart`/`TimeSlotEnd` are `TimeOnly?`
- [x] Update `QueueEntry` entity: `PatientId → Guid?`
- [x] Update `AppointmentConfiguration`: FK `IsRequired(false)`; map `AnonymousVisitId`, `TimeSlotStart`, `TimeSlotEnd` as nullable
- [x] Update `QueueEntryConfiguration`: FK `IsRequired(false)`
- [x] Write migration `AllowAnonymousWalkIn` `Up()`: 5x `ALTER COLUMN DROP NOT NULL` + `ADD COLUMN anonymous_visit_id UUID NULL`
- [x] Add partial unique index on `anonymous_visit_id WHERE NOT NULL` in the same migration
- [ ] Generate SQL script and review before applying to Neon production database
