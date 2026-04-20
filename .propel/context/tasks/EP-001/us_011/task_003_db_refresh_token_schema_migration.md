# Task - TASK_003

## Requirement Reference

- **User Story**: US_011 — User Login, JWT Tokens & Session Auto-Timeout
- **Story Location**: `.propel/context/tasks/EP-001/us_011/us_011.md`
- **Acceptance Criteria**:
  - AC-1: Supports persisting rotating refresh tokens with user association and expiry — enables `refresh_tokens` table required by login and refresh flows
  - AC-3: Supports atomic refresh token rotation (revoke old row, insert new row within a transaction) — requires `revokedAt` nullable column and `familyId` grouping column
  - AC-4: Supports refresh token revocation on logout — requires `revokedAt` column writable by logout handler
- **Edge Cases**:
  - Refresh token reuse detection: requires `familyId` column + composite index on `(userId, familyId)` to efficiently revoke all tokens in a family in a single UPDATE statement

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
| Frontend | N/A (database task) | N/A |
| Backend | ASP.NET Core Web API | .net 10 |
| ORM | Entity Framework Core | 9.x |
| Database | PostgreSQL | 16+ |
| Database Hosting | Neon PostgreSQL | Free tier |
| AI/ML | N/A | N/A |
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

Create the `refresh_tokens` database table and associated EF Core migration required by US_011. The schema must support:

- Secure storage of hashed refresh tokens (never raw tokens — OWASP A02)
- Token family grouping for reuse-detection and family-wide revocation (edge case: stolen token)
- Per-device session tracking to enable multi-device logout independence
- Expiry and revocation timestamps for lifecycle management
- Fast lookup by token hash (primary access pattern on every `/refresh` call)
- Fast family-wide revocation scan by `(userId, familyId)` (security alert path)

The migration is implemented as an EF Core code-first migration targeting `Npgsql.EntityFrameworkCore.PostgreSQL 9.x` and `Neon PostgreSQL 16+`.

## Dependent Tasks

- None — this task has no upstream task dependencies. It must be completed before TASK_002.

## Impacted Components

| Component | Status | Location |
|-----------|--------|----------|
| `RefreshToken` (EF Core entity) | NEW | `Server/Domain/Entities/RefreshToken.cs` |
| `AppDbContext` | MODIFY | `Server/Infrastructure/Data/AppDbContext.cs` |
| EF Core migration file | NEW | `Server/Infrastructure/Data/Migrations/` |

## Implementation Plan

1. **Define EF Core Entity (`RefreshToken.cs`)**:

   ```csharp
   public sealed class RefreshToken
   {
       public Guid Id { get; init; }           // PK, default new Guid
       public Guid UserId { get; init; }        // FK → Users.Id (cascade delete)
       public string TokenHash { get; init; }   // SHA-256 hex of raw refresh token; indexed
       public Guid FamilyId { get; init; }      // Groups all rotated descendants of one original token
       public string DeviceId { get; init; }    // Client-supplied fingerprint (max 255 chars)
       public DateTime ExpiresAt { get; init; } // UTC; raw token TTL (7 days default)
       public DateTime? RevokedAt { get; set; } // NULL = active; non-NULL = revoked
       public DateTime CreatedAt { get; init; } // UTC, set at insert
   }
   ```

2. **Register in `AppDbContext`**: Add `DbSet<RefreshToken> RefreshTokens { get; set; }` and configure in `OnModelCreating` via Fluent API:
   - Table name: `refresh_tokens` (snake_case — Npgsql convention)
   - `TokenHash`: max length 512, required, unique index (`IX_refresh_tokens_token_hash`)
   - `DeviceId`: max length 255, required
   - `FamilyId`: required
   - `UserId`: required, FK → `users.id` with `OnDelete(DeleteBehavior.Cascade)`
   - `ExpiresAt`, `CreatedAt`: mapped as `timestamptz` (UTC-enforced by Npgsql)
   - `RevokedAt`: nullable `timestamptz`

3. **Create EF Core Migration**:
   - Run `dotnet ef migrations add AddRefreshTokensTable` to generate the migration scaffold.
   - Verify the generated `Up()` method creates the table and all three indexes (token_hash unique, userId+familyId composite).
   - Verify the generated `Down()` method drops indexes first, then drops the table (correct rollback order).

4. **Apply Migration**:
   - Development: `dotnet ef database update` against the Neon PostgreSQL connection string from environment variable `ConnectionStrings__DefaultConnection`.
   - CI/CD: migration applied automatically via `dotnet ef database update` step in the GitHub Actions pipeline before integration test run.

