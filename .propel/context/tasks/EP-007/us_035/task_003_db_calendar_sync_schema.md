# Task - task_003_db_calendar_sync_schema

## Requirement Reference

- **User Story:** us_035 — Google Calendar OAuth 2.0 Appointment Sync
- **Story Location:** `.propel/context/tasks/EP-007/us_035/us_035.md`
- **Acceptance Criteria:**
  - AC-2: `CalendarSync` record stores `externalEventId` and a `eventLink` to the Google Calendar event (shown to patient)
  - AC-4: `CalendarSync` record stores `retryScheduledAt` (10 minutes from failure) for the `CalendarSyncRetryBackgroundService`; `retryCount` tracks how many retry attempts have been made
- **Edge Cases:**
  - Token expiry: `PatientOAuthToken` table stores encrypted `accessToken` and `refreshToken` with `expiresAt`; single row per `(patientId, provider)` — upsert semantics
  - Permanent failure after 3 retries: `syncStatus = 'PermanentFailed'` must be storable — enum value must be present in the migration

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

| Layer      | Technology                               | Version |
| ---------- | ---------------------------------------- | ------- |
| Database   | PostgreSQL                               | 16+     |
| ORM        | Entity Framework Core                    | 9.x     |
| EF Driver  | Npgsql.EntityFrameworkCore.PostgreSQL    | 9.x     |
| DB Hosting | Neon PostgreSQL (free tier)              | —       |
| Testing    | xUnit                                    | 2.x     |
| AI/ML      | N/A                                      | N/A     |
| Mobile     | N/A                                      | N/A     |

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

Two schema changes are required to support US_035:

1. **New table `patient_oauth_tokens`** — stores encrypted Google (and future Outlook) OAuth tokens per patient per provider. Tokens are encrypted at rest using ASP.NET Core Data Protection API (AES-256, NFR-004). One row per `(patientId, provider)` — upsert on re-authorization.

2. **Extend `calendar_syncs` table** — the foundational US_008 migration created the base `CalendarSync` entity; this migration adds:
   - `event_link VARCHAR(2048) NULL` — Google Calendar event URL (shown to patient, AC-2)
   - `retry_scheduled_at TIMESTAMPTZ NULL` — when the `CalendarSyncRetryBackgroundService` should next attempt (AC-4)
   - `retry_count SMALLINT NOT NULL DEFAULT 0` — tracks retry attempts; service marks `PermanentFailed` after 3
   - `sync_status` expansion to include `PermanentFailed` value (if stored as VARCHAR — add to CHECK constraint or enum)

All new columns are additive / nullable where appropriate — zero-downtime migration on PostgreSQL 16.

---

## Dependent Tasks

- **US_008 (Foundational)** — `calendar_syncs` table must exist before this migration runs

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `PatientOAuthToken` EF Core entity | `Server/Infrastructure/Persistence/Entities/PatientOAuthToken.cs` |
| CREATE | `PatientOAuthTokenConfiguration` EF Core fluent config | `Server/Infrastructure/Persistence/Configurations/PatientOAuthTokenConfiguration.cs` |
| MODIFY | `CalendarSync` EF Core entity | Add `EventLink: string?`, `RetryScheduledAt: DateTime?`, `RetryCount: int` |
| MODIFY | `CalendarSyncConfiguration` EF Core fluent config | Map new columns; add `PermanentFailed` to `syncStatus` valid values |
| CREATE | EF Core migration: `AddCalendarSyncOAuthSchema` | `Server/Infrastructure/Migrations/` |
| CREATE | Migration Designer snapshot | `Server/Infrastructure/Migrations/<timestamp>_AddCalendarSyncOAuthSchema.Designer.cs` |

---

## Implementation Plan

1. **`PatientOAuthToken` entity**:
   ```csharp
   public class PatientOAuthToken
   {
       public Guid Id { get; set; }
       public Guid PatientId { get; set; }
       public string Provider { get; set; } = "Google";         // "Google" | "Outlook"
       public string EncryptedAccessToken { get; set; } = "";   // IDataProtector.Protect(accessToken)
       public string EncryptedRefreshToken { get; set; } = "";  // IDataProtector.Protect(refreshToken)
       public DateTime ExpiresAt { get; set; }                  // access token expiry (UTC)
       public DateTime CreatedAt { get; set; }
       public DateTime UpdatedAt { get; set; }

       // Navigation
       public Patient Patient { get; set; } = null!;
   }
   ```

