# Task - task_002_be_reauth_role_assignment_api

## Requirement Reference

- **User Story:** us_046 — Role Assignment with Re-Authentication Gate
- **Story Location:** `.propel/context/tasks/EP-009/us_046/us_046.md`
- **Acceptance Criteria:**
  - AC-1: `PATCH /api/admin/users/{id}/role` updates `User.role`; the change is committed to the database; response confirms the new role. The existing user session (JWT) retains the old role until natural expiry — no session invalidation.
  - AC-2: When the target role is `Admin`, the request MUST include a valid short-lived `reAuthToken` (issued by `POST /api/admin/reauthenticate`); if absent or expired, HTTP 401 is returned and no change is committed.
  - AC-3: `POST /api/admin/reauthenticate` validates the calling Admin's current password using Argon2 comparison; if it fails, HTTP 401 is returned and a failed re-auth AuditLog entry is written.
  - AC-4: After a successful role change, an AuditLog entry is created with `actionType = "RoleChanged"`, `beforeState = { role: previousRole }`, `afterState = { role: newRole }`, `userId = adminId`, and UTC timestamp.
- **Edge Cases:**
  - Re-auth token is single-use and expires after 5 minutes; replaying an already-consumed or expired token returns HTTP 401.
  - Changing a user's role to their current role (no-op): treat as a valid idempotent update — commit, write AuditLog, return 200.

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

| Layer              | Technology              | Version    |
| ------------------ | ----------------------- | ---------- |
| Backend            | ASP.NET Core Web API    | .net 10     |
| Backend Messaging  | MediatR                 | 12.x       |
| Backend Validation | FluentValidation        | 11.x       |
| ORM                | Entity Framework Core   | 9.x        |
| Database           | PostgreSQL              | 16+        |
| Cache              | Upstash Redis           | Serverless |
| Authentication     | JWT + Argon2            | —          |
| Logging            | Serilog                 | 4.x        |

**Note:** All code and libraries MUST be compatible with versions listed above.

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

Implement two Admin Module endpoints and their supporting components:

1. **`POST /api/admin/reauthenticate`** — Issues a short-lived re-authentication token after verifying the calling Admin's current password (Argon2). The token is stored in Upstash Redis with a 5-minute TTL and a single-use flag. It is consumed (deleted from Redis) on first use. Failed verification writes an AuditLog entry and returns HTTP 401.

2. **`PATCH /api/admin/users/{id}/role`** — Updates the role of a managed Staff or Admin account. When the target role is `Admin`, a valid `reAuthToken` (from step 1) MUST be present in the request body; the handler validates and consumes it before committing the change. Role changes take effect on the user's next session — no session invalidation is performed (FR-061: "effective on next session"). An AuditLog entry is written with before/after role state for all successful changes (FR-059, NFR-009).

The re-auth token mechanism introduced here is also depended upon by US_045's deactivation path (`DELETE /api/admin/users/{id}`), making this a foundational shared component for all destructive Admin actions in EP-009.

---

## Dependent Tasks

- `task_002_be_admin_user_crud_api.md` (EP-009/us_045) — `AdminUsersController` and `AdminModuleRegistration` already exist; this task extends them with two additional endpoints.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `AdminUsersController` (existing — US_045) | Admin Module | MODIFY — Add `POST /reauthenticate` and `PATCH /{id}/role` action methods |
| `ReauthenticateCommand` (new) | Admin Module | CREATE — MediatR command: `currentPassword` |
| `ReauthenticateCommandHandler` (new) | Admin Module | CREATE — Argon2 verify, Redis store token, AuditLog on failure |
| `ReauthenticateCommandValidator` (new) | Admin Module | CREATE — FluentValidation: `currentPassword` not empty, max 200 chars |
| `AssignRoleCommand` (new) | Admin Module | CREATE — MediatR command: `targetUserId`, `newRole`, `reAuthToken?`, `requestingAdminId` |
| `AssignRoleCommandHandler` (new) | Admin Module | CREATE — Re-auth token validation for Admin elevation; User.role update; AuditLog |
| `AssignRoleCommandValidator` (new) | Admin Module | CREATE — FluentValidation: valid role enum; reAuthToken required when newRole = Admin |
| `IReAuthTokenStore` (new) | Infrastructure | CREATE — Interface: `IssueTokenAsync(adminId)`, `ConsumeTokenAsync(token)` |
| `RedisReAuthTokenStore` (new) | Infrastructure | CREATE — Redis SET with 5-min TTL (single-use: SET NX + DEL on consume) |
| `ReAuthTokenResponse` (new) | Shared Contracts | CREATE — API response DTO: `{ reAuthToken: string }` |
| `AssignRoleRequest` (new) | Shared Contracts | CREATE — API request DTO: `{ role: string, reAuthToken?: string }` |
| `AdminModuleRegistration` (existing — US_045) | DI Bootstrap | MODIFY — Register `ReauthenticateCommand`, `AssignRoleCommand`, `IReAuthTokenStore` |

---

## Implementation Plan

