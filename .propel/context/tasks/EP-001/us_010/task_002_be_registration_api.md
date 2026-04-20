# Task - task_002_be_registration_api

## Requirement Reference

- **User Story:** us_010 — Patient Self-Registration with Email Verification
- **Story Location:** `.propel/context/tasks/EP-001/us_010/us_010.md`
- **Acceptance Criteria:**
  - AC-1: `POST /api/auth/register` validates input, creates an inactive Patient account, and dispatches a verification email via SendGrid within 60 seconds
  - AC-2: `GET /api/auth/verify?token={token}` activates the account (`emailVerified = true`), and logs an immutable audit event with UTC timestamp, user ID, and client IP address
  - AC-3: Duplicate email registration returns HTTP 409 with the message "Email already registered" — no indication of active/inactive status
  - AC-4: Password complexity violations return HTTP 400 with FluentValidation per-rule error messages; no internal stack traces exposed to the client
- **Edge Cases:**
  - Verification token expires after 24 hours → return HTTP 410 (Gone); client can call `POST /api/auth/resend-verification` to trigger a new token
  - Second click on a used token → return HTTP 409 with message "Link already used"

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

| Layer               | Technology                  | Version |
| ------------------- | --------------------------- | ------- |
| Backend             | ASP.NET Core Web API        | .net 10  |
| Backend Messaging   | MediatR                     | 12.x    |
| Backend Validation  | FluentValidation            | 11.x    |
| ORM                 | Entity Framework Core       | 9.x     |
| Password Hashing    | BCrypt.Net-Next / Isopoh.Cryptography.Argon2 | Latest |
| Email Service       | SendGrid SDK for .NET       | Latest  |
| Logging             | Serilog                     | 4.x     |
| Rate Limiting       | ASP.NET Core Rate Limiting  | .net 10  |
| Testing — Unit      | xUnit + Moq                 | 2.x     |
| Database            | PostgreSQL                  | 16+     |
| AI/ML               | N/A                         | N/A     |
| Mobile              | N/A                         | N/A     |

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

Implement the backend registration and email-verification subsystem for EP-001 (Auth Module) using ASP.NET Core .net 10 with a MediatR CQRS pattern. The feature covers three endpoints:

1. `POST /api/auth/register` — validates input, hashes password with Argon2, creates a `Patient` record, generates a secure verification token, stores the token hash, and dispatches a SendGrid verification email asynchronously.
2. `GET /api/auth/verify?token={token}` — validates the token (existence, expiry, used status), activates the account, and writes an immutable AuditLog record.
3. `POST /api/auth/resend-verification` — issues a new token for a given email when the previous token has expired.

Security controls enforced: Argon2 password hashing (NFR-008), FluentValidation input sanitization (NFR-014), rate limiting on public endpoints (NFR-017), HIPAA-compliant audit logging (NFR-013, NFR-015), and side-channel-safe duplicate-email response (AC-3).

---

## Dependent Tasks

- **task_003_db_patient_schema** (EP-001/us_010) — `Patient` and `EmailVerificationToken` tables must exist before this task can be executed
- **US_006** — Patient entity must be present in the data layer

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `AuthController` | ASP.NET Core API (`Server/`) |
| CREATE | `RegisterPatientCommand` + `RegisterPatientCommandHandler` | Auth Module — Application Layer |
| CREATE | `VerifyEmailCommand` + `VerifyEmailCommandHandler` | Auth Module — Application Layer |
| CREATE | `ResendVerificationCommand` + `ResendVerificationCommandHandler` | Auth Module — Application Layer |
| CREATE | `RegistrationRequestValidator` (FluentValidation) | Auth Module — Application Layer |
| CREATE | `IEmailVerificationTokenRepository` + EF Core implementation | Auth Module — Infrastructure Layer |
| CREATE | `IPatientRepository` + EF Core implementation | Patient Module — Infrastructure Layer |
| CREATE | `IEmailService` interface + `SendGridEmailService` implementation | Notification Module — Infrastructure Layer |
| MODIFY | `AuditLogRepository` (INSERT-only, write-through) | Shared Infrastructure |
| MODIFY | `Program.cs` / `ServiceCollectionExtensions` | Register new services and rate limiting policies |

---

## Implementation Plan

1. **`RegisterPatientCommand`** (MediatR `IRequest<RegisterPatientResult>`):
   - Input: `Email`, `Password`, `Name`, `Phone`, `DateOfBirth`
   - Handler steps:
     a. Validate uniqueness: query `IPatientRepository.ExistsByEmailAsync(email)` (case-insensitive `lower()`) → throw `DuplicateEmailException` if exists
     b. Hash password: `Argon2.Hash(password, iterations=3, memory=65536, parallelism=2)` — Argon2id variant (NFR-008)
     c. Create `Patient` entity: `emailVerified = false`, `status = Active`
     d. Generate verification token: `RandomNumberGenerator.GetBytes(32)` → Base64Url encoded; store `SHA-256(token)` in `EmailVerificationToken.TokenHash` (never store raw token in DB)
     e. Set `ExpiresAt = UtcNow + 24h`
     f. Persist `Patient` + `EmailVerificationToken` in a single EF Core transaction
     g. Enqueue `SendVerificationEmailCommand` via `IMediator` for async execution (fire-and-forget with structured error logging; AC-1 ≤60s SLA)

