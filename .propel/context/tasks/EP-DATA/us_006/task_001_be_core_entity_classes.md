# Task - TASK_001

## Requirement Reference

- User Story: [us_006] (extracted from input)
- Story Location: [.propel/context/tasks/EP-DATA/us_006/us_006.md]
- Acceptance Criteria:
  - **AC-1**: Given the EF Core 9 context is configured, When I run `dotnet ef migrations add Initial`, Then a migration script is generated that creates Patient, User, Appointment, WaitlistEntry, and Specialty tables with all columns, constraints, and foreign keys correctly defined.
  - **AC-4**: Given the data model is applied, When I query related entities, Then FK constraints are enforced (e.g., inserting an Appointment with a non-existent `patientId` raises a constraint violation).
- Edge Case:
  - How is email uniqueness for Patient enforced at the DB level? — Unique index on `Patient.email`; defined in EF Core fluent configuration (handled in task_002); the C# property is non-nullable `string` with `[MaxLength]`.

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

| Layer      | Technology           | Version |
| ---------- | -------------------- | ------- |
| Backend    | ASP.NET Core Web API | .NET 9  |
| ORM        | Entity Framework Core | 9.x    |
| Database   | PostgreSQL           | 16+     |
| AI/ML      | N/A                  | N/A     |
| Vector Store | N/A                | N/A     |
| AI Gateway | N/A                  | N/A     |
| Mobile     | N/A                  | N/A     |

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

Create the C# POCO domain entity classes for the five core entities scoped to US_006: `Patient`, `User`, `Appointment`, `WaitlistEntry`, and `Specialty`. Each class lives in the `Domain/Entities/` folder of the `PropelIQ.Infrastructure` (or `PropelIQ.Domain`) project. Supporting enum types (`PatientStatus`, `UserRole`, `AppointmentStatus`, `WaitlistStatus`) are defined in `Domain/Enums/`. No fluent configuration or `DbContext` registration is included here — those are handled in `task_002_db_efcore_fluent_config.md`.

## Dependent Tasks

- US_002 — .NET 9 solution must exist with at least a `PropelIQ.Domain` or `PropelIQ.Infrastructure` project that references `Microsoft.EntityFrameworkCore 9.x` via NuGet.

## Impacted Components

| Component | Action | Notes |
| --------- | ------ | ----- |
| `server/src/PropelIQ.Domain/Entities/Patient.cs` | CREATE | Patient domain entity |
| `server/src/PropelIQ.Domain/Entities/User.cs` | CREATE | User (auth identity) domain entity |
| `server/src/PropelIQ.Domain/Entities/Appointment.cs` | CREATE | Appointment domain entity with concurrency token |
| `server/src/PropelIQ.Domain/Entities/WaitlistEntry.cs` | CREATE | WaitlistEntry domain entity |
| `server/src/PropelIQ.Domain/Entities/Specialty.cs` | CREATE | Specialty lookup entity |
| `server/src/PropelIQ.Domain/Enums/PatientStatus.cs` | CREATE | Enum: Active, Deactivated |
| `server/src/PropelIQ.Domain/Enums/UserRole.cs` | CREATE | Enum: Patient, Staff, Admin |
| `server/src/PropelIQ.Domain/Enums/AppointmentStatus.cs` | CREATE | Enum: Booked, Arrived, Cancelled, Completed |

## Implementation Plan

1. **Create enum types** — Add `PatientStatus` (Active, Deactivated), `UserRole` (Patient, Staff, Admin), `AppointmentStatus` (Booked, Arrived, Cancelled, Completed), `WaitlistStatus` (Active, Swapped, Expired) in `Domain/Enums/`. Store as `string` in the database (EF `HasConversion<string>()` configured in task_002) for human-readable audit logs.

2. **Create `Patient` entity** — Properties: `Guid Id`, `string Name`, `string Email`, `string Phone`, `DateOnly DateOfBirth`, `string PasswordHash`, `bool EmailVerified`, `PatientStatus Status`, `DateTime CreatedAt`. Navigation collections: `ICollection<Appointment> Appointments` (initialised to `new List<Appointment>()`). No data annotations — all constraints via fluent configuration.

3. **Create `User` entity** — Properties: `Guid Id`, `string Email`, `string PasswordHash`, `UserRole Role`, `PatientStatus Status` (reuses Active/Deactivated values), `DateTime? LastLoginAt`, `DateTime CreatedAt`. Navigation: `ICollection<AuditLog> AuditLogs` (stub navigation — AuditLog entity added in a later US).

4. **Create `Specialty` entity** — Properties: `Guid Id`, `string Name`, `string? Description`. Navigation: `ICollection<Appointment> Appointments`.

5. **Create `Appointment` entity** — Properties: `Guid Id`, `Guid PatientId`, `Guid SpecialtyId`, `DateOnly Date`, `TimeOnly TimeSlotStart`, `TimeOnly TimeSlotEnd`, `AppointmentStatus Status`, `string? CancellationReason`, `Guid CreatedBy`, `DateTime CreatedAt`, `uint RowVersion` (EF concurrency token — mapped to PostgreSQL `xmin` system column in fluent config). Navigation: `Patient Patient`, `Specialty Specialty`, `WaitlistEntry? WaitlistEntry`.

6. **Create `WaitlistEntry` entity** — Properties: `Guid Id`, `Guid PatientId`, `Guid CurrentAppointmentId`, `DateOnly PreferredDate`, `TimeOnly PreferredTimeSlot`, `DateTime EnrolledAt`, `WaitlistStatus Status`. Navigation: `Patient Patient`, `Appointment CurrentAppointment`.

