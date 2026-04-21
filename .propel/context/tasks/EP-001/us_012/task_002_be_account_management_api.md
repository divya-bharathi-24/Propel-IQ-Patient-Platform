# Task - task_002_be_account_management_api

## Requirement Reference

- **User Story:** us_012 — Admin-Managed Staff & Admin Account Creation
- **Story Location:** `.propel/context/tasks/EP-001/us_012/us_012.md`
- **Acceptance Criteria:**
  - AC-1: `POST /api/admin/users` (Admin-only) creates a new User record with `status = Active`, generates a credential setup token, and dispatches a SendGrid setup email; all steps logged in the audit trail
  - AC-2: `POST /api/auth/setup-credentials` (token-gated, public) validates the one-time token, enforces password complexity, hashes the password with Argon2, and activates the user's credentials
  - AC-3: `POST /api/patients/create` (Staff-only) creates a basic Patient record linked to the walk-in booking flow; returns 409 when the email already exists
  - AC-4: All admin account management endpoints return HTTP 403 Forbidden for non-Admin callers; all Staff-only endpoints return HTTP 403 for non-Staff callers (NFR-006 RBAC enforcement)
- **Edge Cases:**
  - Credential setup email bounces: SendGrid webhook or delivery status is recorded in the User record (`credentialEmailStatus`); Admin can resend via `POST /api/admin/users/{id}/resend-invite`
  - Duplicate Patient email during walk-in: 409 response body includes `{ message: "Email already registered", existingPatientId: "..." }` so the frontend can offer link-to-existing

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

| Layer              | Technology                 | Version |
| ------------------ | -------------------------- | ------- |
| Backend            | ASP.NET Core Web API       | .net 10 |
| Backend Messaging  | MediatR                    | 12.x    |
| Backend Validation | FluentValidation           | 11.x    |
| ORM                | Entity Framework Core      | 9.x     |
| Password Hashing   | Isopoh.Cryptography.Argon2 | Latest  |
| Email Service      | SendGrid SDK for .NET      | Latest  |
| Logging            | Serilog                    | 4.x     |
| Rate Limiting      | ASP.NET Core Rate Limiting | .net 10 |
| Testing — Unit     | xUnit + Moq                | 2.x     |
| Database           | PostgreSQL                 | 16+     |
| AI/ML              | N/A                        | N/A     |
| Mobile             | N/A                        | N/A     |

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

Implement the backend API surface for US_012 in the ASP.NET Core .net 10 modular monolith across three modules:

- **Admin Module** — `POST /api/admin/users` (Admin-only): create Staff/Admin user account, generate one-time credential setup token, dispatch SendGrid invite email, write audit log.
- **Auth Module** — `POST /api/auth/setup-credentials` (public, token-gated): validate one-time token, enforce password complexity, hash with Argon2, persist credentials, mark token consumed.
- **Patient Module** — `POST /api/patients/create` (Staff-only): create basic Patient record for walk-in flow; side-channel-safe duplicate-email handling returns 409 with `existingPatientId`.

All endpoints enforce RBAC via `[Authorize(Roles = "Admin")]` / `[Authorize(Roles = "Staff")]` (NFR-006). Non-matching roles return HTTP 403 before any business logic executes. AuditLog entries are written for every admin action (AC-1, NFR-009).

---

## Dependent Tasks

- **task_003_db_user_credential_schema** (EP-001/us_012) — `users` and `credential_setup_tokens` tables must exist
- **US_011** (EP-001) — JWT authentication layer must be active so `[Authorize]` attributes resolve role claims correctly

---

## Impacted Components

