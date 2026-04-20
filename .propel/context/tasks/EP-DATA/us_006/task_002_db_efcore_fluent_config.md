# Task - TASK_002

## Requirement Reference

- User Story: [us_006] (extracted from input)
- Story Location: [.propel/context/tasks/EP-DATA/us_006/us_006.md]
- Acceptance Criteria:
  - **AC-2**: Given the Patient and Appointment entities are configured, When a Patient record is soft-deleted, Then the `status` field is set to `Deactivated` and the row is retained; no hard DELETE is issued.
  - **AC-3**: Given the Appointment entity is configured with optimistic concurrency, When two concurrent requests attempt to book the same slot, Then only one succeeds; the other receives a `DbUpdateConcurrencyException`.
  - **AC-4**: Given the data model is applied, When I query related entities, Then FK constraints are enforced (e.g., inserting an Appointment with a non-existent `patientId` raises a constraint violation).
- Edge Case:
  - How is email uniqueness for Patient enforced at the DB level? — Unique index on `Patient.email` via `HasIndex(p => p.Email).IsUnique()` in `PatientConfiguration`.
  - What happens when a migration is applied to a database that already has data? — Additive-only migrations; enforced by PR review and EF migration generator output review.

## Design References (Frontend Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **UI Impact**        | No    |
| **Figma URL**        | N/A   |
| **Wireframe Status** | N/A   |
| **Wireframe Type**   | N/A   |
| **Wireframe Type**   | N/A   |
| **Wireframe Path/URL** | N/A |
| **Screen Spec**      | N/A   |
| **UXR Requirements** | N/A   |
| **Design Tokens**    | N/A   |

## Applicable Technology Stack

| Layer      | Technology            | Version  |
| ---------- | --------------------- | -------- |
| Backend    | ASP.NET Core Web API  | .net 10   |
| ORM        | Entity Framework Core | 9.x      |
| Database   | PostgreSQL            | 16+      |
| DB Driver  | Npgsql EF Core Provider | 9.x    |
| AI/ML      | N/A                   | N/A      |
| Mobile     | N/A                   | N/A      |

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

Implement EF Core 9 `IEntityTypeConfiguration<T>` classes for all five US_006 entities (`Patient`, `User`, `Appointment`, `WaitlistEntry`, `Specialty`), register them in `AppDbContext`, configure soft-delete global query filters, and set up the optimistic concurrency token on `Appointment` using PostgreSQL's `xmin` system column. This task produces the full EF Core mapping layer on top of the plain C# entity classes from `task_001_be_core_entity_classes.md`.

## Dependent Tasks

- `task_001_be_core_entity_classes.md` — all five entity classes and enum types must exist before fluent configuration can reference them

## Impacted Components

| Component | Action | Notes |
| --------- | ------ | ----- |
| `server/src/PropelIQ.Infrastructure/Persistence/Configurations/PatientConfiguration.cs` | CREATE | Fluent config for Patient |
| `server/src/PropelIQ.Infrastructure/Persistence/Configurations/UserConfiguration.cs` | CREATE | Fluent config for User |
| `server/src/PropelIQ.Infrastructure/Persistence/Configurations/AppointmentConfiguration.cs` | CREATE | Fluent config for Appointment — includes xmin concurrency token |
| `server/src/PropelIQ.Infrastructure/Persistence/Configurations/WaitlistEntryConfiguration.cs` | CREATE | Fluent config for WaitlistEntry |
| `server/src/PropelIQ.Infrastructure/Persistence/Configurations/SpecialtyConfiguration.cs` | CREATE | Fluent config for Specialty |
| `server/src/PropelIQ.Infrastructure/Persistence/AppDbContext.cs` | CREATE | EF Core DbContext — registers all entity sets and applies configurations |
| `server/src/PropelIQ.Infrastructure/PropelIQ.Infrastructure.csproj` | MODIFY | Add `Npgsql.EntityFrameworkCore.PostgreSQL 9.x` NuGet package if not present |

