# Task - task_003_db_patient_schema

## Requirement Reference

- **User Story:** us_010 — Patient Self-Registration with Email Verification
- **Story Location:** `.propel/context/tasks/EP-001/us_010/us_010.md`
- **Acceptance Criteria:**
  - AC-1: Patient record is persisted with `emailVerified = false` on initial registration
  - AC-2: Account activation updates `emailVerified = true`; AuditLog records event with UTC timestamp
  - AC-3: Email uniqueness enforced at the database level — duplicate INSERT raises a unique constraint violation
  - AC-4: Password complexity is enforced at the application layer; DB stores only the Argon2 hash value
- **Edge Cases:**
  - Verification token expires after 24 hours: enforced by `EmailVerificationToken.ExpiresAt` column comparison in application query (no DB-level TTL)
  - Second verification link click: `EmailVerificationToken.UsedAt IS NOT NULL` check at application layer

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

| Layer      | Technology                  | Version |
| ---------- | --------------------------- | ------- |
| Database   | PostgreSQL                  | 16+     |
| ORM        | Entity Framework Core       | 9.x     |
| DB Hosting | Neon PostgreSQL (free tier) | —       |
| Testing    | xUnit                       | 2.x     |
| AI/ML      | N/A                         | N/A     |
| Mobile     | N/A                         | N/A     |

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

Create the EF Core code-first migrations for the `Patient`, `EmailVerificationToken`, and `AuditLog` tables required by US_010. The `Patient` entity is the foundational domain record; `EmailVerificationToken` stores the SHA-256 hash of the one-time verification token with its expiry and used-at timestamps. `AuditLog` is an immutable, INSERT-only table (no UPDATE or DELETE permitted at the DB trigger level). Each migration includes a corresponding `Down()` rollback. Schema aligns with DR-001, DR-009, DR-010, NFR-004, NFR-008, NFR-013.

---

## Dependent Tasks

- **US_006** — Patient entity foundational dependency (this task provides the schema that US_006 depends on)

---

## Impacted Components

| Status | Component / Module                                           | Project                                                                  |
| ------ | ------------------------------------------------------------ | ------------------------------------------------------------------------ |
| CREATE | `Patient` EF Core entity + type configuration                | `Server/Infrastructure/Persistence/`                                     |
| CREATE | `EmailVerificationToken` EF Core entity + type configuration | `Server/Infrastructure/Persistence/`                                     |
| CREATE | `AuditLog` EF Core entity + type configuration (INSERT-only) | `Server/Infrastructure/Persistence/`                                     |
| CREATE | EF Core migration: `CreatePatientAndAuthTables`              | `Server/Infrastructure/Migrations/`                                      |
| MODIFY | `AppDbContext.cs`                                            | Add `DbSet<Patient>`, `DbSet<EmailVerificationToken>`, `DbSet<AuditLog>` |

---

## Implementation Plan

1. **`Patient` EF Core entity** (maps to `patients` table):

   ```csharp
   // Columns
   Id           UUID         PK DEFAULT gen_random_uuid()
   Name         VARCHAR(200) NOT NULL
   Email        VARCHAR(320) NOT NULL UNIQUE  -- case-insensitive enforced via unique index on lower(email)
   Phone        VARCHAR(20)  NULL
   DateOfBirth  DATE         NOT NULL
   PasswordHash VARCHAR(512) NOT NULL         -- Argon2 hash (NFR-008)
   EmailVerified BOOLEAN     NOT NULL DEFAULT FALSE
   Status       VARCHAR(20)  NOT NULL DEFAULT 'Active'  -- CHECK ('Active','Deactivated')
   CreatedAt    TIMESTAMPTZ  NOT NULL DEFAULT NOW()
   ```

   - Soft-delete pattern: `Status = 'Deactivated'` (DR-010); no hard DELETE
   - AES-256 column-level encryption for PHI fields (`Name`, `Phone`, `DateOfBirth`) via `pgcrypto` or application-layer encryption before persistence (NFR-004)

2. **`EmailVerificationToken` EF Core entity** (maps to `email_verification_tokens` table):

   ```
   Id          UUID         PK DEFAULT gen_random_uuid()
   PatientId   UUID         NOT NULL FK → patients(id) ON DELETE CASCADE
   TokenHash   VARCHAR(512) NOT NULL         -- SHA-256 hash of the raw token (never raw token)
   ExpiresAt   TIMESTAMPTZ  NOT NULL         -- CreatedAt + 24 hours
   UsedAt      TIMESTAMPTZ  NULL             -- SET when token is consumed
   CreatedAt   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
   ```