7. **Verify property nullability alignment** — All non-nullable reference type properties must be `required` or initialised to avoid CS8618 null-safety warnings in .NET 9. Optional FK columns (`CancellationReason`, `Description`) must be declared `string?`.

8. **Confirm project reference** — Verify `PropelIQ.Infrastructure.csproj` references `PropelIQ.Domain.csproj` (or entities are co-located in Infrastructure). Entities must be discoverable by `AppDbContext` in task_002.

## Current Project State

```
server/
├── PropelIQ.sln
└── src/
    ├── PropelIQ.Api/             # .NET 9 Web API entry point (from US_002)
    ├── PropelIQ.Domain/          # Domain layer — to be populated by this task
    │   ├── Entities/             # To be created
    │   └── Enums/                # To be created
    └── PropelIQ.Infrastructure/  # EF Core project (from US_002)
```

_Update this tree during execution based on completed dependent tasks._

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `server/src/PropelIQ.Domain/Enums/PatientStatus.cs` | Enum: Active, Deactivated |
| CREATE | `server/src/PropelIQ.Domain/Enums/UserRole.cs` | Enum: Patient, Staff, Admin |
| CREATE | `server/src/PropelIQ.Domain/Enums/AppointmentStatus.cs` | Enum: Booked, Arrived, Cancelled, Completed |
| CREATE | `server/src/PropelIQ.Domain/Enums/WaitlistStatus.cs` | Enum: Active, Swapped, Expired |
| CREATE | `server/src/PropelIQ.Domain/Entities/Patient.cs` | Patient entity with navigation collections |
| CREATE | `server/src/PropelIQ.Domain/Entities/User.cs` | User entity with role and login tracking |
| CREATE | `server/src/PropelIQ.Domain/Entities/Specialty.cs` | Specialty lookup entity |
| CREATE | `server/src/PropelIQ.Domain/Entities/Appointment.cs` | Appointment entity with concurrency token `RowVersion` |

### Reference: `Appointment.cs` (partial — concurrency token)

```csharp
namespace PropelIQ.Domain.Entities;

public class Appointment
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid SpecialtyId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly TimeSlotStart { get; set; }
    public TimeOnly TimeSlotEnd { get; set; }
    public AppointmentStatus Status { get; set; }
    public string? CancellationReason { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    // Optimistic concurrency token — mapped to PostgreSQL xmin in fluent config
    public uint RowVersion { get; set; }

    // Navigation properties
    public Patient Patient { get; set; } = null!;
    public Specialty Specialty { get; set; } = null!;
    public WaitlistEntry? WaitlistEntry { get; set; }
}
```

### Reference: `Patient.cs` (partial — soft delete status)

```csharp
namespace PropelIQ.Domain.Entities;

public class Patient
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public required string Phone { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public required string PasswordHash { get; set; }
    public bool EmailVerified { get; set; }
    // Soft-delete field — DR-010; never hard DELETE
    public PatientStatus Status { get; set; } = PatientStatus.Active;
    public DateTime CreatedAt { get; set; }

    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}
```

## External References

- [EF Core 9 — Entity types and properties](https://learn.microsoft.com/en-us/ef/core/modeling/entity-types)
- [EF Core 9 — Relationships and navigation properties](https://learn.microsoft.com/en-us/ef/core/modeling/relationships)
- [EF Core 9 — Optimistic concurrency (PostgreSQL xmin)](https://www.npgsql.org/efcore/modeling/concurrency.html)
- [.NET 9 — Nullable reference types (CS8618)](https://learn.microsoft.com/en-us/dotnet/csharp/nullable-references)
- [PostgreSQL 16 — UUID primary key type](https://www.postgresql.org/docs/current/datatype-uuid.html)

## Build Commands

```bash
# Verify domain project compiles (no EF configuration yet)
cd server
dotnet build src/PropelIQ.Domain/PropelIQ.Domain.csproj

# Check for CS8618 null-safety warnings
dotnet build src/PropelIQ.Domain/PropelIQ.Domain.csproj -warnaserror:CS8618
```

## Implementation Validation Strategy

- [ ] All 5 entity classes compile with zero warnings under `dotnet build`
- [ ] All 4 enum types compile and are referenced correctly by entity properties
- [ ] `Appointment.RowVersion` is of type `uint` (required for PostgreSQL `xmin` mapping in task_002)
- [ ] `Patient.Status` and `Appointment.Status` default to `Active`/`Booked` respectively via property initialisers
- [ ] No data annotations (`[Key]`, `[Required]`, `[MaxLength]`) — all constraints deferred to fluent configuration in task_002
- [ ] Navigation properties on `Appointment` use `null!` suppressor (non-null assertion) for EF-managed references

## Implementation Checklist

- [ ] Create `PatientStatus`, `UserRole`, `AppointmentStatus`, `WaitlistStatus` enums in `Domain/Enums/`
- [ ] Create `Specialty.cs` — `Guid Id`, `string Name`, `string? Description`, `ICollection<Appointment> Appointments`
- [ ] Create `Patient.cs` — all DR-001 attributes, `PatientStatus Status = PatientStatus.Active`, navigation collection
- [ ] Create `User.cs` — all attributes from design.md `User` entity, `UserRole Role`, `PatientStatus Status`
- [ ] Create `Appointment.cs` — all DR-002 attributes, `uint RowVersion` concurrency token, navigation to `Patient`, `Specialty`, `WaitlistEntry?`
- [ ] Create `WaitlistEntry.cs` — all DR-003 attributes, `DateTime EnrolledAt` (FIFO ordering key), navigation to `Patient`, `Appointment`
- [ ] Verify all `required` / `null!` / `string?` usage is correct for .NET 9 nullable reference types
- [ ] Run `dotnet build` on Domain project; confirm zero errors and zero CS8618 warnings