1. **Define shared DTOs**:
   - `ReAuthTokenResponse`: `{ string ReAuthToken }` — returned on successful re-authentication.
   - `AssignRoleRequest`: `{ string Role, string? ReAuthToken }` — request body for role assignment.

2. **Implement `IReAuthTokenStore` / `RedisReAuthTokenStore`**:
   - `IssueTokenAsync(Guid adminId) → string token`:
     - Generate a cryptographically secure token: 32-byte `RandomNumberGenerator.GetBytes(32)`, Base64URL-encoded.
     - Store in Redis: key = `reauth:{token_hash}`, value = `adminId.ToString()`, TTL = `TimeSpan.FromMinutes(5)`.
     - Hash the token before use as Redis key (SHA-256) — raw token is never stored, preventing Redis dump exposure.
     - Return the unhashed token to the caller (to send to the client).
   - `ConsumeTokenAsync(string token, Guid expectedAdminId) → bool`:
     - Compute SHA-256 of the incoming token to derive the Redis key.
     - Atomic `GET` then `DEL` using Redis transaction (`IDatabase.StringGetDeleteAsync`) — single-use enforced.
     - Return `true` if key existed and stored `adminId == expectedAdminId`; `false` otherwise (expired, already used, or wrong admin).

3. **Implement `POST /api/admin/reauthenticate`** — `ReauthenticateCommand` handler:
   - Resolve calling Admin's user record from `HttpContext.User` claims (sub → `Guid adminId`).
   - Load `User` from EF Core by `adminId`; verify `Argon2.Verify(user.PasswordHash, command.CurrentPassword)`.
   - On failure:
     - Write `AuditLog` entry: `actionType = "ReAuthFailed"`, `userId = adminId`, no before/after state.
     - Return HTTP 401: `{ error: "Re-authentication failed" }`.
   - On success:
     - Call `IReAuthTokenStore.IssueTokenAsync(adminId)` to get a short-lived token.
     - Return HTTP 200: `ReAuthTokenResponse { ReAuthToken = token }`.

4. **Implement `PATCH /api/admin/users/{id}/role`** — `AssignRoleCommand` handler:
   - Resolve `requestingAdminId` from JWT claims.
   - Fetch target user by `targetUserId`; return HTTP 404 if not found or if role is `Patient`.
   - **Re-auth gate for Admin elevation**: if `newRole == "Admin"`:
     - Call `IReAuthTokenStore.ConsumeTokenAsync(command.ReAuthToken, requestingAdminId)`.
     - If `false` (token missing, expired, or already used): return HTTP 401 `{ error: "Valid re-authentication required for Admin elevation" }`.
   - Capture `beforeRole = user.Role`.
   - Set `user.Role = newRole`; save via EF Core.
   - Write `AuditLog`: `actionType = "RoleChanged"`, `beforeState = { role: beforeRole }`, `afterState = { role: newRole }`, `userId = requestingAdminId`.
   - Return HTTP 200 with updated `ManagedUserDto`.

5. **Add action methods to `AdminUsersController`**:
   - `[HttpPost("reauthenticate")] [Authorize(Roles = "Admin")]` → dispatches `ReauthenticateCommand`; returns `200 ReAuthTokenResponse` or `401`.
   - `[HttpPatch("{id:guid}/role")] [Authorize(Roles = "Admin")]` → dispatches `AssignRoleCommand`; returns `200 ManagedUserDto` or `401` or `404`.

6. **Register in `AdminModuleRegistration`** — Add `IReAuthTokenStore → RedisReAuthTokenStore` (singleton or scoped per Redis multiplexer pattern), and register the two new command validators.

---

## Current Project State

```
Server/
  Admin/
    Controllers/
      AdminUsersController.cs           ← EXISTS (US_045 task_002) — MODIFY
    Commands/
      CreateManagedUserCommand.cs       ← EXISTS (US_045 task_002)
      DeactivateUserCommand.cs          ← EXISTS (US_045 task_002)
    Queries/
      GetManagedUsersQuery.cs           ← EXISTS (US_045 task_002)
  Infrastructure/
    Session/
      RedisSessionInvalidationService.cs ← EXISTS (US_045 task_002) — Redis pattern to follow
  DI/
    AdminModuleRegistration.cs          ← EXISTS (US_045 task_002) — MODIFY
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Admin/Commands/ReauthenticateCommand.cs` | MediatR command: `currentPassword` field |
| CREATE | `Server/Admin/Commands/ReauthenticateCommandHandler.cs` | Argon2 verify + IReAuthTokenStore.IssueToken + AuditLog on failure |
| CREATE | `Server/Admin/Commands/ReauthenticateCommandValidator.cs` | FluentValidation: password not empty, max 200 chars |
| CREATE | `Server/Admin/Commands/AssignRoleCommand.cs` | MediatR command: targetUserId, newRole, reAuthToken?, requestingAdminId |
| CREATE | `Server/Admin/Commands/AssignRoleCommandHandler.cs` | Re-auth token gate for Admin elevation; role update; AuditLog before/after |
| CREATE | `Server/Admin/Commands/AssignRoleCommandValidator.cs` | FluentValidation: valid role enum; reAuthToken required when newRole = Admin |
| CREATE | `Server/Infrastructure/ReAuth/IReAuthTokenStore.cs` | Interface: IssueTokenAsync, ConsumeTokenAsync |
| CREATE | `Server/Infrastructure/ReAuth/RedisReAuthTokenStore.cs` | Redis SET NX (5-min TTL) + StringGetDeleteAsync for single-use enforcement |
| CREATE | `Server/Shared/Contracts/ReAuthTokenResponse.cs` | Response DTO: `ReAuthToken` string |
| CREATE | `Server/Shared/Contracts/AssignRoleRequest.cs` | Request DTO: `Role`, `ReAuthToken?` |
| MODIFY | `Server/Admin/Controllers/AdminUsersController.cs` | Add `POST /reauthenticate` and `PATCH /{id}/role` action methods |
| MODIFY | `Server/DI/AdminModuleRegistration.cs` | Register IReAuthTokenStore, two new commands and validators |