3. **`AuditLog` EF Core entity** (maps to `audit_logs` table, INSERT-only):

   ```
   Id          UUID         PK DEFAULT gen_random_uuid()
   UserId      UUID         NOT NULL FK → patients(id)   -- or users table when User entity is available
   Action      VARCHAR(100) NOT NULL
   EntityType  VARCHAR(100) NOT NULL
   EntityId    UUID         NOT NULL
   Details     JSONB        NULL
   IpAddress   VARCHAR(45)  NOT NULL         -- supports IPv6
   Timestamp   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
   ```

   - DB-level INSERT-only enforcement via PostgreSQL trigger that raises `EXCEPTION` on UPDATE and DELETE (AD-7)

4. **Indexes**:
   - `CREATE UNIQUE INDEX uq_patients_email_lower ON patients (lower(email))` — case-insensitive uniqueness
   - `CREATE INDEX idx_email_verification_tokens_hash ON email_verification_tokens (token_hash)` — O(1) lookup
   - `CREATE INDEX idx_email_verification_tokens_patient ON email_verification_tokens (patient_id)` — resend lookups
   - `CREATE INDEX idx_audit_logs_entity ON audit_logs (entity_type, entity_id)` — audit queries

5. **INSERT-only trigger** for `audit_logs`:

   ```sql
   CREATE OR REPLACE FUNCTION prevent_audit_log_mutation()
   RETURNS TRIGGER LANGUAGE plpgsql AS $$
   BEGIN
     RAISE EXCEPTION 'Audit log records are immutable. UPDATE and DELETE are not permitted.';
   END;
   $$;

   CREATE TRIGGER trg_audit_logs_immutable
   BEFORE UPDATE OR DELETE ON audit_logs
   FOR EACH ROW EXECUTE FUNCTION prevent_audit_log_mutation();
   ```

6. **EF Core migration** (`CreatePatientAndAuthTables`):
   - `Up()`: create `patients`, `email_verification_tokens`, `audit_logs` tables, indexes, and immutable trigger
   - `Down()`: drop trigger → drop indexes → drop tables in reverse FK dependency order
   - Migration managed by EF Core `dotnet ef migrations add CreatePatientAndAuthTables`
   - Zero-downtime compatible (new tables, no modifications to existing tables — DR-013)

---

## Current Project State

```
server/
├── Propel.Api.Gateway/
│   ├── Data/
│   │   ├── AppDbContext.cs                   ← DbSet<Patient>, DbSet<EmailVerificationToken>, DbSet<AuditLog>; ApplyConfigurationsFromAssembly
│   │   └── Configurations/
│   │       ├── PatientConfiguration.cs       ← functional unique index uq_patients_email_lower via SQL; soft-delete filter
│   │       ├── EmailVerificationTokenConfiguration.cs ← FK CASCADE, token_hash unique index, patient_id index
│   │       └── AuditLogConfiguration.cs      ← JSONB details, (entity_type, entity_id) composite index
│   └── Migrations/
│       ├── 20260420161639_Initial.cs         ← patients, users, appointments, specialties, waitlist_entries
│       ├── 20260420171127_AddClinicalEntities.cs
│       ├── 20260420190747_AddAuditNotificationEntities.cs ← audit_logs + INSERT-only trigger
│       ├── 20260420191333_AddExtensionsSeedData.cs
│       ├── 20260421033625_AddEmailVerificationTokens.cs   ← email_verification_tokens
│       ├── 20260421120000_AddCaseInsensitiveEmailIndex.cs ← uq_patients_email_lower + ix_audit_logs_entity_type_entity_id
│       └── AppDbContextModelSnapshot.cs
└── Propel.Domain/
    └── Entities/
        ├── Patient.cs
        ├── EmailVerificationToken.cs
        └── AuditLog.cs
```

---

## Expected Changes

| Action | File Path                                                                                 | Description                                                                                    |
| ------ | ----------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| CREATE | `Server/Infrastructure/Persistence/Entities/Patient.cs`                                   | EF Core entity for Patient domain object                                                       |
| CREATE | `Server/Infrastructure/Persistence/Entities/EmailVerificationToken.cs`                    | EF Core entity for verification tokens                                                         |
| CREATE | `Server/Infrastructure/Persistence/Entities/AuditLog.cs`                                  | EF Core entity for immutable audit log                                                         |
| CREATE | `Server/Infrastructure/Persistence/Configurations/PatientConfiguration.cs`                | EF Core fluent config: table name, constraints, indexes, soft-delete query filter              |
| CREATE | `Server/Infrastructure/Persistence/Configurations/EmailVerificationTokenConfiguration.cs` | EF Core fluent config: FK, indexes, token hash length                                          |
| CREATE | `Server/Infrastructure/Persistence/Configurations/AuditLogConfiguration.cs`               | EF Core fluent config: INSERT-only annotation, JSONB column, indexes                           |
| CREATE | `Server/Infrastructure/Migrations/<timestamp>_CreatePatientAndAuthTables.cs`              | EF Core migration: Up() + Down()                                                               |
| CREATE | `Server/Infrastructure/Migrations/<timestamp>_CreatePatientAndAuthTables.Designer.cs`     | EF Core migration snapshot                                                                     |
| MODIFY | `Server/Infrastructure/Persistence/AppDbContext.cs`                                       | Add `DbSet<Patient>`, `DbSet<EmailVerificationToken>`, `DbSet<AuditLog>`; apply configurations |

