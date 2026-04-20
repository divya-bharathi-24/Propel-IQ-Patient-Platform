# Task - task_002_be_admin_user_crud_api

## Requirement Reference

- **User Story:** us_045 — Admin CRUD on Staff & Admin User Accounts
- **Story Location:** `.propel/context/tasks/EP-009/us_045/us_045.md`
- **Acceptance Criteria:**
  - AC-1: `GET /api/admin/users` returns all Staff and Admin accounts with `name`, `email`, `role`, `status`, and `lastLoginAt` (RBAC: Admin only).
  - AC-2: `POST /api/admin/users` creates a `User` record with `status = Active`, triggers a credential setup email via SendGrid, and writes an AuditLog entry with before/after state; response includes `emailSent` flag.
  - AC-3: `DELETE /api/admin/users/{id}` (soft-delete) sets `status = Deactivated`, invalidates all active sessions for the target user in Upstash Redis, and writes an AuditLog entry. Self-deactivation is rejected with HTTP 422 and message "Cannot deactivate your own account".
  - AC-4: All user management endpoints return HTTP 403 Forbidden for non-Admin callers; no data is modified.
- **Edge Cases:**
  - Credential email failure: account is created successfully; response includes `emailSent = false`; Admin can call `POST /api/admin/users/{id}/resend-credentials` to retry.
  - Deactivating an already-Deactivated account: idempotent — update succeeds; AuditLog entry still written; no session invalidation needed (no active sessions).

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
| Email Service      | SendGrid                | —          |
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

Implement the Admin Module's user management REST API surface. Five endpoints cover the full CRUD lifecycle:

1. **`GET /api/admin/users`** — Lists all Staff and Admin accounts.
2. **`POST /api/admin/users`** — Creates a new Staff or Admin account; sends credential setup email via SendGrid; returns `emailSent` flag for graceful degradation.
3. **`PATCH /api/admin/users/{id}`** — Updates name and/or role on an existing account; role changes take effect on the user's next session (no active session invalidation required for edits).
4. **`DELETE /api/admin/users/{id}`** — Soft-deletes (deactivates) an account; invalidates all active Redis sessions for the target user; blocks self-deactivation.
5. **`POST /api/admin/users/{id}/resend-credentials`** — Resends the credential setup email to an existing Active account.

All endpoints are `[Authorize(Roles = "Admin")]`, follow the CQRS MediatR pattern (AD-2), and write AuditLog entries per action (NFR-009, FR-057, FR-058). The re-authentication check (FR-062) for deactivation is handled separately by US_046; this task assumes the re-auth token produced by US_046 is validated by middleware before the `DELETE` handler executes.

---

## Dependent Tasks

- No blocking dependencies — the `User` entity already exists from US_006/US_011. This task adds Admin-specific endpoints on top of the existing Auth module's User table.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `AdminUsersController` (new) | Admin Module | CREATE — REST controller: GET, POST, PATCH, DELETE, POST resend |
| `GetManagedUsersQuery` (new) | Admin Module | CREATE — MediatR query: returns Staff + Admin users |
| `GetManagedUsersQueryHandler` (new) | Admin Module | CREATE — EF Core query on `Users` filtered by role IN (Staff, Admin) |
| `CreateManagedUserCommand` (new) | Admin Module | CREATE — MediatR command: name, email, role |
| `CreateManagedUserCommandHandler` (new) | Admin Module | CREATE — Creates User, sends credential email, writes AuditLog |
| `UpdateManagedUserCommand` (new) | Admin Module | CREATE — MediatR command: id, name?, role? |
| `UpdateManagedUserCommandHandler` (new) | Admin Module | CREATE — Updates User, writes AuditLog with before/after state |
| `DeactivateUserCommand` (new) | Admin Module | CREATE — MediatR command: targetUserId, requestingAdminId |
| `DeactivateUserCommandHandler` (new) | Admin Module | CREATE — Self-deactivation guard, soft-delete, Redis session flush, AuditLog |
| `ResendCredentialEmailCommand` (new) | Admin Module | CREATE — MediatR command: targetUserId |
| `ResendCredentialEmailCommandHandler` (new) | Admin Module | CREATE — SendGrid email trigger; returns success/failure |
| `ICredentialEmailService` (new) | Infrastructure | CREATE — Interface for sending credential setup emails |
| `SendGridCredentialEmailService` (new) | Infrastructure | CREATE — SendGrid implementation; graceful degradation on failure |
| `ISessionInvalidationService` (new) | Infrastructure | CREATE — Interface for flushing Redis sessions by userId |
| `RedisSessionInvalidationService` (new) | Infrastructure | CREATE — Deletes all Redis keys matching `session:{userId}:*` |
| `ManagedUserDto` (new) | Shared Contracts | CREATE — API-safe DTO: id, name, email, role, status, lastLoginAt, emailDeliveryFailed? |
| `AdminModuleRegistration` (existing) | DI Bootstrap | MODIFY — Register all new commands, handlers, services |

