# Task - TASK_002

## Requirement Reference

- **User Story**: US_011 — User Login, JWT Tokens & Session Auto-Timeout
- **Story Location**: `.propel/context/tasks/EP-001/us_011/us_011.md`
- **Acceptance Criteria**:
  - AC-1: Given valid credentials submitted to `POST /api/auth/login`, Then return JWT access token (15-min expiry), rotating refresh token, and create a Redis session entry with 15-min TTL (`NFR-007`, `TR-007`, `AD-9`)
  - AC-2: Given the Redis session TTL has expired, When any protected endpoint is called, Then return HTTP 401 (session alive check in `AuthMiddleware`) so the client redirects to the login page
  - AC-3: Given an expired access token but a valid Redis session, When `POST /api/auth/refresh` is called, Then issue a new JWT and rotate the refresh token atomically, without requiring re-authentication
  - AC-4: Given `POST /api/auth/logout` is called, Then delete the Redis session key, revoke the refresh token in the database, and write a logout audit event (`FR-006`)
- **Edge Cases**:
  - Refresh token reuse (stolen token): If a revoked/already-rotated refresh token is presented to `POST /api/auth/refresh`, invalidate the entire token family (all refresh tokens for that `familyId`), delete all Redis session keys for the affected user, and write a `SecurityAlert` to `AuditLog`
  - Multi-device: each login creates an independent Redis session keyed by `{userId}:{deviceFingerprint}`; logout from one device does not affect other active sessions

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
| Frontend | N/A (backend task) | N/A |
| Backend | ASP.NET Core Web API | .net 10 |
| Backend Messaging | MediatR | 12.x |
| Backend Validation | FluentValidation | 11.x |
| ORM | Entity Framework Core | 9.x |
| Database | PostgreSQL | 16+ |
| Cache | Upstash Redis | Serverless |
| Logging | Serilog | 4.x |
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

Implement the three backend authentication endpoints for US_011 using ASP.NET Core Web API (.net 10) following the CQRS pattern via MediatR 12.x. This task covers:

1. `POST /api/auth/login` — credential validation (Argon2 hash comparison), JWT generation (15-min expiry with role claim), refresh token generation (CSPRNG), Redis session creation (15-min TTL), and audit log entry.
2. `POST /api/auth/refresh` — refresh token validation, Redis session alive check, atomic token rotation (revoke old / insert new in the same DB transaction), new JWT issuance. Includes full reuse-detection flow (token family invalidation + security alert).
3. `POST /api/auth/logout` — Redis session deletion, refresh token revocation, logout audit event.

A custom `SessionAliveMiddleware` validates that a Redis session key exists on every protected request, returning HTTP 401 when the key is missing (enforcing the 15-minute server-side session TTL independently of JWT expiry).

All three endpoints are protected by FluentValidation request validators and produce structured error responses.

## Dependent Tasks

- **TASK_003** — `refresh_tokens` table migration must be applied before this backend can persist or query refresh tokens via EF Core.

## Impacted Components

| Component | Status | Location |
|-----------|--------|----------|
| `AuthController` | NEW | `Server/Modules/Auth/AuthController.cs` |
| `LoginCommand` + `LoginCommandHandler` | NEW | `Server/Modules/Auth/Commands/Login/` |
| `RefreshTokenCommand` + `RefreshTokenCommandHandler` | NEW | `Server/Modules/Auth/Commands/RefreshToken/` |
| `LogoutCommand` + `LogoutCommandHandler` | NEW | `Server/Modules/Auth/Commands/Logout/` |
| `JwtService` | NEW | `Server/Modules/Auth/Services/JwtService.cs` |
| `RedisSessionService` | NEW | `Server/Modules/Auth/Services/RedisSessionService.cs` |
| `SessionAliveMiddleware` | NEW | `Server/Middleware/SessionAliveMiddleware.cs` |
| `LoginCommandValidator` | NEW | `Server/Modules/Auth/Commands/Login/LoginCommandValidator.cs` |
| `RefreshTokenCommandValidator` | NEW | `Server/Modules/Auth/Commands/RefreshToken/RefreshTokenCommandValidator.cs` |
| `RefreshToken` (EF Core entity) | NEW | `Server/Domain/Entities/RefreshToken.cs` |
| `AppDbContext` | MODIFY | `Server/Infrastructure/Data/AppDbContext.cs` — add `DbSet<RefreshToken>` |
| `Program.cs` | MODIFY | Register JWT authentication, MediatR, Redis `IConnectionMultiplexer`, FluentValidation pipeline behavior |

