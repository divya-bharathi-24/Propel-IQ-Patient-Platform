# Task - TASK_003

## Requirement Reference

- **User Story**: US_019 — End-to-End Single-Session Appointment Booking Workflow
- **Story Location**: `.propel/context/tasks/EP-003-I/us_019/us_019.md`
- **Acceptance Criteria**:
  - AC-2: Given I submit the booking, When the system commits the appointment, Then an Appointment record is created with `status = Booked`, an InsuranceValidation record is stored, and a WaitlistEntry is created if I designated a preferred slot
  - AC-3: Given two patients submit bookings for the same slot at the exact same moment, Then exactly one succeeds and the other receives an HTTP 409 Conflict (enforced at DB level by unique partial index — FR-013)
- **Edge Cases**:
  - Concurrent booking of same slot: unique partial index on `appointments(specialty_id, date, time_slot_start)` WHERE status NOT IN (`Cancelled`) guarantees database-level exclusivity; second `INSERT` raises constraint violation surfaced as `DbUpdateException`

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
| ORM | Entity Framework Core | 9.x |
| Database | PostgreSQL | 16+ |
| Backend | ASP.NET Core Web API | .net 10 |
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

Create the EF Core 9 migration that:

1. **Creates the `insurance_validations` table** — stores one record per booking attempt with the insurance pre-check result. Linked to both `patients` and `appointments` via cascading FK. Indexed on `(patient_id, validated_at DESC)` to support the patient dashboard query (AC-2, FR-038 surface area).

2. **Creates the `dummy_insurers` seed table** — an internal lookup table of dummy insurer records used by `InsuranceSoftCheckService` for soft-matching during the booking flow (FR-038). Seeded with 5 sample records via `HasData()`.

3. **Adds a unique partial index on `appointments(specialty_id, date, time_slot_start)` WHERE `status NOT IN ('Cancelled')`** — the primary database-level enforcement mechanism for FR-013. A second concurrent `INSERT` to the same slot raises a PostgreSQL unique constraint violation, surfaced as `DbUpdateException` in EF Core and mapped to 409 Conflict by TASK_002.

> **Scope boundary**: `appointments` and `waitlist_entries` base tables were created in US_006 (EP-DATA). This migration is additive only (zero-downtime per DR-013). The `InsuranceValidation` entity was not included in US_006 scope.

## Dependent Tasks

- **US_006 (EP-DATA)** — `patients`, `appointments`, and `waitlist_entries` tables must exist before this additive migration runs.
- **US_009** — `dummy_insurers` seed data is the foundation for insurance soft-check matching referenced in US_022.

## Impacted Components

| Component | Status | Location |
|-----------|--------|----------|
| `InsuranceValidation` (EF Core entity) | NEW | `Server/Domain/InsuranceValidation.cs` |
| `InsuranceValidationConfiguration` | NEW | `Server/Infrastructure/Persistence/Configurations/InsuranceValidationConfiguration.cs` |
| `DummyInsurer` (EF Core entity) | NEW | `Server/Domain/DummyInsurer.cs` |
| `DummyInsurerConfiguration` | NEW | `Server/Infrastructure/Persistence/Configurations/DummyInsurerConfiguration.cs` |
| `AppDbContext` | MODIFY | Add `DbSet<InsuranceValidation>` and `DbSet<DummyInsurer>` |
| `<timestamp>_AddInsuranceValidationAndBookingConstraint.cs` | NEW | `Server/Infrastructure/Persistence/Migrations/` |

## Implementation Plan

1. **`InsuranceValidation` entity class**:

   ```csharp
   public class InsuranceValidation
   {
       public Guid Id { get; set; }
       public Guid PatientId { get; set; }
       public Guid? AppointmentId { get; set; }       // nullable — created alongside Appointment in same transaction
       public string? ProviderName { get; set; }
       public string? InsuranceId { get; set; }
       public InsuranceValidationResult Result { get; set; }  // Verified|NotRecognized|Incomplete|CheckPending
       public DateTimeOffset ValidatedAt { get; set; }

       // Navigation
       public Patient Patient { get; set; } = null!;
       public Appointment? Appointment { get; set; }
   }

   public enum InsuranceValidationResult { Verified, NotRecognized, Incomplete, CheckPending }
   ```

2. **`InsuranceValidationConfiguration`** (`IEntityTypeConfiguration<InsuranceValidation>`):

   ```csharp
   builder.ToTable("insurance_validations");
   builder.HasKey(x => x.Id);
   builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
   builder.Property(x => x.ProviderName).HasMaxLength(200);
   builder.Property(x => x.InsuranceId).HasMaxLength(100);
   builder.Property(x => x.Result).HasConversion<string>().HasMaxLength(50).IsRequired();
   builder.Property(x => x.ValidatedAt).HasColumnType("timestamptz").IsRequired();

   builder.HasOne(x => x.Patient)
          .WithMany()
          .HasForeignKey(x => x.PatientId)
          .OnDelete(DeleteBehavior.Cascade);

   builder.HasOne(x => x.Appointment)
          .WithMany()
          .HasForeignKey(x => x.AppointmentId)
          .OnDelete(DeleteBehavior.Cascade)
          .IsRequired(false);

   // Composite index for patient dashboard query (AC-2, US_016 integration)
   builder.HasIndex(x => new { x.PatientId, x.ValidatedAt })
          .HasDatabaseName("IX_insurance_validations_patient_id_validated_at")
          .IsDescending(false, true);
   ```