---

## Implementation Plan

1. **Define `ManagedUserDto`** — Safe DTO for all user management responses: `id` (Guid), `name` (string), `email` (string), `role` (enum: Staff | Admin), `status` (enum: Active | Deactivated), `lastLoginAt` (DateTimeOffset?), `emailDeliveryFailed` (bool, nullable — populated only in creation/resend responses).

2. **Implement `GET /api/admin/users`** — `GetManagedUsersQuery` handler executes:
   ```
   SELECT * FROM Users WHERE role IN ('Staff', 'Admin') ORDER BY name ASC
   ```
   Maps to `List<ManagedUserDto>`. No pagination in Phase 1 (user count bounded by team size).

3. **Implement `POST /api/admin/users`** — `CreateManagedUserCommand` handler:
   - Validate email uniqueness (HTTP 409 if duplicate).
   - Generate a secure one-time credential setup token (32-byte `RandomNumberGenerator`, stored hashed in `CredentialSetupTokens` table with 72-hour expiry).
   - `INSERT User { name, email, role, status = Active, passwordHash = null (set on first login via token) }`.
   - Invoke `ICredentialEmailService.SendCredentialSetupEmailAsync(email, token)`; catch and log any SendGrid failure; set `emailDeliveryFailed = true` in response if send fails (graceful degradation — account is created regardless).
   - Write `AuditLog` entry: `actionType = "UserCreated"`, `afterState = { email, role, status }`, `userId = adminId`.
   - Return `ManagedUserDto` with `emailDeliveryFailed` flag.

4. **Implement `PATCH /api/admin/users/{id}`** — `UpdateManagedUserCommand` handler:
   - Fetch existing user; return HTTP 404 if not found or if role is Patient (cannot manage patients via this endpoint).
   - Capture `beforeState = { name, role }`.
   - Apply updates: `name` (if provided), `role` (if provided — role change takes effect on next session; no session invalidation required here).
   - Write `AuditLog` with before/after state.
   - Return updated `ManagedUserDto`.

5. **Implement `DELETE /api/admin/users/{id}`** (soft-delete / deactivation) — `DeactivateUserCommand` handler:
   - **Self-deactivation guard**: if `targetUserId == requestingAdminId`, return HTTP 422 with `{ error: "Cannot deactivate your own account" }`.
   - Fetch target user; return HTTP 404 if not found.
   - Set `User.status = Deactivated`.
   - Call `ISessionInvalidationService.InvalidateAllSessionsAsync(targetUserId)` — flushes all Redis keys for the target user, forcing immediate logout on next request.
   - Write `AuditLog` entry: `actionType = "UserDeactivated"`, `beforeState = { status: Active }`, `afterState = { status: Deactivated }`.
   - Return HTTP 204 No Content.