## Current Project State

```
Server/
└── Infrastructure/
    └── Data/
        ├── AppDbContext.cs    ← needs DbSet<RefreshToken> added
        └── Migrations/        ← new migration file goes here
```

> This is a greenfield project. Only the AppDbContext shell and initial migration baseline are expected to exist from project scaffolding.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Domain/Entities/RefreshToken.cs` | EF Core entity with all columns and navigation property |
| MODIFY | `Server/Infrastructure/Data/AppDbContext.cs` | Add `DbSet<RefreshToken>` + Fluent API column/index config in `OnModelCreating` |
| CREATE | `Server/Infrastructure/Data/Migrations/<timestamp>_AddRefreshTokensTable.cs` | EF Core `Up()` and `Down()` migration |
| CREATE | `Server/Infrastructure/Data/Migrations/<timestamp>_AddRefreshTokensTable.Designer.cs` | EF Core migration snapshot (auto-generated) |

## External References

- [EF Core 9 — Code First Migrations (Npgsql)](https://www.npgsql.org/efcore/index.html) — Npgsql-specific column type mapping (`timestamptz`, `uuid`, `text`)
- [EF Core Fluent API — Index configuration](https://learn.microsoft.com/en-us/ef/core/modeling/indexes?tabs=data-annotations) — `HasIndex`, `IsUnique`, composite index syntax
- [EF Core — Cascade Delete](https://learn.microsoft.com/en-us/ef/core/saving/cascade-delete) — `OnDelete(DeleteBehavior.Cascade)` for `userId` FK
- [PostgreSQL 16 — Index Types](https://www.postgresql.org/docs/16/indexes.html) — B-tree indexes (default, suitable for equality lookup on `token_hash`)
- [Neon PostgreSQL — Connection Strings](https://neon.tech/docs/connect/connect-from-any-app) — TLS-required connection string format for .NET
- [OWASP: Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html) — Guidance on hashing tokens before storage (SHA-256 for non-secret-derived tokens)

## Build Commands

```bash
# Ensure Npgsql EF Core provider is installed
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 9.*
dotnet add package Microsoft.EntityFrameworkCore.Design --version 9.*

# Generate the migration (run from solution root or Server project directory)
dotnet ef migrations add AddRefreshTokensTable \
  --project Server/PropelIQ.Server.csproj \
  --startup-project Server/PropelIQ.Server.csproj \
  --output-dir Infrastructure/Data/Migrations

# Apply migration to the target database
dotnet ef database update \
  --project Server/PropelIQ.Server.csproj \
  --connection "$env:ConnectionStrings__DefaultConnection"

# Rollback migration (if needed)
dotnet ef database update <PreviousMigrationName> \
  --project Server/PropelIQ.Server.csproj

# Verify migration was applied
dotnet ef migrations list --project Server/PropelIQ.Server.csproj
```

## Implementation Validation Strategy

- [ ] Unit tests pass (to be planned separately via `plan-unit-test` workflow)
- [ ] `dotnet ef migrations list` shows `AddRefreshTokensTable` as `Applied`
- [ ] `refresh_tokens` table exists in PostgreSQL with all 8 columns: `id`, `user_id`, `token_hash`, `family_id`, `device_id`, `expires_at`, `revoked_at`, `created_at`
- [ ] `IX_refresh_tokens_token_hash` unique index exists (verify via `\d refresh_tokens` in psql)
- [ ] `IX_refresh_tokens_user_id_family_id` composite index exists
- [ ] FK constraint `FK_refresh_tokens_users_user_id` exists with CASCADE DELETE behaviour
- [ ] `Down()` migration rollback succeeds without errors: drops indexes then drops table
- [ ] `expires_at` and `created_at` columns are type `timestamptz` (UTC-aware, not `timestamp without time zone`)

## Implementation Checklist

- [ ] Create `RefreshToken.cs` EF Core entity with all 8 properties and XML doc comment for `FamilyId` (explains reuse-detection purpose)
- [ ] Add `DbSet<RefreshToken>` to `AppDbContext` and configure via Fluent API: column types, indexes, FK cascade
- [ ] Generate EF Core migration `AddRefreshTokensTable` and verify `Up()` SQL creates table + 2 indexes
- [ ] Verify `Down()` rollback drops indexes first, then the table (correct dependency order)
- [ ] Apply migration to development Neon PostgreSQL database and confirm table structure with `\d refresh_tokens`
