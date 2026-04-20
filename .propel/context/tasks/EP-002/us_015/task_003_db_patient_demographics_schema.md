# Task - task_003_db_patient_demographics_schema

## Requirement Reference

- **User Story:** us_015 — Patient Profile View & Structured Demographic Edit
- **Story Location:** `.propel/context/tasks/EP-002/us_015/us_015.md`
- **Acceptance Criteria:**
  - AC-1: `patients` table stores all demographic fields: legal name, date of birth, biological sex, email, phone, address (structured), insurance details, and emergency contact
  - AC-2: Non-locked fields can be updated independently; locked fields (name, dateOfBirth, biologicalSex) are immutable from the application layer (no DB-level constraint needed — enforced by `UpdatePatientProfileCommandHandler`)
  - AC-4: `phone` column accepts nullable values and stores PHI-encrypted ciphertext (AES-256-GCM via `IPhiEncryptionService`)
- **Edge Cases:**
  - Optimistic concurrency: `xmin` PostgreSQL system column used as the concurrency token — no additional column required; EF Core Npgsql extension exposes it automatically via `UseXminAsConcurrencyToken()`

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

| Layer      | Technology                | Version |
| ---------- | ------------------------- | ------- |
| Database   | PostgreSQL                | 16+     |
| ORM        | Entity Framework Core     | 9.x     |
| EF Driver  | Npgsql.EntityFrameworkCore.PostgreSQL | 9.x |
| DB Hosting | Neon PostgreSQL (free tier) | —     |
| Testing    | xUnit                     | 2.x     |
| AI/ML      | N/A                       | N/A     |
| Mobile     | N/A                       | N/A     |

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

Extend the `patients` table (created by US_010 task_003) with additional demographic columns required by FR-007 and FR-008. The existing `Patient` entity has: `id`, `name`, `email`, `phone`, `dateOfBirth`, `passwordHash`, `emailVerified`, `status`, `createdAt`. This migration adds the missing structured fields as follows:

- `biological_sex` — `VARCHAR(20) NULL`
- `address` — `JSONB NULL` (structured: street, city, state, postalCode, country)
- `emergency_contact` — `JSONB NULL` (name, phone, relationship)
- `communication_preferences` — `JSONB NULL` (emailOptIn, smsOptIn, preferredLanguage)
- `insurer_name` — `VARCHAR(200) NULL` (PHI, encrypted at app layer)
- `member_id` — `VARCHAR(200) NULL` (PHI, encrypted at app layer)
- `group_number` — `VARCHAR(200) NULL`

All new columns are nullable (existing patients created via registration have no demographic profile yet). The migration is zero-downtime (additive only — no existing column changes). The `xmin` concurrency token is a PostgreSQL system column — no migration step required; EF Core configuration only.

---

## Dependent Tasks

- **US_010 task_003_db_patient_schema** (EP-001) — `patients` table must already exist; this migration runs after it
- **US_014 task_002_be_argon2_phi_encryption_service** (EP-001) — `IPhiEncryptionService` value converters in `PatientConfiguration` must be updated to cover the new `insurerName` and `memberId` PHI columns

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| MODIFY | `Patient` EF Core entity | `Server/Infrastructure/Persistence/Entities/Patient.cs` |
| MODIFY | `PatientConfiguration.cs` (EF Core fluent config) | Add JSONB mappings, `HasConversion` for new PHI columns, `UseXminAsConcurrencyToken` |
| CREATE | EF Core migration: `ExtendPatientDemographics` | `Server/Infrastructure/Migrations/` |
| CREATE | `Server/Infrastructure/Migrations/<timestamp>_ExtendPatientDemographics.Designer.cs` | EF Core snapshot |

---

## Implementation Plan

1. **Extend `Patient` entity** with new properties:

   ```csharp
   public string? BiologicalSex { get; set; }          // VARCHAR(20) NULL
   public AddressValue? Address { get; set; }           // JSONB NULL
   public EmergencyContactValue? EmergencyContact { get; set; }  // JSONB NULL
   public CommunicationPreferencesValue? CommunicationPreferences { get; set; }  // JSONB NULL
   public string? InsurerName { get; set; }             // VARCHAR(200) NULL – PHI encrypted
   public string? MemberId { get; set; }                // VARCHAR(200) NULL – PHI encrypted
   public string? GroupNumber { get; set; }             // VARCHAR(200) NULL
   public uint RowVersion { get; set; }                 // xmin system column (Npgsql)
   ```

   Value objects for JSONB columns (owned types or simple classes serialised as JSONB via `ToJson()`):

   ```csharp
   public record AddressValue(string? Street, string? City, string? State, string? PostalCode, string? Country);
   public record EmergencyContactValue(string? Name, string? Phone, string? Relationship);
   public record CommunicationPreferencesValue(bool EmailOptIn, bool SmsOptIn, string? PreferredLanguage);
   ```

2. **Update `PatientConfiguration.cs`**:

   ```csharp
   // xmin-based optimistic concurrency (Npgsql built-in)
   builder.UseXminAsConcurrencyToken();

   // JSONB columns (EF Core 9 owned JSON navigation)
   builder.OwnsOne(p => p.Address, a => a.ToJson());
   builder.OwnsOne(p => p.EmergencyContact, e => e.ToJson());
   builder.OwnsOne(p => p.CommunicationPreferences, c => c.ToJson());

   // PHI encrypted columns (reuses IPhiEncryptionService value converters)
   builder.Property(p => p.InsurerName)
       .HasMaxLength(500)
       .HasConversion(
           v => v == null ? null : _phi.Encrypt(v),
           v => v == null ? null : _phi.Decrypt(v));

   builder.Property(p => p.MemberId)
       .HasMaxLength(500)
       .HasConversion(
           v => v == null ? null : _phi.Encrypt(v),
           v => v == null ? null : _phi.Decrypt(v));

   builder.Property(p => p.BiologicalSex).HasMaxLength(20);
   builder.Property(p => p.GroupNumber).HasMaxLength(200);
   ```