6. **Implement `POST /api/admin/users/{id}/resend-credentials`** — `ResendCredentialEmailCommand` handler:
   - Fetch existing user; return HTTP 404 if not found; HTTP 422 if user is Deactivated (no credential email to deactivated accounts).
   - Generate fresh credential setup token (replaces any existing token for this user).
   - Invoke `ICredentialEmailService.SendCredentialSetupEmailAsync`; return `200 OK` on success, `502 Bad Gateway` with structured error body if SendGrid call fails.

7. **Implement `ISessionInvalidationService` / `RedisSessionInvalidationService`** — Uses `IConnectionMultiplexer` (Upstash Redis) to `SCAN` and `DEL` all keys matching pattern `session:{userId}:*` in a single pipeline, ensuring all active JWT refresh tokens for the user are voided (AD-9).

8. **Implement `ICredentialEmailService` / `SendGridCredentialEmailService`** — Calls SendGrid `POST /v3/mail/send` with a transactional credential setup email template. Wraps in try/catch; returns `bool emailSent`. Does not throw — failure is gracefully handled by returning `emailDeliveryFailed = true` to the caller.

---

## Current Project State

```
Server/
  Admin/
    Controllers/                        ← folder to create
    Commands/                           ← folder to create
    Queries/                            ← folder to create
  Auth/
    Services/
      TokenService.cs                   ← existing JWT/Redis token service; session pattern to follow
  Infrastructure/
    Persistence/
      Repositories/                     ← existing repository folder
    Email/
      SendGridEmailService.cs           ← existing SendGrid service to follow for credential email
  DI/
    AdminModuleRegistration.cs          ← existing DI bootstrap to extend
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Admin/Controllers/AdminUsersController.cs` | REST controller: GET list, POST create, PATCH update, DELETE deactivate, POST resend |
| CREATE | `Server/Admin/Queries/GetManagedUsersQuery.cs` | MediatR query: returns `List<ManagedUserDto>` |
| CREATE | `Server/Admin/Queries/GetManagedUsersQueryHandler.cs` | EF Core query: Users WHERE role IN (Staff, Admin) |
| CREATE | `Server/Admin/Commands/CreateManagedUserCommand.cs` | MediatR command: name, email, role |
| CREATE | `Server/Admin/Commands/CreateManagedUserCommandHandler.cs` | Create user, send email, AuditLog, return DTO with emailDeliveryFailed flag |
| CREATE | `Server/Admin/Commands/UpdateManagedUserCommand.cs` | MediatR command: id, name?, role? |
| CREATE | `Server/Admin/Commands/UpdateManagedUserCommandHandler.cs` | Update user fields, AuditLog before/after |
| CREATE | `Server/Admin/Commands/DeactivateUserCommand.cs` | MediatR command: targetUserId, requestingAdminId |
| CREATE | `Server/Admin/Commands/DeactivateUserCommandHandler.cs` | Self-deactivation guard, soft-delete, Redis session flush, AuditLog |
| CREATE | `Server/Admin/Commands/ResendCredentialEmailCommand.cs` | MediatR command: targetUserId |
| CREATE | `Server/Admin/Commands/ResendCredentialEmailCommandHandler.cs` | Regenerate token, send email, return success/failure |
| CREATE | `Server/Infrastructure/Email/CredentialEmailService.cs` | SendGrid credential setup email implementation |
| CREATE | `Server/Infrastructure/Session/RedisSessionInvalidationService.cs` | Redis SCAN+DEL for all user sessions |
| CREATE | `Server/Shared/Contracts/ManagedUserDto.cs` | API DTO: id, name, email, role, status, lastLoginAt, emailDeliveryFailed? |
| MODIFY | `Server/DI/AdminModuleRegistration.cs` | Register all new commands, handlers, services |

---

## External References