2. **`VerifyEmailCommand`** (MediatR `IRequest<VerifyEmailResult>`):
   - Input: `Token` (raw, from query string)
   - Handler steps:
     a. Hash inbound token: `SHA-256(token)` → lookup by `TokenHash` in `EmailVerificationTokenRepository`
     b. If not found → `NotFoundException`
     c. If `ExpiresAt < UtcNow` → `TokenExpiredException` (HTTP 410)
     d. If `UsedAt != null` → `TokenAlreadyUsedException` (HTTP 409 "Link already used")
     e. In EF Core transaction: `Patient.emailVerified = true` + `EmailVerificationToken.UsedAt = UtcNow`
     f. INSERT `AuditLog`: `UserId = patient.Id`, `Action = "PatientEmailVerified"`, `EntityType = "Patient"`, `EntityId = patient.Id`, `IpAddress = HttpContext.Connection.RemoteIpAddress`, `Timestamp = UtcNow`

3. **`ResendVerificationCommand`**:
   - Input: `Email`
   - Handler: Invalidate existing unused tokens for patient (set `UsedAt = UtcNow` to prevent reuse), generate new token, persist, dispatch new email — only if patient exists and `emailVerified = false` (respond `200 OK` regardless to prevent email enumeration)

4. **`RegistrationRequestValidator`** (FluentValidation `AbstractValidator<RegisterPatientCommand>`):
   - `Email`: `NotEmpty()`, `EmailAddress()`, max 320 chars
   - `Password`: `NotEmpty()`, `MinimumLength(8)`, regex rules for uppercase, digit, special character — each as separate `Must()` rule with descriptive `WithMessage()` (surfaces to AC-4)
   - `Name`: `NotEmpty()`, max 200 chars
   - `Phone`: optional, `Matches(@"^\+?[1-9]\d{1,14}$")` when provided
   - `DateOfBirth`: `NotEmpty()`, must be in the past, age ≤ 130 years

5. **`AuthController`** endpoints:
   - `POST /api/auth/register` → dispatch `RegisterPatientCommand` → 201 Created
   - `GET /api/auth/verify` → dispatch `VerifyEmailCommand` → 200 OK
   - `POST /api/auth/resend-verification` → dispatch `ResendVerificationCommand` → 200 OK (always, per enumeration-safe design)
   - Global exception filter maps: `DuplicateEmailException → 409`, `TokenExpiredException → 410`, `TokenAlreadyUsedException → 409`, `ValidationException → 400` (never expose stack traces; NFR-014)

6. **Rate Limiting** (ASP.NET Core `RateLimiterMiddleware`):
   - `/api/auth/register`: fixed window — 5 requests per IP per 10 minutes
   - `/api/auth/resend-verification`: fixed window — 3 requests per email per 5 minutes (keyed on email hash, not plaintext)
   - Return `429 Too Many Requests` with `Retry-After` header

7. **SendGrid Integration** (`SendGridEmailService`):
   - Template-based email with verification link `{baseUrl}/auth/verify?token={rawToken}`
   - Respect `IEmailService` interface to enable mocking in tests
   - Log delivery result via Serilog; on failure, log structured error and do NOT re-throw (graceful degradation per NFR-018)

8. **Audit Logging** (NFR-013, NFR-015):
   - `AuditLog` entries are INSERT-only (no UPDATE/DELETE at repository level — AD-7)
   - Log fields: `userId`, `action`, `entityType`, `entityId`, `ipAddress` (from `HttpContext`), `details` (JSONB, non-PII summary), `timestamp` (UTC)

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no Server/ scaffold yet — greenfield ASP.NET Core project)
```

> Update this section with actual `Server/` tree after project scaffold is completed.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Modules/Auth/AuthController.cs` | REST endpoints: register, verify, resend-verification |
| CREATE | `Server/Modules/Auth/Commands/RegisterPatientCommand.cs` | MediatR command + result types |
| CREATE | `Server/Modules/Auth/Commands/RegisterPatientCommandHandler.cs` | Registration handler with Argon2 hashing, token generation, email dispatch |
| CREATE | `Server/Modules/Auth/Commands/VerifyEmailCommand.cs` | MediatR command + result types |
| CREATE | `Server/Modules/Auth/Commands/VerifyEmailCommandHandler.cs` | Token validation, account activation, audit log write |
| CREATE | `Server/Modules/Auth/Commands/ResendVerificationCommand.cs` | MediatR command |
| CREATE | `Server/Modules/Auth/Commands/ResendVerificationCommandHandler.cs` | Token invalidation + new token dispatch |
| CREATE | `Server/Modules/Auth/Validators/RegistrationRequestValidator.cs` | FluentValidation validator with per-rule password rules |
| CREATE | `Server/Modules/Auth/Exceptions/DuplicateEmailException.cs` | Domain exception |
| CREATE | `Server/Modules/Auth/Exceptions/TokenExpiredException.cs` | Domain exception |
| CREATE | `Server/Modules/Auth/Exceptions/TokenAlreadyUsedException.cs` | Domain exception |
| CREATE | `Server/Infrastructure/Repositories/PatientRepository.cs` | EF Core patient repository with `ExistsByEmailAsync` |
| CREATE | `Server/Infrastructure/Repositories/EmailVerificationTokenRepository.cs` | Token CRUD with SHA-256 hash lookup |
| CREATE | `Server/Infrastructure/Email/IEmailService.cs` | Email service abstraction |
| CREATE | `Server/Infrastructure/Email/SendGridEmailService.cs` | SendGrid SDK implementation |
| MODIFY | `Server/Program.cs` | Register MediatR, FluentValidation, rate limiting, SendGrid, Serilog |
| MODIFY | `Server/Infrastructure/Persistence/AppDbContext.cs` | Add `DbSet<Patient>`, `DbSet<EmailVerificationToken>`, `DbSet<AuditLog>` |

