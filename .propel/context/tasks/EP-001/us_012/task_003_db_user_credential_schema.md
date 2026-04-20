# Task - task_003_db_user_credential_schema

## Requirement Reference

- **User Story:** us_012 — Admin-Managed Staff & Admin Account Creation
- **Story Location:** `.propel/context/tasks/EP-001/us_012/us_012.md`
- **Acceptance Criteria:**
  - AC-1: `User` table stores Staff and Admin accounts with `status = Active`, `role`, `credentialEmailStatus`, and audit timestamps
  - AC-2: `CredentialSetupToken` table stores SHA-256 token hash with `expiresAt` and `usedAt` for one-time credential setup enforcement
  - AC-3: Email uniqueness enforced at the database level across both `users` and `patients` tables (separate unique constraints per table)
  - AC-4: RBAC role values are constrained to `Patient`, `Staff`, `Admin` via a CHECK constraint; no free-text role values permitted
- **Edge Cases:**
  - Credential setup token already used: `UsedAt IS NOT NULL` check enforced at the application layer; DB stores the timestamp for audit
  - Token expiry: `ExpiresAt` column used in application-layer query; no DB-level TTL job required for Phase 1

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

| Layer      | Technology              | Version |
| ---------- | ----------------------- | ------- |
| Database   | PostgreSQL              | 16+     |
| ORM        | Entity Framework Core   | 9.x     |
| DB Hosting | Neon PostgreSQL (free tier) | —   |
| Testing    | xUnit                   | 2.x     |
| AI/ML      | N/A                     | N/A     |
| Mobile     | N/A                     | N/A     |

> All code and libraries MUST be compatible with versions above.

---

## AI References (AI Tasks Only)

| Reference Type        | Value |
| --------------------- | ----- |
| **AI Impact**         | No    |
| **AIR Requirements**  | N/A   |
| **AI Pattern**        | N/A   |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A   |
| **Model Provider**    | N/A   |

---

## Mobile References (Mobile Tasks Only)

| Reference Type      | Value |
| ------------------- | ----- |
| **Mobile Impact**   | No    |
| **Platform Target** | N/A   |
| **Min OS Version**  | N/A   |
| **Mobile Framework**| N/A   |

---

## Task Overview

Create EF Core code-first migrations for the `User` and `CredentialSetupToken` entities required by US_012. The `User` entity represents authenticated system users across Staff and Admin roles (the `Patient` entity was created in US_010 task_003 and is not duplicated here). `CredentialSetupToken` stores the SHA-256 hash of the one-time invite token with expiry and used-at timestamps. Schema aligns with design.md domain entities for `User` (id, email, passwordHash, role, status, lastLoginAt, createdAt) and adds a `credentialEmailStatus` column to track SendGrid delivery state. Each migration includes a `Down()` rollback.

---

## Dependent Tasks

- **US_010 task_003_db_patient_schema** (EP-001/us_010) — `patients` and `audit_logs` tables must already exist (the `audit_logs` INSERT-only trigger was created there and must NOT be re-created)

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `User` EF Core entity + type configuration | `Server/Infrastructure/Persistence/` |
| CREATE | `CredentialSetupToken` EF Core entity + type configuration | `Server/Infrastructure/Persistence/` |
| CREATE | EF Core migration: `CreateUserAndCredentialTables` | `Server/Infrastructure/Migrations/` |
| MODIFY | `AppDbContext.cs` | Add `DbSet<User>`, `DbSet<CredentialSetupToken>`; apply configurations |

---

## Implementation Plan

1. **`User` EF Core entity** (maps to `users` table):

   ```
   Id                    UUID          PK DEFAULT gen_random_uuid()
   Email                 VARCHAR(320)  NOT NULL UNIQUE
   PasswordHash          VARCHAR(512)  NULL             -- NULL until credential setup completed
   Role                  VARCHAR(20)   NOT NULL CHECK (role IN ('Patient','Staff','Admin'))
   Status                VARCHAR(20)   NOT NULL DEFAULT 'Active' CHECK (status IN ('Active','Deactivated'))
   CredentialEmailStatus VARCHAR(20)   NOT NULL DEFAULT 'Pending' CHECK (status IN ('Pending','Sent','Failed','Accepted'))
   LastLoginAt           TIMESTAMPTZ   NULL
   CreatedAt             TIMESTAMPTZ   NOT NULL DEFAULT NOW()
   ```

   - Soft-delete pattern: `Status = 'Deactivated'` (DR-010); no hard DELETE
   - `PasswordHash` is nullable to support the invite flow (account exists before credentials are set)
   - AES-256 / application-layer PHI encryption not required for `users` table (no PHI fields — role/status are not PHI)

2. **`CredentialSetupToken` EF Core entity** (maps to `credential_setup_tokens` table):

   ```
   Id          UUID         PK DEFAULT gen_random_uuid()
   UserId      UUID         NOT NULL FK → users(id) ON DELETE CASCADE
   TokenHash   VARCHAR(512) NOT NULL         -- SHA-256 hash of the raw invite token
   ExpiresAt   TIMESTAMPTZ  NOT NULL         -- CreatedAt + 72 hours (3-day invite window)
   UsedAt      TIMESTAMPTZ  NULL             -- SET when credentials are successfully configured
   CreatedAt   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
   ```

3. **Indexes**:
   - `CREATE UNIQUE INDEX uq_users_email_lower ON users (lower(email))` — case-insensitive uniqueness
   - `CREATE INDEX idx_credential_setup_tokens_hash ON credential_setup_tokens (token_hash)` — O(1) token lookup
   - `CREATE INDEX idx_credential_setup_tokens_user ON credential_setup_tokens (user_id)` — resend invite lookups