3. **`DummyInsurer` entity class**:

   ```csharp
   public class DummyInsurer
   {
       public Guid Id { get; set; }
       public string InsurerName { get; set; } = string.Empty;    // display name
       public string MemberIdPrefix { get; set; } = string.Empty; // prefix that qualifies as a match
       public bool IsActive { get; set; }
   }
   ```

4. **`DummyInsurerConfiguration`** — `HasData()` seed with 5 sample insurer records:

   ```csharp
   builder.ToTable("dummy_insurers");
   builder.HasKey(x => x.Id);
   builder.Property(x => x.InsurerName).HasMaxLength(200).IsRequired();
   builder.Property(x => x.MemberIdPrefix).HasMaxLength(20).IsRequired();

   builder.HasData(
       new DummyInsurer { Id = Guid.Parse("..."), InsurerName = "BlueCross Shield",   MemberIdPrefix = "BCS",  IsActive = true },
       new DummyInsurer { Id = Guid.Parse("..."), InsurerName = "Aetna Health",        MemberIdPrefix = "AET",  IsActive = true },
       new DummyInsurer { Id = Guid.Parse("..."), InsurerName = "United HealthGroup",  MemberIdPrefix = "UHG",  IsActive = true },
       new DummyInsurer { Id = Guid.Parse("..."), InsurerName = "Cigna Medical",       MemberIdPrefix = "CGN",  IsActive = true },
       new DummyInsurer { Id = Guid.Parse("..."), InsurerName = "Humana Plus",         MemberIdPrefix = "HMN",  IsActive = true }
   );
   ```

   > **Note**: Replace `Guid.Parse("...")` placeholders with deterministic, pre-generated GUIDs before running the migration.

5. **`AppDbContext` update**:

   ```csharp
   public DbSet<InsuranceValidation> InsuranceValidations => Set<InsuranceValidation>();
   public DbSet<DummyInsurer> DummyInsurers => Set<DummyInsurer>();
   ```

   Apply both configurations in `OnModelCreating` via `modelBuilder.ApplyConfiguration(new InsuranceValidationConfiguration())` and `modelBuilder.ApplyConfiguration(new DummyInsurerConfiguration())`.

6. **Migration `Up()` / `Down()`**:

   **`Up()`**:
   ```sql
   -- Create insurance_validations table
   CREATE TABLE insurance_validations (
       id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
       patient_id UUID NOT NULL REFERENCES patients(id) ON DELETE CASCADE,
       appointment_id UUID REFERENCES appointments(id) ON DELETE CASCADE,
       provider_name VARCHAR(200),
       insurance_id VARCHAR(100),
       result VARCHAR(50) NOT NULL,
       validated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
   );
   CREATE INDEX IX_insurance_validations_patient_id_validated_at
       ON insurance_validations (patient_id, validated_at DESC);

   -- Create dummy_insurers seed table
   CREATE TABLE dummy_insurers (
       id UUID PRIMARY KEY,
       insurer_name VARCHAR(200) NOT NULL,
       member_id_prefix VARCHAR(20) NOT NULL,
       is_active BOOLEAN NOT NULL DEFAULT TRUE
   );
   -- HasData seeding handled by EF Core migration scaffolding

   -- Add unique partial index on appointments for FR-013 concurrency constraint
   CREATE UNIQUE INDEX IX_appointments_slot_uniqueness
       ON appointments (specialty_id, date, time_slot_start)
       WHERE status NOT IN ('Cancelled');
   ```

   **`Down()`** (reverse order — drop index before table):
   ```sql
   DROP INDEX IF EXISTS IX_appointments_slot_uniqueness;
   DROP TABLE IF EXISTS dummy_insurers;
   DROP INDEX IF EXISTS IX_insurance_validations_patient_id_validated_at;
   DROP TABLE IF EXISTS insurance_validations;
   ```

## Current Project State