| Status | Component / Module                                                 | Project                                         |
| ------ | ------------------------------------------------------------------ | ----------------------------------------------- |
| CREATE | `AdminController`                                                  | ASP.NET Core API (`Server/Modules/Admin/`)      |
| CREATE | `CreateUserAccountCommand` + `CreateUserAccountCommandHandler`     | Admin Module — Application Layer                |
| CREATE | `ResendInviteCommand` + `ResendInviteCommandHandler`               | Admin Module — Application Layer                |
| CREATE | `SetupCredentialsCommand` + `SetupCredentialsCommandHandler`       | Auth Module — Application Layer                 |
| CREATE | `CreateWalkInPatientCommand` + `CreateWalkInPatientCommandHandler` | Patient Module — Application Layer              |
| CREATE | `CreateUserAccountValidator` (FluentValidation)                    | Admin Module — Application Layer                |
| CREATE | `SetupCredentialsValidator` (FluentValidation)                     | Auth Module — Application Layer                 |
| CREATE | `IUserRepository` + EF Core implementation                         | Admin Module — Infrastructure Layer             |
| CREATE | `ICredentialSetupTokenRepository` + EF Core implementation         | Auth Module — Infrastructure Layer              |
| MODIFY | `AuthController`                                                   | Add `POST /api/auth/setup-credentials` endpoint |
| MODIFY | `PatientController` (or create if not yet present)                 | Add `POST /api/patients/create` endpoint        |
| MODIFY | `AuditLogRepository`                                               | Reuse INSERT-only pattern from US_010           |
| MODIFY | `Program.cs` / `ServiceCollectionExtensions`                       | Register new Admin and Patient services         |

---

## Implementation Plan

1. **`CreateUserAccountCommand`** (MediatR `IRequest<CreateUserAccountResult>`):
   - Input: `Name`, `Email`, `Role` (Staff | Admin)
   - Handler steps:
     a. Validate uniqueness: `IUserRepository.ExistsByEmailAsync(email)` (case-insensitive) → throw `DuplicateEmailException` (→ 409) if found
     b. Create `User` entity: `status = Active`, `role = role`, `passwordHash = null` (credentials not yet set), `createdAt = UtcNow`
     c. Generate credential setup token: `RandomNumberGenerator.GetBytes(32)` → Base64Url; store `SHA-256(token)` in `CredentialSetupToken.TokenHash`; `ExpiresAt = UtcNow + 72h` (3-day invite window)
     d. Persist `User` + `CredentialSetupToken` in a single EF Core transaction
     e. Dispatch `SendCredentialSetupEmailCommand` (async fire-and-forget with error logging); record `credentialEmailStatus = Pending` on User
     f. INSERT `AuditLog`: `userId = adminId`, `action = "AdminCreatedUser"`, `entityType = "User"`, `entityId = newUser.Id`, `IpAddress`, `Timestamp = UtcNow`, `details = { role, email }`

2. **`SetupCredentialsCommand`** (MediatR `IRequest<SetupCredentialsResult>`):
   - Input: `Token` (raw), `Password`
   - Handler steps:
     a. Compute `SHA-256(token)` → lookup `CredentialSetupTokenRepository.GetByHashAsync(hash)`
     b. If not found → `NotFoundException` (→ 404)
     c. If `ExpiresAt < UtcNow` → `TokenExpiredException` (→ 410)
     d. If `UsedAt != null` → `TokenAlreadyUsedException` (→ 409)
     e. EF Core transaction: hash password `Argon2id(password)` → `User.passwordHash = hash`; `CredentialSetupToken.UsedAt = UtcNow`
     f. INSERT `AuditLog`: `action = "UserSetupCredentials"`, `entityType = "User"`, `entityId = user.Id`, IP, UTC

3. **`CreateWalkInPatientCommand`** (MediatR `IRequest<CreateWalkInPatientResult>`):
   - Input: `Name`, `Phone` (optional), `Email`
   - Handler steps:
     a. Check email uniqueness: `IPatientRepository.ExistsByEmailAsync(email)` → if found, return `ConflictResult` with `existingPatientId` (409 — enables link-to-existing in FE)
     b. Create `Patient` entity: `emailVerified = false`, `status = Active`, `passwordHash = null` (walk-in patient — no self-registration password required at this step)
     c. INSERT `Patient`; INSERT `AuditLog`: `action = "StaffCreatedWalkInPatient"`, `entityType = "Patient"`, `entityId = patient.Id`, staffId, IP, UTC