## Implementation Plan

1. **JWT Configuration (Program.cs)**: Register `AddAuthentication(JwtBearerDefaults.AuthenticationScheme)` + `AddJwtBearer`. Configure `TokenValidationParameters` with `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`, `ValidateIssuerSigningKey`, and a 0-second `ClockSkew` (no tolerance beyond the stated 15-min expiry). Read signing key from `IConfiguration["Jwt:SigningKey"]` (environment variable, never hard-coded — OWASP A02).

2. **JwtService**: Wrap `System.IdentityModel.Tokens.Jwt`. `GenerateAccessToken(userId, role, jti)` creates a `JwtSecurityToken` with claims `sub`, `role`, `jti`, `iat`, and `exp = utcNow + 15 min`. `GenerateRefreshToken()` returns `Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))` (cryptographically secure, 512-bit entropy — OWASP A02).

3. **POST /api/auth/login (LoginCommandHandler)**:
   - Retrieve `User` by email from `PostgreSQL` via EF Core (case-insensitive index lookup).
   - Verify password using `Argon2.Verify(storedHash, inputPassword)` (via `Konscious.Security.Cryptography` or `BCrypt.Net-Next` — match whatever is in the user store from US_010).
   - On mismatch: return `HTTP 401 Unauthorized` with generic message (no user enumeration — OWASP A07).
   - Generate `accessToken` + `refreshToken`. Compute `refreshTokenHash = SHA-256(refreshToken)` before DB storage (never store raw token).
   - Persist `RefreshToken` entity: `{ userId, tokenHash, familyId = newGuid, deviceId, expiresAt = utcNow + 7d, createdAt }`.
   - Call `RedisSessionService.SetAsync($"session:{userId}:{deviceId}", sessionPayload, TTL=15min)`.
   - Write `AuditLog` entry: `action=LOGIN`, `userId`, `role`, `ipAddress` (from `HttpContext.Connection.RemoteIpAddress`), `timestamp=utcNow` (FR-006).
   - Return `200 OK` with `{ accessToken, refreshToken, expiresIn: 900 }`.

4. **SessionAliveMiddleware**: On every request to a `[Authorize]`-protected route, extract `userId` + `deviceId` from JWT claims, call `RedisSessionService.ExistsAsync($"session:{userId}:{deviceId}")`. If key does not exist (TTL expired or explicit logout), short-circuit with `HTTP 401` and JSON body `{ error: "session_expired" }`. If key exists, call `RedisSessionService.ResetTTLAsync(...)` to slide the 15-minute window on active use (NFR-007).

5. **POST /api/auth/refresh (RefreshTokenCommandHandler)**:
   - Hash the incoming `refreshToken` using SHA-256 and look up in `refresh_tokens` table.
   - If not found: return `HTTP 401`.
   - If `revokedAt IS NOT NULL` (already used): **Reuse detected** — call `RevokeTokenFamily(familyId)` (mark all tokens in family as revoked), call `RedisSessionService.DeleteAllUserSessionsAsync(userId)`, write `AuditLog` entry `action=SECURITY_ALERT_REFRESH_TOKEN_REUSE`, return `HTTP 401` with `{ error: "token_reuse_detected" }`.
   - If `expiresAt < utcNow`: return `HTTP 401` with `{ error: "refresh_token_expired" }`.
   - Verify Redis session exists (alive check).
   - In a single DB transaction: mark old `RefreshToken.revokedAt = utcNow`, insert new `RefreshToken` (same `familyId`, new `tokenHash`, new `expiresAt`).
   - Issue new JWT (`GenerateAccessToken`).
   - Reset Redis session TTL to 15 minutes.
   - Return `200 OK` with `{ accessToken, refreshToken, expiresIn: 900 }`.

6. **POST /api/auth/logout (LogoutCommandHandler)**:
   - Extract `userId` + `deviceId` from the Authorization header JWT claims.
   - Call `RedisSessionService.DeleteAsync($"session:{userId}:{deviceId}")`.
   - Hash the incoming `refreshToken` and mark corresponding DB record `revokedAt = utcNow`.
   - Write `AuditLog` entry: `action=LOGOUT`, `userId`, `role`, `ipAddress`, `timestamp=utcNow` (FR-006, AC-4).
   - Return `204 No Content`.