---

## External References

- [Entity Framework Core 9 — Code-First Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli)
- [EF Core — Fluent API Configuration](https://learn.microsoft.com/en-us/ef/core/modeling/)
- [PostgreSQL — gen_random_uuid()](https://www.postgresql.org/docs/16/functions-uuid.html)
- [PostgreSQL — Functional Indexes (lower)](https://www.postgresql.org/docs/16/indexes-expressional.html)
- [PostgreSQL — Triggers](https://www.postgresql.org/docs/16/triggers.html)
- [PostgreSQL — JSONB Column Type](https://www.postgresql.org/docs/16/datatype-json.html)
- [Neon PostgreSQL — Free Tier Connection](https://neon.tech/docs/connect/connect-from-any-app)
- [OWASP — Password Storage Cheat Sheet (Argon2)](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)
- [design.md Domain Entities — Patient](../.propel/context/docs/design.md#domain-entities)

---

## Build Commands

```bash
# Add migration
dotnet ef migrations add CreatePatientAndAuthTables --project Server/Server.csproj --output-dir Infrastructure/Migrations

# Apply migration
dotnet ef database update --project Server/Server.csproj

# Rollback migration
dotnet ef database update <PreviousMigrationName> --project Server/Server.csproj

# Generate SQL script for review
dotnet ef migrations script --project Server/Server.csproj --output migrations.sql
```

---

## Implementation Validation Strategy

- [ ] Migration `Up()` executes without error against a clean Neon PostgreSQL 16 instance
- [ ] Migration `Down()` rolls back completely (all tables, indexes, triggers removed)
- [ ] `patients.email` unique index rejects case-insensitive duplicate insert (e.g., `User@Example.com` vs `user@example.com`)
- [ ] `email_verification_tokens.token_hash` index confirms O(1) lookup via `EXPLAIN` plan
- [ ] INSERT into `audit_logs` succeeds; UPDATE on `audit_logs` raises PostgreSQL exception (trigger verified)
- [ ] DELETE on `audit_logs` raises PostgreSQL exception (trigger verified)
- [ ] FK constraint `email_verification_tokens.patient_id → patients.id` CASCADE DELETE verified
- [ ] EF Core `DbContext` resolves `Patient`, `EmailVerificationToken`, `AuditLog` entities without error at startup
- [ ] Integration tests pass (if applicable)

---

## Implementation Checklist

- [x] Create `Patient` EF Core entity with all columns from design.md DR-001 (id, name, email, phone, dateOfBirth, passwordHash, emailVerified, status, createdAt)
- [x] Create `EmailVerificationToken` EF Core entity (id, patientId, tokenHash, expiresAt, usedAt, createdAt)
- [x] Create `AuditLog` EF Core entity (id, userId, action, entityType, entityId, details JSONB, ipAddress, timestamp)
- [x] Configure `PatientConfiguration`: case-insensitive functional unique index `uq_patients_email_lower ON patients (lower(email))` via `migrationBuilder.Sql()` (AC-3); soft-delete query filter on `status != 'Deactivated'`
- [x] Configure `EmailVerificationTokenConfiguration`: FK to Patient with CASCADE DELETE, index on tokenHash, index on patientId
- [x] Configure `AuditLogConfiguration`: JSONB mapping for `details`, index on (entityType, entityId), plus userId/patientId/timestamp indexes
- [x] Write EF Core migration `AddCaseInsensitiveEmailIndex` with `Up()` and `Down()` (replaces case-sensitive `ix_patients_email` with `uq_patients_email_lower`, adds `ix_audit_logs_entity_type_entity_id`)
- [x] Add SQL for immutable audit log trigger inside migration `Up()` (via `migrationBuilder.Sql()`) — already in `AddAuditNotificationEntities` migration
- [x] Drop trigger in migration `Down()` before dropping table — implemented in `AddAuditNotificationEntities.Down()`
- [x] Register entity configurations in `AppDbContext.OnModelCreating()` via `ApplyConfigurationsFromAssembly`
- [x] Generate SQL script (`dotnet ef migrations script`) and review for correctness before applying