4. **RBAC enforcement** (NFR-006):
   - `AdminController` decorated with `[Authorize(Roles = "Admin")]` at controller level — all admin endpoints reject non-Admin with 403 before handler executes
   - `PatientController.CreateWalkIn` decorated with `[Authorize(Roles = "Staff")]` — rejects non-Staff with 403
   - `AuthController.SetupCredentials` is `[AllowAnonymous]` — token acts as the authorization credential

5. **`CreateUserAccountValidator`** (FluentValidation):
   - `Name`: `NotEmpty()`, `MaximumLength(200)`
   - `Email`: `NotEmpty()`, `EmailAddress()`, `MaximumLength(320)`
   - `Role`: `NotEmpty()`, `Must(r => r == "Staff" || r == "Admin")` — rejects invalid role strings

6. **`SetupCredentialsValidator`** (FluentValidation):
   - `Token`: `NotEmpty()`
   - `Password`: `NotEmpty()`, `MinimumLength(8)`, separate `Must()` rules for uppercase, digit, special character — each with descriptive `WithMessage()` (surfaces per-rule errors to AC-2)

7. **`ResendInviteCommand`** (MediatR, Admin-only):
   - Input: `UserId`
   - Handler: invalidate existing unused tokens for user, generate new token, dispatch new setup email, update `credentialEmailStatus` — respond 200 OK regardless to prevent enumeration