## Implementation Plan

1. **Create `PatientConfiguration`** — Table name `patients`, PK `id` (UUID, `ValueGeneratedOnAdd`). Properties: `name` (varchar 200, required), `email` (varchar 320, required), `phone` (varchar 30, required), `date_of_birth` (date), `password_hash` (varchar 500, required), `email_verified` (bool, default false), `status` (varchar 20, `HasConversion<string>()`, required), `created_at` (timestamp with time zone, default `now()`). Unique index: `HasIndex(p => p.Email).IsUnique().HasDatabaseName("ix_patients_email")`. Soft-delete global query filter: `HasQueryFilter(p => p.Status != PatientStatus.Deactivated)`.

2. **Create `UserConfiguration`** — Table name `users`, PK `id` (UUID). Properties: `email` (varchar 320, required), `password_hash` (varchar 500, required), `role` (varchar 20, `HasConversion<string>()`), `status` (varchar 20, `HasConversion<string>()`), `last_login_at` (timestamp?, nullable), `created_at` (timestamp, default `now()`). Unique index: `HasIndex(u => u.Email).IsUnique().HasDatabaseName("ix_users_email")`.

3. **Create `SpecialtyConfiguration`** — Table name `specialties`, PK `id` (UUID). Properties: `name` (varchar 100, required), `description` (varchar 500, nullable). Seed two starter specialties (`HasData`) for local development and smoke tests.

4. **Create `AppointmentConfiguration`** — Table name `appointments`, PK `id` (UUID). Properties: `patient_id` (FK → patients, required, restrict-or-no-action on delete to prevent cascade), `specialty_id` (FK → specialties, required), `date` (date), `time_slot_start` (time), `time_slot_end` (time), `status` (varchar 20, `HasConversion<string>()`), `cancellation_reason` (varchar 500, nullable), `created_by` (Guid, required), `created_at` (timestamp). **Optimistic concurrency** — `Property(a => a.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion()` (Npgsql `xmin` concurrency pattern). Composite index on `(date, time_slot_start, specialty_id)` for availability queries. Soft-delete global query filter: `HasQueryFilter(a => a.Status != AppointmentStatus.Cancelled)`.

5. **Create `WaitlistEntryConfiguration`** — Table name `waitlist_entries`, PK `id` (UUID). Properties: `patient_id` (FK → patients), `current_appointment_id` (FK → appointments), `preferred_date` (date), `preferred_time_slot` (time), `enrolled_at` (timestamp — FIFO ordering key per DR-003), `status` (varchar 20, `HasConversion<string>()`). Index on `enrolled_at` for FIFO queue ordering: `HasIndex(w => w.EnrolledAt).HasDatabaseName("ix_waitlist_enrolled_at")`.

6. **Create `AppDbContext`** — Inherit `DbContext`. Expose `DbSet<Patient> Patients`, `DbSet<User> Users`, `DbSet<Appointment> Appointments`, `DbSet<WaitlistEntry> WaitlistEntries`, `DbSet<Specialty> Specialties`. In `OnModelCreating`, call `modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly)` to auto-discover all `IEntityTypeConfiguration<T>` implementations. Set `UseSnakeCaseNamingConvention()` (Npgsql convention) for consistent PostgreSQL column naming.

7. **Register `AppDbContext` in DI** — In `Program.cs` (or a dedicated `InfrastructureServiceExtensions.cs`), call `builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(builder.Configuration["DATABASE_URL"]).UseSnakeCaseNamingConvention())`.

8. **Override `SaveChangesAsync` for soft-delete enforcement** — In `AppDbContext`, override `SaveChangesAsync` to intercept `EntityState.Deleted` entries of type `Patient` or `Appointment`; change state to `Modified` and set `Status = Deactivated` / `Status = Cancelled` respectively, preventing hard DELETEs (DR-010, AC-2).