```
Server/
├── Domain/
│   ├── Patient.cs                   (US_006 — completed)
│   ├── Appointment.cs               (US_006 — completed)
│   ├── WaitlistEntry.cs             (US_006 — completed)
│   ├── InsuranceValidation.cs       ← NEW
│   └── DummyInsurer.cs              ← NEW
├── Infrastructure/
│   └── Persistence/
│       ├── AppDbContext.cs          (MODIFY — add new DbSets)
│       ├── Configurations/
│       │   ├── AppointmentConfiguration.cs          (US_006)
│       │   ├── InsuranceValidationConfiguration.cs  ← NEW
│       │   └── DummyInsurerConfiguration.cs         ← NEW
│       └── Migrations/
│           └── <timestamp>_AddInsuranceValidationAndBookingConstraint.cs  ← NEW
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Domain/InsuranceValidation.cs` | Entity class: `Id`, `PatientId`, `AppointmentId`, `ProviderName`, `InsuranceId`, `Result` (enum), `ValidatedAt` (timestamptz) |
| CREATE | `Server/Domain/DummyInsurer.cs` | Seed lookup entity: `Id`, `InsurerName`, `MemberIdPrefix`, `IsActive` |
| CREATE | `Server/Infrastructure/Persistence/Configurations/InsuranceValidationConfiguration.cs` | FK cascade config; composite index on `(patient_id, validated_at DESC)` |
| CREATE | `Server/Infrastructure/Persistence/Configurations/DummyInsurerConfiguration.cs` | `HasData()` with 5 pre-seeded dummy insurer records |
| CREATE | `Server/Infrastructure/Persistence/Migrations/<timestamp>_AddInsuranceValidationAndBookingConstraint.cs` | EF Core migration: `Up()` creates tables and unique partial index; `Down()` reverses in dependency order |
| MODIFY | `Server/Infrastructure/Persistence/AppDbContext.cs` | Add `DbSet<InsuranceValidation>` and `DbSet<DummyInsurer>`; apply both new configurations in `OnModelCreating` |

> **Note on migration file name**: `<timestamp>` is generated by the EF Core tooling at `dotnet ef migrations add` execution time. It is intentionally left as a placeholder here per EF Core tooling conventions and is not resolvable at task-authoring time.

## External References

- [EF Core — IEntityTypeConfiguration](https://learn.microsoft.com/en-us/ef/core/modeling/#grouping-configuration)
- [EF Core — HasData seeding](https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding)
- [PostgreSQL — Partial indexes](https://www.postgresql.org/docs/current/indexes-partial.html)
- [EF Core — HasFilter() for partial indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes#index-filter)
- [DR-013 — Zero-downtime migrations (additive only)](design.md#DR-013)
- [FR-013 — Double-booking prevention](spec.md#FR-013)
- [FR-038 — Soft insurance pre-check against dummy records](spec.md#FR-038)

## Build Commands

- Refer to: `.propel/build/backend-build.md`
- Migration generation: `dotnet ef migrations add AddInsuranceValidationAndBookingConstraint --project Server/Infrastructure --startup-project Server`
- Migration apply: `dotnet ef database update --project Server/Infrastructure --startup-project Server`

## Implementation Validation Strategy

- [ ] `dotnet ef migrations add` generates `Up()` and `Down()` without errors
- [ ] `dotnet ef database update` applies migration to local PostgreSQL without errors
- [ ] `insurance_validations` table exists with correct columns, types, and FKs after migration
- [ ] `dummy_insurers` table contains exactly 5 seeded rows after migration
- [ ] `IX_appointments_slot_uniqueness` partial index exists; verified with `\d appointments` in psql
- [ ] Attempting two concurrent INSERTs for the same `(specialty_id, date, time_slot_start)` with status=Booked raises a unique constraint violation on second insert
- [ ] `Down()` migration drops index first, then tables, without FK violation errors

## Implementation Checklist

- [ ] Create `InsuranceValidation` entity class with `Id`, `PatientId` (FK → patients, CASCADE), `AppointmentId` (FK → appointments, CASCADE, nullable), `ProviderName VARCHAR(200) NULL`, `InsuranceId VARCHAR(100) NULL`, `Result VARCHAR(50) NOT NULL` (`InsuranceValidationResult` enum), `ValidatedAt TIMESTAMPTZ NOT NULL`
- [ ] Create `InsuranceValidationConfiguration`: FK cascade configuration; composite index `IX_insurance_validations_patient_id_validated_at` on `(patient_id, validated_at DESC)`
- [ ] Create `DummyInsurer` entity + `DummyInsurerConfiguration` with `HasData()` seeding 5 sample insurer records (BlueCross Shield, Aetna Health, United HealthGroup, Cigna Medical, Humana Plus) using pre-generated deterministic GUIDs
- [ ] Modify `AppDbContext`: add `DbSet<InsuranceValidation>` and `DbSet<DummyInsurer>`; apply new configurations in `OnModelCreating`
- [ ] Add unique partial index `IX_appointments_slot_uniqueness` on `appointments(specialty_id, date, time_slot_start)` WHERE `status NOT IN ('Cancelled')` using `HasIndex().HasFilter().IsUnique()` — enforces FR-013 concurrency constraint at database level
- [ ] Generate EF Core migration `<timestamp>_AddInsuranceValidationAndBookingConstraint.cs`; verify `Up()` creates tables and index; `Down()` drops index first, then tables (zero-downtime, DR-013)