8. **Global exception mapping**:
   - `DuplicateEmailException → 409` (with `existingPatientId` in body for walk-in patient flow)
   - `TokenExpiredException → 410`
   - `TokenAlreadyUsedException → 409`
   - `ValidationException → 400` (FluentValidation per-rule errors)
   - Never expose stack traces or internal details (NFR-014)

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── server/
│   ├── Propel.Api.Gateway/
│   │   ├── Controllers/
│   │   │   ├── AdminController.cs          ← POST /api/admin/users, POST /api/admin/users/{id}/resend-invite
│   │   │   ├── AuthController.cs           ← POST /api/auth/setup-credentials added
│   │   │   └── PatientController.cs        ← POST /api/patients/create added
│   │   ├── Data/
│   │   │   ├── AppDbContext.cs             ← CredentialSetupTokens DbSet added
│   │   │   └── Configurations/
│   │   │       ├── CredentialSetupTokenConfiguration.cs  ← NEW
│   │   │       └── UserConfiguration.cs    ← updated (nullable PasswordHash, Name, CredentialEmailStatus)
│   │   ├── Infrastructure/
│   │   │   ├── Email/SendGridEmailService.cs ← SendCredentialSetupEmailAsync added
│   │   │   └── Repositories/
│   │   │       ├── UserRepository.cs       ← NEW
│   │   │       └── CredentialSetupTokenRepository.cs ← NEW
│   │   ├── Middleware/
│   │   │   └── ExceptionHandlingMiddleware.cs ← DuplicateUserEmailException + WalkInPatientDuplicateEmailException mapped
│   │   └── Program.cs                      ← IUserRepository, ICredentialSetupTokenRepository registered
│   ├── Propel.Domain/
│   │   ├── Entities/
│   │   │   ├── CredentialSetupToken.cs     ← NEW
│   │   │   └── User.cs                     ← updated (nullable PasswordHash, Name, CredentialEmailStatus, nav)
│   │   └── Interfaces/
│   │       ├── IUserRepository.cs          ← NEW
│   │       ├── ICredentialSetupTokenRepository.cs ← NEW
│   │       └── IEmailService.cs            ← SendCredentialSetupEmailAsync added
│   ├── Propel.Modules.Admin/
│   │   ├── Commands/
│   │   │   ├── CreateUserAccountCommand.cs ← NEW
│   │   │   └── ResendInviteCommand.cs      ← NEW
│   │   ├── Exceptions/
│   │   │   └── DuplicateUserEmailException.cs ← NEW
│   │   ├── Handlers/
│   │   │   ├── CreateUserAccountCommandHandler.cs ← NEW
│   │   │   └── ResendInviteCommandHandler.cs ← NEW
│   │   └── Validators/
│   │       └── CreateUserAccountValidator.cs ← NEW
│   ├── Propel.Modules.Auth/
│   │   ├── Commands/
│   │   │   └── SetupCredentialsCommand.cs  ← NEW
│   │   ├── Handlers/
│   │   │   └── SetupCredentialsCommandHandler.cs ← NEW
│   │   └── Validators/
│   │       └── SetupCredentialsValidator.cs ← NEW
│   ├── Propel.Modules.Notification/
│   │   ├── Commands/
│   │   │   └── SendCredentialSetupEmailCommand.cs ← NEW
│   │   └── Handlers/
│   │       └── SendCredentialSetupEmailCommandHandler.cs ← NEW
│   └── Propel.Modules.Patient/
│       ├── Commands/
│       │   └── CreateWalkInPatientCommand.cs ← NEW
│       ├── Exceptions/
│       │   └── WalkInPatientDuplicateEmailException.cs ← NEW
│       ├── Handlers/
│       │   └── CreateWalkInPatientCommandHandler.cs ← NEW
│       └── Validators/
│           └── CreateWalkInPatientValidator.cs ← NEW
```

---

## Expected Changes

| Action | File Path                                                              | Description                                                                         |
| ------ | ---------------------------------------------------------------------- | ----------------------------------------------------------------------------------- |
| CREATE | `Server/Modules/Admin/AdminController.cs`                              | Endpoints: `POST /api/admin/users`, `POST /api/admin/users/{id}/resend-invite`      |
| CREATE | `Server/Modules/Admin/Commands/CreateUserAccountCommand.cs`            | MediatR command + result                                                            |
| CREATE | `Server/Modules/Admin/Commands/CreateUserAccountCommandHandler.cs`     | User creation, token gen, email dispatch, audit log                                 |
| CREATE | `Server/Modules/Admin/Commands/ResendInviteCommand.cs`                 | MediatR command                                                                     |
| CREATE | `Server/Modules/Admin/Commands/ResendInviteCommandHandler.cs`          | Token invalidation + new invite dispatch                                            |
| CREATE | `Server/Modules/Admin/Validators/CreateUserAccountValidator.cs`        | FluentValidation: name, email, role                                                 |
| CREATE | `Server/Modules/Auth/Commands/SetupCredentialsCommand.cs`              | MediatR command + result                                                            |
| CREATE | `Server/Modules/Auth/Commands/SetupCredentialsCommandHandler.cs`       | Token validation, Argon2 hash, credential persist, audit log                        |
| CREATE | `Server/Modules/Auth/Validators/SetupCredentialsValidator.cs`          | FluentValidation: per-rule password checks                                          |
| CREATE | `Server/Modules/Patient/Commands/CreateWalkInPatientCommand.cs`        | MediatR command + result                                                            |
| CREATE | `Server/Modules/Patient/Commands/CreateWalkInPatientCommandHandler.cs` | Patient insert, duplicate email handling, audit log                                 |
| CREATE | `Server/Infrastructure/Repositories/UserRepository.cs`                 | EF Core user repository (`ExistsByEmailAsync`, CRUD)                                |
| CREATE | `Server/Infrastructure/Repositories/CredentialSetupTokenRepository.cs` | Token CRUD with SHA-256 hash lookup                                                 |
| MODIFY | `Server/Modules/Auth/AuthController.cs`                                | Add `POST /api/auth/setup-credentials` [AllowAnonymous]                             |
| MODIFY | `Server/Modules/Patient/PatientController.cs`                          | Add `POST /api/patients/create` [Authorize(Roles="Staff")]                          |
| MODIFY | `Server/Program.cs`                                                    | Register `IUserRepository`, `ICredentialSetupTokenRepository`, new MediatR handlers |

---

## External References

- [ASP.NET Core .net 10 — Role-Based Authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-9.0)
- [ASP.NET Core — AllowAnonymous](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/simple?view=aspnetcore-9.0#allow-anonymous-access-with-the-allowanonymous-attribute)
- [MediatR 12 — CQRS Pattern](https://github.com/jbogard/MediatR/wiki)
- [FluentValidation 11 — ASP.NET Core Integration](https://docs.fluentvalidation.net/en/latest/aspnet.html)
- [Isopoh.Cryptography.Argon2 — Argon2id Hashing](https://github.com/mheyman/Isopoh.Cryptography.Argon2)
- [SendGrid .NET SDK](https://github.com/sendgrid/sendgrid-csharp)
- [Entity Framework Core 9 — Transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions)
- [OWASP A01 — Broken Access Control](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)
- [OWASP A03 — Injection Prevention](https://owasp.org/Top10/A03_2021-Injection/)
- [OWASP — Username Enumeration Prevention](https://owasp.org/www-project-web-security-testing-guide/latest/4-Web_Application_Security_Testing/03-Identity_Management_Testing/04-Testing_for_Account_Enumeration_and_Guessable_User_Account)

---

## Build Commands

```bash
# Restore packages
dotnet restore