## Current Project State

```
server/src/
├── PropelIQ.Domain/
│   ├── Entities/           # Created in task_001
│   │   ├── Patient.cs
│   │   ├── User.cs
│   │   ├── Appointment.cs
│   │   ├── WaitlistEntry.cs
│   │   └── Specialty.cs
│   └── Enums/              # Created in task_001
├── PropelIQ.Infrastructure/
│   ├── Persistence/
│   │   ├── Configurations/ # To be created
│   │   └── AppDbContext.cs # To be created
│   └── PropelIQ.Infrastructure.csproj
└── PropelIQ.Api/
    └── Program.cs          # To be modified (DI registration)
```

_Update this tree during execution based on completed dependent tasks._

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `server/src/PropelIQ.Infrastructure/Persistence/AppDbContext.cs` | EF Core DbContext with all 5 DbSets, `ApplyConfigurationsFromAssembly`, soft-delete `SaveChangesAsync` override |
| CREATE | `server/src/PropelIQ.Infrastructure/Persistence/Configurations/PatientConfiguration.cs` | Fluent config: column lengths, unique email index, soft-delete query filter |
| CREATE | `server/src/PropelIQ.Infrastructure/Persistence/Configurations/UserConfiguration.cs` | Fluent config: column lengths, unique email index, enum-to-string conversion |
| CREATE | `server/src/PropelIQ.Infrastructure/Persistence/Configurations/AppointmentConfiguration.cs` | Fluent config: FKs, xmin concurrency token, composite availability index, soft-delete filter |
| CREATE | `server/src/PropelIQ.Infrastructure/Persistence/Configurations/WaitlistEntryConfiguration.cs` | Fluent config: FKs, FIFO `enrolled_at` index |
| CREATE | `server/src/PropelIQ.Infrastructure/Persistence/Configurations/SpecialtyConfiguration.cs` | Fluent config: column lengths, seed data |
| MODIFY | `server/src/PropelIQ.Api/Program.cs` | Register `AppDbContext` with Npgsql using `DATABASE_URL` |

### Reference: `AppointmentConfiguration.cs` — optimistic concurrency (xmin)

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropelIQ.Domain.Entities;
using PropelIQ.Domain.Enums;

namespace PropelIQ.Infrastructure.Persistence.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();

        builder.Property(a => a.Status)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired();

        // Optimistic concurrency via PostgreSQL xmin system column
        builder.Property(a => a.RowVersion)
               .HasColumnName("xmin")
               .HasColumnType("xid")
               .IsRowVersion();

        builder.HasOne(a => a.Patient)
               .WithMany(p => p.Appointments)
               .HasForeignKey(a => a.PatientId)
               .OnDelete(DeleteBehavior.Restrict);  // DR-009 — no cascade

        builder.HasOne(a => a.Specialty)
               .WithMany(s => s.Appointments)
               .HasForeignKey(a => a.SpecialtyId)
               .OnDelete(DeleteBehavior.Restrict);

        // Composite index for slot availability queries
        builder.HasIndex(a => new { a.Date, a.TimeSlotStart, a.SpecialtyId })
               .HasDatabaseName("ix_appointments_slot_lookup");

        // Soft-delete query filter — DR-010
        builder.HasQueryFilter(a => a.Status != AppointmentStatus.Cancelled);
    }
}
```

### Reference: `AppDbContext.cs` — soft-delete `SaveChangesAsync` override

```csharp
public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
{
    foreach (var entry in ChangeTracker.Entries())
    {
        if (entry.State == EntityState.Deleted)
        {
            if (entry.Entity is Patient patient)
            {
                entry.State = EntityState.Modified;
                patient.Status = PatientStatus.Deactivated;
            }
            else if (entry.Entity is Appointment appointment)
            {
                entry.State = EntityState.Modified;
                appointment.Status = AppointmentStatus.Cancelled;
            }
        }
    }
    return await base.SaveChangesAsync(cancellationToken);
}
```

## External References

- [EF Core 9 — `IEntityTypeConfiguration<T>` pattern](https://learn.microsoft.com/en-us/ef/core/modeling/#grouping-configuration)
- [EF Core 9 — Global query filters (soft delete)](https://learn.microsoft.com/en-us/ef/core/querying/filters)
- [EF Core 9 — Optimistic concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- [Npgsql EF Core — PostgreSQL `xmin` concurrency token](https://www.npgsql.org/efcore/modeling/concurrency.html)
- [Npgsql EF Core — Snake case naming convention](https://www.npgsql.org/efcore/miscellaneous.html#naming-conventions)
- [EF Core 9 — `ApplyConfigurationsFromAssembly`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.modelbuilder.applyconfigurationsfromassembly)
- [EF Core 9 — `HasConversion<string>()` for enums](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions)
- [EF Core — Cascade delete behaviours (`DeleteBehavior.Restrict`)](https://learn.microsoft.com/en-us/ef/core/saving/cascade-delete)

## Build Commands

```bash
# Add Npgsql EF Core provider to Infrastructure project (if not present)
cd server
dotnet add src/PropelIQ.Infrastructure/PropelIQ.Infrastructure.csproj \
  package Npgsql.EntityFrameworkCore.PostgreSQL --version 9.*