2. **`PatientOAuthTokenConfiguration`** (EF Core fluent config):
   ```csharp
   builder.ToTable("patient_oauth_tokens");
   builder.HasKey(t => t.Id);
   builder.Property(t => t.Provider).HasMaxLength(20).IsRequired();
   builder.Property(t => t.EncryptedAccessToken).IsRequired();    // no max length — Data Protection output is variable
   builder.Property(t => t.EncryptedRefreshToken).IsRequired();
   builder.Property(t => t.ExpiresAt).IsRequired();
   // Unique index: one token row per (patientId, provider) — upsert semantics
   builder.HasIndex(t => new { t.PatientId, t.Provider }).IsUnique();
   builder.HasOne(t => t.Patient).WithMany().HasForeignKey(t => t.PatientId).OnDelete(DeleteBehavior.Cascade);
   ```
   - `OnDelete(Cascade)`: when a Patient is deactivated/deleted, OAuth tokens are removed automatically (HIPAA — no orphaned PHI-adjacent tokens)

3. **Extend `CalendarSync` entity**:
   ```csharp
   public string? EventLink { get; set; }          // Google Calendar event URL (AC-2)
   public DateTime? RetryScheduledAt { get; set; } // When retry service should reattempt (AC-4)
   public int RetryCount { get; set; } = 0;        // Retry attempts; PermanentFailed after 3
   ```
   - `syncStatus` VARCHAR values extended to include `'PermanentFailed'` (document in migration comment; validated at application layer)

4. **Update `CalendarSyncConfiguration`**:
   ```csharp
   builder.Property(c => c.EventLink).HasMaxLength(2048).IsRequired(false);
   builder.Property(c => c.RetryScheduledAt).IsRequired(false);
   builder.Property(c => c.RetryCount).HasDefaultValue(0).IsRequired();
   ```

5. **EF Core migration `AddCalendarSyncOAuthSchema`**:
   - `Up()`:
     ```sql
     CREATE TABLE patient_oauth_tokens (
         id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
         patient_id UUID NOT NULL REFERENCES patients(id) ON DELETE CASCADE,
         provider VARCHAR(20) NOT NULL,
         encrypted_access_token TEXT NOT NULL,
         encrypted_refresh_token TEXT NOT NULL,
         expires_at TIMESTAMPTZ NOT NULL,
         created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
         updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
     );
     CREATE UNIQUE INDEX idx_patient_oauth_tokens_patient_provider
         ON patient_oauth_tokens (patient_id, provider);

     ALTER TABLE calendar_syncs ADD COLUMN event_link VARCHAR(2048) NULL;
     ALTER TABLE calendar_syncs ADD COLUMN retry_scheduled_at TIMESTAMPTZ NULL;
     ALTER TABLE calendar_syncs ADD COLUMN retry_count SMALLINT NOT NULL DEFAULT 0;
     ```
   - `Down()`:
     ```sql
     ALTER TABLE calendar_syncs DROP COLUMN retry_count;
     ALTER TABLE calendar_syncs DROP COLUMN retry_scheduled_at;
     ALTER TABLE calendar_syncs DROP COLUMN event_link;
     DROP TABLE IF EXISTS patient_oauth_tokens;
     ```
   - `gen_random_uuid()` requires `pgcrypto` extension (assumed already enabled from prior foundational migrations)

6. **Security note**: `encrypted_access_token` and `encrypted_refresh_token` columns store ASP.NET Core Data Protection ciphertext (AES-256-GCM). The column type is `TEXT` (not `BYTEA`) because the Data Protection API outputs Base64-encoded strings. The decryption key ring is persisted to Redis in production (already provisioned). Do NOT store plaintext tokens in any column.

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
| CREATE | `Server/Infrastructure/Persistence/Entities/PatientOAuthToken.cs` | Entity: patientId, provider, encryptedAccessToken, encryptedRefreshToken, expiresAt, timestamps |
| CREATE | `Server/Infrastructure/Persistence/Configurations/PatientOAuthTokenConfiguration.cs` | Unique index (patientId, provider); Cascade delete; max lengths |
| MODIFY | `Server/Infrastructure/Persistence/Entities/CalendarSync.cs` | Add `EventLink: string?`, `RetryScheduledAt: DateTime?`, `RetryCount: int` |
| MODIFY | `Server/Infrastructure/Persistence/Configurations/CalendarSyncConfiguration.cs` | Map new columns; max length 2048 on `EventLink`; default 0 on `RetryCount` |
| CREATE | `Server/Infrastructure/Migrations/<timestamp>_AddCalendarSyncOAuthSchema.cs` | `Up()`: CREATE `patient_oauth_tokens`, ADD 3 columns to `calendar_syncs`; `Down()`: reversal |
| CREATE | `Server/Infrastructure/Migrations/<timestamp>_AddCalendarSyncOAuthSchema.Designer.cs` | EF Core migration snapshot |