# Build
dotnet build

# Run API
dotnet run --project Server/Server.csproj

# Run unit tests
dotnet test

# Apply EF Core migrations
dotnet ef database update --project Server/Server.csproj
```

---

## Implementation Validation Strategy

- [ ] Unit tests pass for `CreateUserAccountCommandHandler` (happy path, duplicate email)
- [ ] Unit tests pass for `SetupCredentialsCommandHandler` (success, expired token, already-used token)
- [ ] Unit tests pass for `CreateWalkInPatientCommandHandler` (success, duplicate email with `existingPatientId`)
- [ ] Unit tests pass for `CreateUserAccountValidator` (all field validations including invalid role)
- [ ] Unit tests pass for `SetupCredentialsValidator` (each password rule violated in isolation)
- [ ] `POST /api/admin/users` returns 403 when called with Staff or Patient JWT role
- [ ] `POST /api/admin/users` returns 403 when called without any JWT
- [ ] `POST /api/patients/create` returns 403 when called with Patient or Admin JWT role
- [ ] `POST /api/auth/setup-credentials` accessible without a JWT (AllowAnonymous)
- [ ] `POST /api/auth/setup-credentials` with expired token returns 410
- [ ] `POST /api/auth/setup-credentials` with used token returns 409
- [ ] AuditLog record created for each admin action with correct userId, role, IP, UTC timestamp
- [ ] Raw token is never persisted in DB (only SHA-256 hash)
- [ ] No stack traces or internal details in API error responses (NFR-014)
- [ ] Integration tests pass (if applicable)

---

## Implementation Checklist

- [x] Create `AdminController` with `[Authorize(Roles = "Admin")]`; implement `POST /api/admin/users` and `POST /api/admin/users/{id}/resend-invite`
- [x] Create `CreateUserAccountCommandHandler`: uniqueness check, User INSERT, token generation (SHA-256 hash stored), SendGrid invite dispatch, AuditLog INSERT
- [x] Create `SetupCredentialsCommandHandler`: SHA-256 hash lookup, expiry/used-at checks, Argon2id hash, `User.passwordHash` UPDATE, `CredentialSetupToken.UsedAt` SET, AuditLog INSERT
- [x] Create `CreateWalkInPatientCommandHandler`: email uniqueness check (409 + `existingPatientId`), Patient INSERT, AuditLog INSERT
- [x] Create `CreateUserAccountValidator` (name, email, role enum check)
- [x] Create `SetupCredentialsValidator` (per-rule password checks — reuse same rule logic as US_010 backend validator)
- [x] Add `[Authorize(Roles = "Staff")]` to `POST /api/patients/create` endpoint
- [x] Add `[AllowAnonymous]` to `POST /api/auth/setup-credentials` endpoint
- [x] Map `DuplicateEmailException`, `TokenExpiredException`, `TokenAlreadyUsedException` to correct HTTP codes in global exception filter
- [x] Register `IUserRepository`, `ICredentialSetupTokenRepository`, new handlers, and validators in `Program.cs`