3. **EF Core migration** (`ExtendPatientDemographics`):
   - `Up()`:
     ```sql
     ALTER TABLE patients ADD COLUMN biological_sex VARCHAR(20) NULL;
     ALTER TABLE patients ADD COLUMN address JSONB NULL;
     ALTER TABLE patients ADD COLUMN emergency_contact JSONB NULL;
     ALTER TABLE patients ADD COLUMN communication_preferences JSONB NULL;
     ALTER TABLE patients ADD COLUMN insurer_name VARCHAR(500) NULL;
     ALTER TABLE patients ADD COLUMN member_id VARCHAR(500) NULL;
     ALTER TABLE patients ADD COLUMN group_number VARCHAR(200) NULL;
     ```
   - `Down()`: `ALTER TABLE patients DROP COLUMN` in reverse order
   - No index additions required for Phase 1 (columns are not query filter candidates at this stage)
   - Zero-downtime: all `ADD COLUMN ... NULL` operations are non-blocking in PostgreSQL 16

4. **`xmin` row version** — exposed automatically by Npgsql when `UseXminAsConcurrencyToken()` is called. No DDL required. EF Core maps `xmin` as a shadow property of type `uint` and uses it in `UPDATE ... WHERE xmin = @p_xmin` statements automatically.

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

| Action | File Path | Description |
| ------ | --------- | ----------- |
| MODIFY | `Server/Infrastructure/Persistence/Entities/Patient.cs` | Add new nullable properties: BiologicalSex, Address, EmergencyContact, CommunicationPreferences, InsurerName, MemberId, GroupNumber, RowVersion (xmin) |
| CREATE | `Server/Infrastructure/Persistence/ValueObjects/AddressValue.cs` | Owned JSON value object |
| CREATE | `Server/Infrastructure/Persistence/ValueObjects/EmergencyContactValue.cs` | Owned JSON value object |
| CREATE | `Server/Infrastructure/Persistence/ValueObjects/CommunicationPreferencesValue.cs` | Owned JSON value object |
| MODIFY | `Server/Infrastructure/Persistence/Configurations/PatientConfiguration.cs` | Add `UseXminAsConcurrencyToken()`, `OwnsOne(...).ToJson()` for JSONB columns, `HasConversion` for InsurerName + MemberId |
| CREATE | `Server/Infrastructure/Migrations/<timestamp>_ExtendPatientDemographics.cs` | Migration Up() + Down() |
| CREATE | `Server/Infrastructure/Migrations/<timestamp>_ExtendPatientDemographics.Designer.cs` | Migration snapshot |

---

## External References

- [Npgsql EF Core — xmin Concurrency Token](https://www.npgsql.org/efcore/modeling/concurrency.html)
- [EF Core 9 — Owned JSON Entities (ToJson)](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0/whatsnew#json-in-complex-types)
- [EF Core — Value Converters](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions)
- [PostgreSQL 16 — ADD COLUMN non-blocking](https://www.postgresql.org/docs/16/sql-altertable.html)
- [PostgreSQL — JSONB Type](https://www.postgresql.org/docs/16/datatype-json.html)
- [Entity Framework Core 9 — Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli)

---

## Build Commands

```bash
# Add migration
dotnet ef migrations add ExtendPatientDemographics --project Server/Server.csproj --output-dir Infrastructure/Migrations

# Apply migration
dotnet ef database update --project Server/Server.csproj

# Rollback migration
dotnet ef database update CreatePatientAndAuthTables --project Server/Server.csproj

# Generate SQL script for review
dotnet ef migrations script --project Server/Server.csproj --output migrations_patient_demographics.sql
```

---

## Implementation Validation Strategy

- [ ] Migration `Up()` executes without error on existing Neon PostgreSQL 16 database (after US_010 migration)
- [ ] Migration `Down()` rolls back all 7 new columns without affecting existing columns
- [ ] All new columns are nullable — existing Patient rows remain valid after migration (no constraint violations)
- [ ] `address` JSONB column accepts and returns structured address object without data loss (round-trip test via EF Core)
- [ ] `insurer_name` and `member_id` columns store ciphertext (not plaintext) after `Patient.InsurerName = "Aetna"` insert
- [ ] `UseXminAsConcurrencyToken()` causes EF Core to include `WHERE xmin = @p` in UPDATE statements (verified via EF Core logging)
- [ ] EF Core `AppDbContext` resolves new `Patient` properties without error at startup
- [ ] Integration tests pass (if applicable)

---

## Implementation Checklist

- [ ] Add `BiologicalSex`, `Address`, `EmergencyContact`, `CommunicationPreferences`, `InsurerName`, `MemberId`, `GroupNumber`, `RowVersion` properties to `Patient` entity
- [ ] Create `AddressValue`, `EmergencyContactValue`, `CommunicationPreferencesValue` record types as owned JSON value objects
- [ ] Update `PatientConfiguration`: call `UseXminAsConcurrencyToken()`, map JSONB owned entities with `OwnsOne(...).ToJson()`, add `HasConversion` for `InsurerName` and `MemberId` PHI fields
- [ ] Write EF Core migration `ExtendPatientDemographics` with `Up()` (7x `ALTER TABLE ADD COLUMN NULL`) and `Down()` (7x `DROP COLUMN`)
- [ ] Confirm migration does NOT modify any existing column or table (additive only)
- [ ] Generate SQL script and review before applying to Neon PostgreSQL