7. **FluentValidation Validators**:
   - `LoginCommandValidator`: `RuleFor(x => x.Email).NotEmpty().EmailAddress()`, `RuleFor(x => x.Password).NotEmpty().MinimumLength(8)`.
   - `RefreshTokenCommandValidator`: `RuleFor(x => x.RefreshToken).NotEmpty()`.
   - Wire `AddFluentValidation` / `AddValidatorsFromAssemblyContaining<LoginCommandValidator>()` in `Program.cs` with `ValidationBehavior<TRequest, TResponse>` MediatR pipeline behavior.

8. **Rate Limiting (OWASP A04, NFR-017)**: Apply `[EnableRateLimiting("login")]` to `POST /api/auth/login` using ASP.NET Core's built-in `RateLimiterMiddleware`. Configure a fixed-window limiter: max 10 requests per IP per minute. Add `RateLimitingMiddleware` registration in `Program.cs` using `AddRateLimiter(options => options.AddFixedWindowLimiter("login", ...))`.

## Current Project State

```
Server/                   ← ASP.NET Core .net 10 Web API root (to be scaffolded)
└── (no source files yet)
```

> This is a greenfield project. No existing backend source files. All classes and modules are new.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Modules/Auth/AuthController.cs` | Minimal API / controller with three auth endpoints |
| CREATE | `Server/Modules/Auth/Commands/Login/LoginCommand.cs` | MediatR command record: `{ Email, Password, DeviceId }` |
| CREATE | `Server/Modules/Auth/Commands/Login/LoginCommandHandler.cs` | Credential check, JWT + refresh token generation, Redis session, audit log |
| CREATE | `Server/Modules/Auth/Commands/Login/LoginCommandValidator.cs` | FluentValidation: email format, password min length |
| CREATE | `Server/Modules/Auth/Commands/RefreshToken/RefreshTokenCommand.cs` | MediatR command: `{ RefreshToken, DeviceId }` |
| CREATE | `Server/Modules/Auth/Commands/RefreshToken/RefreshTokenCommandHandler.cs` | Token validation, reuse detection, rotation, new JWT |
| CREATE | `Server/Modules/Auth/Commands/RefreshToken/RefreshTokenCommandValidator.cs` | FluentValidation: non-empty refresh token |
| CREATE | `Server/Modules/Auth/Commands/Logout/LogoutCommand.cs` | MediatR command: `{ UserId, DeviceId, RefreshToken }` |
| CREATE | `Server/Modules/Auth/Commands/Logout/LogoutCommandHandler.cs` | Redis session delete, token revoke, audit log |
| CREATE | `Server/Modules/Auth/Services/JwtService.cs` | `GenerateAccessToken()`, `GenerateRefreshToken()` |
| CREATE | `Server/Modules/Auth/Services/RedisSessionService.cs` | `SetAsync()`, `ExistsAsync()`, `ResetTTLAsync()`, `DeleteAsync()`, `DeleteAllUserSessionsAsync()` |
| CREATE | `Server/Middleware/SessionAliveMiddleware.cs` | Per-request Redis session alive check; sliding TTL reset |
| CREATE | `Server/Domain/Entities/RefreshToken.cs` | EF Core entity matching `refresh_tokens` schema from TASK_003 |
| MODIFY | `Server/Infrastructure/Data/AppDbContext.cs` | Add `DbSet<RefreshToken> RefreshTokens` |
| MODIFY | `Server/Program.cs` | Register JWT auth, MediatR, Redis, FluentValidation, rate limiter, `SessionAliveMiddleware` |

## External References

- [ASP.NET Core JWT Bearer Authentication (.net 10)](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/jwt-authn?view=aspnetcore-9.0) — `AddAuthentication`, `AddJwtBearer`, `TokenValidationParameters`
- [MediatR 12.x — Send and Publish](https://github.com/jbogard/MediatR/wiki) — `IRequest<T>`, `IRequestHandler<T,R>`, pipeline behaviors
- [FluentValidation 11.x with ASP.NET Core](https://docs.fluentvalidation.net/en/latest/aspnet.html) — `AddFluentValidationAutoValidation`, `AbstractValidator<T>`
- [ASP.NET Core Rate Limiting Middleware (.NET 7+)](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-9.0) — `AddRateLimiter`, `AddFixedWindowLimiter`
- [Upstash Redis .NET SDK (StackExchange.Redis)](https://upstash.com/docs/redis/howto/connectwithupstash#net) — TLS connection string, `IDatabase.StringSetAsync`, `KeyExpireAsync`
- [Entity Framework Core 9.x — Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli) — `Add-Migration`, `Update-Database`
- [OWASP Authentication Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html) — Generic error messages, rate limiting, secure token storage
- [OWASP Refresh Token Rotation](https://auth0.com/docs/secure/tokens/refresh-tokens/refresh-token-rotation) — Family-based reuse detection pattern
- [RFC 7519 — JWT](https://datatracker.ietf.org/doc/html/rfc7519) — Claims standard: `sub`, `iat`, `exp`, `jti`
- [Serilog 4.x Structured Logging](https://serilog.net/) — `Log.ForContext<T>().Information(...)` pattern for audit events

## Build Commands

```bash
# Scaffold .net 10 Web API (greenfield)
dotnet new webapi -n PropelIQ.Server --framework net9.0