- [ASP.NET Core 9 Role-Based Authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-9.0) — `[Authorize(Roles = "Admin")]`
- [MediatR 12.x Commands and Queries](https://github.com/jbogard/MediatR/wiki) — `IRequest<T>`, `IRequestHandler` patterns
- [FluentValidation 11.x with ASP.NET Core](https://docs.fluentvalidation.net/en/latest/aspnet.html) — Pipeline-integrated validators
- [StackExchange.Redis — SCAN + DEL Pipeline](https://stackexchange.github.io/StackExchange.Redis/Basics.html) — Bulk key deletion for session invalidation
- [SendGrid .NET SDK v9](https://github.com/sendgrid/sendgrid-csharp) — `SendGridClient.SendEmailAsync` with transactional template
- [System.Security.Cryptography.RandomNumberGenerator](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.randomnumbergenerator) — Secure token generation (32 bytes)
- [NFR-006 RBAC (design.md)](../.propel/context/docs/design.md) — Admin-only endpoint enforcement
- [NFR-007, AD-8, AD-9 (design.md)](../.propel/context/docs/design.md) — Redis session management and forced logout
- [NFR-009, FR-057, FR-058 (spec.md)](../.propel/context/docs/spec.md) — AuditLog with before/after state
- [FR-060 (spec.md)](../.propel/context/docs/spec.md) — Admin CRUD and soft-delete requirement
- [DR-010 (design.md)](../.propel/context/docs/design.md) — Soft-delete pattern (row retained, status updated)
- [UC-010 Sequence Diagram (models.md)](../.propel/context/docs/models.md) — Full Admin user management API flow

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [ ] Unit tests pass (xUnit + Moq — all command handlers and query handler)
- [ ] Integration tests pass (controller → handler → EF Core in-memory + Redis mock)
- [ ] `GET /api/admin/users` returns HTTP 403 for Staff and Patient callers
- [ ] `GET /api/admin/users` returns only Staff and Admin accounts (no Patient rows)
- [ ] `POST /api/admin/users` creates user with `status = Active`; response includes `emailSent = true` on success
- [ ] `POST /api/admin/users` returns `emailDeliveryFailed = true` when SendGrid fails; user is still created
- [ ] `POST /api/admin/users` returns HTTP 409 for duplicate email
- [ ] `PATCH /api/admin/users/{id}` updates name and role; AuditLog contains before/after state
- [ ] `DELETE /api/admin/users/{id}` returns HTTP 422 when `targetUserId == requestingAdminId`
- [ ] `DELETE /api/admin/users/{id}` sets `status = Deactivated`; all Redis session keys for user are removed
- [ ] `DELETE /api/admin/users/{id}` returns HTTP 204; AuditLog entry written
- [ ] `POST /api/admin/users/{id}/resend-credentials` returns HTTP 422 for Deactivated users
- [ ] All endpoints return HTTP 401 for unauthenticated callers

---

## Implementation Checklist

- [ ] Create `ManagedUserDto` shared contract
- [ ] Implement `GetManagedUsersQuery` + handler (EF Core filter: role IN Staff, Admin)
- [ ] Implement `CreateManagedUserCommand` + handler (token gen, user insert, SendGrid call, AuditLog, emailDeliveryFailed flag)
- [ ] Implement `UpdateManagedUserCommand` + handler (404 guard, field update, AuditLog before/after)
- [ ] Implement `DeactivateUserCommand` + handler (self-deactivation 422 guard, soft-delete, Redis session flush, AuditLog)
- [ ] Implement `ResendCredentialEmailCommand` + handler (token refresh, SendGrid retry, graceful failure response)
- [ ] Implement `ICredentialEmailService` / `SendGridCredentialEmailService` (try/catch, returns bool)
- [ ] Implement `ISessionInvalidationService` / `RedisSessionInvalidationService` (SCAN+DEL pipeline)
- [ ] Implement `AdminUsersController` with all five endpoints, `[Authorize(Roles = "Admin")]`, ProducesResponseType annotations
- [ ] Register all new components in `AdminModuleRegistration`