---

## External References

- [ASP.NET Core .net 10 — Minimal APIs and Controllers](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-9.0)
- [MediatR 12 — CQRS Pattern](https://github.com/jbogard/MediatR/wiki)
- [FluentValidation 11 — ASP.NET Core Integration](https://docs.fluentvalidation.net/en/latest/aspnet.html)
- [Argon2 Password Hashing — OWASP Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)
- [ASP.NET Core Rate Limiting (.NET 7+)](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-9.0)
- [SendGrid .NET SDK](https://github.com/sendgrid/sendgrid-csharp)
- [Serilog Structured Logging](https://serilog.net/)
- [Entity Framework Core 9 — Transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions)
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

- [ ] Unit tests pass for `RegisterPatientCommandHandler` (happy path, duplicate email, password complexity failure)
- [ ] Unit tests pass for `VerifyEmailCommandHandler` (success, expired token, already-used token)
- [ ] Unit tests pass for `ResendVerificationCommandHandler` (existing active patient, email-safe enumeration response)
- [ ] Unit tests pass for `RegistrationRequestValidator` (each password rule violation in isolation)
- [ ] Integration tests pass for `POST /api/auth/register` with valid payload → 201 Created
- [ ] Integration tests pass for `POST /api/auth/register` with duplicate email → 409 "Email already registered"
- [ ] Integration tests pass for `POST /api/auth/register` with invalid password → 400 with per-rule error messages
- [ ] Integration tests pass for `GET /api/auth/verify?token={valid}` → 200, patient.emailVerified = true
- [ ] Integration tests pass for `GET /api/auth/verify?token={expired}` → 410
- [ ] Integration tests pass for `GET /api/auth/verify?token={used}` → 409 "Link already used"
- [ ] Rate limiting returns 429 after 5 registration attempts from same IP within 10 minutes
- [ ] AuditLog record created with correct userId, IP, UTC timestamp on email verification success
- [ ] Raw verification token is never stored in database (only SHA-256 hash persisted)
- [ ] No stack traces or internal exception details returned in API error responses (NFR-014)
- [ ] SendGrid failure is logged but does not break the registration flow (NFR-018 graceful degradation)
- [ ] Integration tests pass (if applicable)

---

## Implementation Checklist

- [ ] Create `AuthController` with `POST /api/auth/register`, `GET /api/auth/verify`, `POST /api/auth/resend-verification` endpoints
- [ ] Create `RegisterPatientCommandHandler`: uniqueness check, Argon2 hashing, patient INSERT, token generation (raw → SHA-256 hash stored), email dispatch
- [ ] Create `RegistrationRequestValidator` with per-rule FluentValidation password checks
- [ ] Create `VerifyEmailCommandHandler`: SHA-256 hash lookup, expiry/used checks, `emailVerified=true` UPDATE, AuditLog INSERT with IP + UTC timestamp
- [ ] Create `ResendVerificationCommandHandler`: invalidate existing tokens, issue new token, dispatch email (enumeration-safe 200 response)
- [ ] Create `SendGridEmailService` implementing `IEmailService`; log delivery result; degrade gracefully on failure
- [ ] Apply ASP.NET Core rate limiting policies to `/register` (5/10min/IP) and `/resend-verification` (3/5min/email-hash)
- [ ] Map domain exceptions to HTTP status codes via global exception filter (no stack trace exposure)
- [ ] Register all dependencies in `Program.cs` (MediatR, FluentValidation, rate limiter, Serilog, EF Core)
- [ ] Write AuditLog as INSERT-only via repository (no UPDATE/DELETE permitted at the repository layer — AD-7)