# Add required NuGet packages
dotnet add package MediatR --version 12.*
dotnet add package FluentValidation.AspNetCore --version 11.*
dotnet add package Microsoft.EntityFrameworkCore.Design --version 9.*
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 9.*
dotnet add package StackExchange.Redis --version 2.*
dotnet add package Serilog.AspNetCore --version 8.*
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 9.*
dotnet add package Konscious.Security.Cryptography.Argon2 --version 1.*

# Run migrations (after TASK_003 migration is created)
dotnet ef database update

# Build
dotnet build

# Run locally
dotnet run --project Server/PropelIQ.Server.csproj
```

## Implementation Validation Strategy

- [ ] Unit tests pass (to be planned separately via `plan-unit-test` workflow)
- [ ] `POST /api/auth/login` returns `200 OK` with `accessToken` + `refreshToken` for valid credentials
- [ ] `POST /api/auth/login` returns `401 Unauthorized` for invalid credentials (no user enumeration leak)
- [ ] JWT `exp` claim is exactly `utcNow + 900 seconds` (15 min)
- [ ] Redis key `session:{userId}:{deviceId}` exists with TTL ≤ 900 seconds after login
- [ ] `POST /api/auth/refresh` rotates token: old `revokedAt` is set; new token inserted in DB; new JWT returned
- [ ] `POST /api/auth/refresh` with a reused (already-rotated) token: all family tokens revoked; Redis sessions deleted; `SECURITY_ALERT` in AuditLog; `HTTP 401` returned
- [ ] `SessionAliveMiddleware` returns `HTTP 401` when Redis key is absent (simulate by deleting key manually)
- [ ] `SessionAliveMiddleware` slides TTL on each active request (TTL resets to 900 s after each call)
- [ ] `POST /api/auth/logout` deletes Redis key, sets `revokedAt` in DB, writes `LOGOUT` AuditLog entry, returns `204`
- [ ] Login rate limiter blocks after 10 requests per IP per minute; returns `429 Too Many Requests`
- [ ] FluentValidation rejects `POST /api/auth/login` with missing/invalid email or password shorter than 8 chars; returns `400 Bad Request`

## Implementation Checklist

- [ ] Scaffold `AuthController` with three route handlers dispatching to MediatR commands
- [ ] Implement `JwtService`: `GenerateAccessToken()` (15-min, role claim, jti) and `GenerateRefreshToken()` (CSPRNG 512-bit)
- [ ] Implement `LoginCommandHandler`: Argon2 verify → JWT + refresh token → Redis session + AuditLog write
- [ ] Implement `RedisSessionService`: Set/Exists/ResetTTL/Delete/DeleteAllUserSessions using `StackExchange.Redis`
- [ ] Implement `SessionAliveMiddleware`: Redis alive check + sliding TTL on every protected request
- [ ] Implement `RefreshTokenCommandHandler`: reuse detection (family invalidation) + atomic rotation + new JWT
- [ ] Implement `LogoutCommandHandler`: Redis delete + token revoke + logout AuditLog entry
- [ ] Apply FluentValidation validators and wire `ValidationBehavior` pipeline in `Program.cs`
- [ ] Configure login rate limiter (10 req/IP/min fixed-window) in `Program.cs`