4. **EF Core migration** (`CreateUserAndCredentialTables`):
   - `Up()`: create `users` then `credential_setup_tokens` tables, then indexes
   - `Down()`: drop indexes → drop `credential_setup_tokens` → drop `users` (reverse FK dependency order)
   - **DO NOT** re-create `audit_logs` table or its INSERT-only trigger (already created by US_010 migration)
   - Zero-downtime compatible (new tables only — DR-013)

5. **`AuditLog` FK** — `audit_logs.userId` references `users.id` for Staff/Admin actions, and `patients.id` for Patient actions. For Phase 1, `userId UUID NOT NULL` references `users(id)` (added in this migration as a second FK path). Coordinate with US_010 migration author: the `audit_logs` table `userId` column may need to become a logical FK without a DB-level constraint if it must reference both `users` and `patients`. Recommendation: leave as unconstrained `UUID` with application-layer enforcement and a composite index on `(entity_type, entity_id)` — already created in US_010.

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no Server/ scaffold yet — greenfield ASP.NET Core project)
```

> Update this section with actual `Server/Infrastructure/Persistence/` tree after the backend scaffold is completed.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Infrastructure/Persistence/Entities/User.cs` | EF Core entity for User domain object (Staff/Admin) |
| CREATE | `Server/Infrastructure/Persistence/Entities/CredentialSetupToken.cs` | EF Core entity for one-time invite tokens |
| CREATE | `Server/Infrastructure/Persistence/Configurations/UserConfiguration.cs` | EF Core fluent config: table name, CHECK constraints, case-insensitive unique index, soft-delete query filter |
| CREATE | `Server/Infrastructure/Persistence/Configurations/CredentialSetupTokenConfiguration.cs` | EF Core fluent config: FK to User CASCADE, token hash index, user index |
| CREATE | `Server/Infrastructure/Migrations/<timestamp>_CreateUserAndCredentialTables.cs` | EF Core migration: Up() + Down() |
| CREATE | `Server/Infrastructure/Migrations/<timestamp>_CreateUserAndCredentialTables.Designer.cs` | EF Core migration snapshot |
| MODIFY | `Server/Infrastructure/Persistence/AppDbContext.cs` | Add `DbSet<User>`, `DbSet<CredentialSetupToken>`; apply configurations in `OnModelCreating` |

---

## External References

- [Entity Framework Core 9 — Code-First Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli)
- [EF Core — Fluent API: Check Constraints](https://learn.microsoft.com/en-us/ef/core/modeling/indexes?tabs=data-annotations#index-filter)
- [EF Core — HasCheckConstraint](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.metadata.builders.entitytypebuilder.hascheckconstraint)
- [PostgreSQL — Functional Unique Index (lower)](https://www.postgresql.org/docs/16/indexes-expressional.html)
- [PostgreSQL — Nullable Columns (NULL PasswordHash)](https://www.postgresql.org/docs/16/ddl-constraints.html)
- [design.md Domain Entities — User](../.propel/context/docs/design.md#domain-entities)
- [Neon PostgreSQL — Free Tier Connection](https://neon.tech/docs/connect/connect-from-any-app)

---

## Build Commands

```bash
# Add migration
dotnet ef migrations add CreateUserAndCredentialTables --project Server/Server.csproj --output-dir Infrastructure/Migrations

# Apply migration
dotnet ef database update --project Server/Server.csproj

# Rollback migration
dotnet ef database update <PreviousMigrationName> --project Server/Server.csproj

# Generate SQL script for review
dotnet ef migrations script --project Server/Server.csproj --output migrations_user.sql
```

---

## Implementation Validation Strategy

- [ ] Migration `Up()` executes without error against a clean Neon PostgreSQL 16 instance (after US_010 migration has run)
- [ ] Migration `Down()` rolls back completely (all tables and indexes removed; audit_logs table is NOT affected)
- [ ] `users.email` unique index rejects case-insensitive duplicate insert
- [ ] `users.role` CHECK constraint rejects any value outside `('Patient','Staff','Admin')`
- [ ] `users.status` CHECK constraint rejects any value outside `('Active','Deactivated')`
- [ ] `credential_setup_tokens.token_hash` index confirms O(1) lookup via `EXPLAIN` plan
- [ ] FK constraint `credential_setup_tokens.user_id → users.id` CASCADE DELETE verified
- [ ] `users.password_hash` column accepts NULL (invite flow: account created before password set)
- [ ] EF Core `AppDbContext` resolves `User` and `CredentialSetupToken` entities without error at startup
- [ ] `audit_logs` table is NOT modified or re-created by this migration
- [ ] Integration tests pass (if applicable)

---

## Implementation Checklist

- [ ] Create `User` EF Core entity with columns: id, email, passwordHash (nullable), role, status, credentialEmailStatus, lastLoginAt, createdAt
- [ ] Create `CredentialSetupToken` EF Core entity with columns: id, userId (FK), tokenHash, expiresAt, usedAt (nullable), createdAt
- [ ] Configure `UserConfiguration`: case-insensitive unique index on `lower(email)`, `HasCheckConstraint` for role values, `HasCheckConstraint` for status values, soft-delete query filter
- [ ] Configure `CredentialSetupTokenConfiguration`: FK to `users` with CASCADE DELETE, unique-enough index on `token_hash`, index on `user_id`
- [ ] Write EF Core migration `CreateUserAndCredentialTables` with `Up()` (create tables + indexes) and `Down()` (drop indexes + tables)
- [ ] Confirm migration does NOT touch `audit_logs` table (already created by US_010 migration)
- [ ] Register `User` and `CredentialSetupToken` configurations in `AppDbContext.OnModelCreating()`
- [ ] Generate SQL script and review before applying to confirm no unintended changes to existing tables