---

## External References

- [PostgreSQL 16 — CREATE TABLE with UUID primary key (gen_random_uuid)](https://www.postgresql.org/docs/16/functions-uuid.html)
- [PostgreSQL 16 — ADD COLUMN (non-blocking on free-tier instance)](https://www.postgresql.org/docs/16/sql-altertable.html)
- [EF Core 9 — Unique index (HasIndex().IsUnique())](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)
- [EF Core 9 — OnDelete Cascade for FK](https://learn.microsoft.com/en-us/ef/core/modeling/relationships/navigations#cascade-delete)
- [ASP.NET Core Data Protection — Key storage in Redis (production)](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers#redis)
- [NFR-004 — AES-256 encryption at rest (design.md line 41)](design.md)
- [DR-017 — CalendarSync: provider, externalEventId, syncStatus, patient reference (design.md line 81)](design.md)
- [OWASP A02 — Cryptographic Failures: encrypted tokens at rest, never plaintext](https://owasp.org/Top10/A02_2021-Cryptographic_Failures/)

---

## Build Commands

```bash
# Add migration
dotnet ef migrations add AddCalendarSyncOAuthSchema --project Server/Server.csproj --output-dir Infrastructure/Migrations

# Apply migration
dotnet ef database update --project Server/Server.csproj

# Generate SQL script for review before applying to Neon production database
dotnet ef migrations script --project Server/Server.csproj --output migration_calendar_sync_oauth.sql

# Rollback
dotnet ef database update <PreviousMigrationName> --project Server/Server.csproj
```

---

## Implementation Validation Strategy

- [ ] Migration `Up()` runs without error on Neon PostgreSQL 16 after all prior migrations
- [ ] `patient_oauth_tokens` table created with correct columns, unique index on `(patient_id, provider)`, and CASCADE delete constraint
- [ ] `calendar_syncs.event_link` is nullable VARCHAR(2048); `retry_scheduled_at` is nullable TIMESTAMPTZ; `retry_count` is NOT NULL with DEFAULT 0
- [ ] Inserting a `PatientOAuthToken` row with the same `(patientId, provider)` twice throws a unique constraint violation — upsert (`ON CONFLICT DO UPDATE`) correctly handled at application layer in task_002
- [ ] Deleting a `Patient` row cascades to delete their `PatientOAuthToken` rows (no orphaned encrypted tokens)
- [ ] Existing `CalendarSync` rows (from US_008) remain valid after migration; `retry_count` defaults to 0, `event_link` defaults to NULL
- [ ] EF Core `AppDbContext` resolves updated `CalendarSync` and new `PatientOAuthToken` entities without startup errors

---

## Implementation Checklist

- [ ] Create `PatientOAuthToken` entity: `Id`, `PatientId`, `Provider`, `EncryptedAccessToken` (TEXT — Data Protection ciphertext), `EncryptedRefreshToken` (TEXT), `ExpiresAt`, `CreatedAt`, `UpdatedAt`
- [ ] Create `PatientOAuthTokenConfiguration`: unique index on `(PatientId, Provider)`; `OnDelete(Cascade)`; `TEXT` columns with no max length (Data Protection output is variable-length)
- [ ] Add `EventLink: string?`, `RetryScheduledAt: DateTime?`, `RetryCount: int = 0` to `CalendarSync` entity and `CalendarSyncConfiguration`
- [ ] Write migration `AddCalendarSyncOAuthSchema` `Up()`: `CREATE TABLE patient_oauth_tokens` + unique index; `ALTER TABLE calendar_syncs ADD COLUMN` (event_link, retry_scheduled_at, retry_count)
- [ ] Generate SQL migration script and review before applying to Neon; confirm `pgcrypto` extension active for `gen_random_uuid()`
- [ ] Verify `Down()` cleanly drops new columns and `patient_oauth_tokens` table without affecting existing `calendar_syncs` rows