---

## External References

- [Konscious.Security.Cryptography — Argon2 for .NET](https://github.com/kmaragon/Konscious.Security.Cryptography) — `Argon2id.VerifyHash()` for password verification (consistent with NFR-008 Argon2 requirement)
- [StackExchange.Redis — StringGetDeleteAsync](https://stackexchange.github.io/StackExchange.Redis/Basics.html) — Atomic GET+DEL for single-use token consumption
- [System.Security.Cryptography.RandomNumberGenerator](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.randomnumbergenerator) — Secure 32-byte token generation
- [MediatR 12.x — IRequest / IRequestHandler](https://github.com/jbogard/MediatR/wiki) — CQRS command dispatch pattern
- [FluentValidation 11.x — AbstractValidator](https://docs.fluentvalidation.net/en/latest/aspnet.html) — Request body validation integrated with MediatR pipeline
- [FR-061 (spec.md)](../.propel/context/docs/spec.md) — Role assignment; change effective on next session
- [FR-062 (spec.md)](../.propel/context/docs/spec.md) — Re-authentication required for Admin elevation and deactivation
- [NFR-008 (design.md)](../.propel/context/docs/design.md) — Argon2 password hashing requirement
- [NFR-009, FR-059 (design.md / spec.md)](../.propel/context/docs/design.md) — Immutable AuditLog with before/after state
- [AD-8, AD-9 (design.md)](../.propel/context/docs/design.md) — Upstash Redis for session/token state; JWT + refresh token rotation
- [UC-010 Sequence Diagram (models.md)](../.propel/context/docs/models.md) — `POST /api/admin/reauthenticate` flow; Argon2 verification; `PATCH /{id}/role` sequence

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [ ] Unit tests pass (xUnit + Moq — all command handlers and validators)
- [ ] Integration tests pass (controller → handler → in-memory EF Core + Redis mock)
- [ ] `POST /api/admin/reauthenticate` with correct password → HTTP 200 + non-empty `reAuthToken`
- [ ] `POST /api/admin/reauthenticate` with wrong password → HTTP 401; AuditLog entry written with `actionType = "ReAuthFailed"`
- [ ] `POST /api/admin/reauthenticate` without authentication → HTTP 401 (JWT middleware, not handler)
- [ ] `PATCH /api/admin/users/{id}/role` with `newRole = Staff` (downgrade) without `reAuthToken` → HTTP 200; role updated
- [ ] `PATCH /api/admin/users/{id}/role` with `newRole = Admin` and valid `reAuthToken` → HTTP 200; role updated; AuditLog written
- [ ] `PATCH /api/admin/users/{id}/role` with `newRole = Admin` and no `reAuthToken` → HTTP 401
- [ ] `PATCH /api/admin/users/{id}/role` with `newRole = Admin` and expired/replayed `reAuthToken` → HTTP 401
- [ ] Re-auth token is single-use: second call with same token → HTTP 401
- [ ] Re-auth token expires after 5 minutes: Redis TTL verified in integration test
- [ ] Role change does NOT invalidate target user's active Redis session (contrast with deactivation)

---

## Implementation Checklist

- [ ] Create `IReAuthTokenStore` interface and `RedisReAuthTokenStore` implementation (SHA-256 key hashing, SET NX 5-min TTL, atomic StringGetDeleteAsync)
- [ ] Create `ReauthenticateCommand` + `ReauthenticateCommandValidator` (password not empty)
- [ ] Create `ReauthenticateCommandHandler` (Argon2 verify, IssueToken on success, AuditLog on failure, HTTP 401 on failure)
- [ ] Create `AssignRoleCommand` + `AssignRoleCommandValidator` (role enum validation; reAuthToken required when newRole = Admin)
- [ ] Create `AssignRoleCommandHandler` (re-auth token gate for Admin, role update, AuditLog before/after)
- [ ] Create `ReAuthTokenResponse` and `AssignRoleRequest` DTOs
- [ ] Modify `AdminUsersController` to add `POST /reauthenticate` and `PATCH /{id}/role` endpoints
- [ ] Register all new components in `AdminModuleRegistration`