# Build Infrastructure project to verify fluent configurations compile
dotnet build src/PropelIQ.Infrastructure/PropelIQ.Infrastructure.csproj

# Build full solution
dotnet build PropelIQ.sln

# Verify DI registration (API builds successfully)
dotnet build src/PropelIQ.Api/PropelIQ.Api.csproj
```

## Implementation Validation Strategy

- [ ] `dotnet build` on full solution passes with zero errors after configurations are applied
- [ ] `AppDbContext` resolves from DI at runtime (confirmed via integration test or `dotnet run` startup)
- [ ] `SaveChangesAsync` soft-delete override: calling `dbContext.Patients.Remove(patient)` does not issue a SQL `DELETE`; it issues an `UPDATE` setting `status = 'Deactivated'`
- [ ] Concurrent update test: two `AppDbContext` instances load the same `Appointment`; one saves first; the second throws `DbUpdateConcurrencyException` (xmin mismatch)
- [ ] All enum values stored as human-readable strings in DB (confirmed via psql query: `SELECT status FROM appointments LIMIT 1;` returns `'Booked'`, not `0`)
- [ ] Unique email index verified: inserting two `Patient` rows with the same email raises `PostgresException` with code `23505` (unique_violation)

## Implementation Checklist

- [ ] Create `SpecialtyConfiguration` — table `specialties`, column constraints, `HasData` seed entries
- [ ] Create `PatientConfiguration` — column constraints, unique `ix_patients_email` index, soft-delete `HasQueryFilter`
- [ ] Create `UserConfiguration` — column constraints, unique `ix_users_email` index, enum-to-string conversion
- [ ] Create `AppointmentConfiguration` — FK to `patients` and `specialties` with `DeleteBehavior.Restrict`, `xmin` concurrency token, composite `ix_appointments_slot_lookup` index, soft-delete `HasQueryFilter`
- [ ] Create `WaitlistEntryConfiguration` — FKs to `patients` and `appointments`, `ix_waitlist_enrolled_at` index for FIFO ordering
- [ ] Create `AppDbContext` — 5 `DbSet<T>` properties, `ApplyConfigurationsFromAssembly`, `UseSnakeCaseNamingConvention`, soft-delete `SaveChangesAsync` override
- [ ] Register `AppDbContext` in `Program.cs` using `DATABASE_URL` from `IConfiguration`
- [ ] Run `dotnet build PropelIQ.sln` — zero errors; run integration smoke (soft-delete + concurrency) manually or via xUnit test
