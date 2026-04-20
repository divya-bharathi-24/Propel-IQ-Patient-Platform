# Task - TASK_001

## Requirement Reference

- **User Story**: US_013 — Authentication Event Audit Logging
- **Story Location**: `.propel/context/tasks/EP-001/us_013/us_013.md`
- **Acceptance Criteria**:
  - AC-1: Given successful login, Then INSERT `AuditLog` with `action=Login`, `userId`, `role`, `ipAddress`, UTC `timestamp` — record is immutable (no UPDATE or DELETE)
  - AC-2: Given failed login (wrong password or unknown email), Then INSERT `AuditLog` with `action=FailedLogin`, attempted email in `details` JSONB (never password), `ipAddress`, UTC `timestamp`
  - AC-3: Given Redis session TTL expires after 15 minutes, Then INSERT `AuditLog` with `action=SessionTimeout`, `userId`, UTC `timestamp` within 5 seconds of expiry
  - AC-4: Given explicit logout, Then INSERT `AuditLog` with `action=Logout` **before** session is invalidated
- **Edge Cases**:
  - Audit log write fails (DB unavailable): retry once; if second attempt also fails, authentication action still succeeds but a `CRITICAL` Serilog alert is emitted (security over audit atomicity)
  - Same IP triggers >10 failed logins in 5 minutes: rate limiter blocks; INSERT `AuditLog` with `action=RateLimitBlock`, `ipAddress`, UTC `timestamp` (userId NULL — blocker may be from unknown user)

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
| Backend | ASP.NET Core Web API | .NET 9 |
| Backend Messaging | MediatR | 12.x |
| ORM | Entity Framework Core | 9.x |
| Database | PostgreSQL | 16+ |
| Cache | Upstash Redis | Serverless |
| Logging | Serilog | 4.x |
| Background Services | ASP.NET Core IHostedService | .NET 9 |
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

Implement the `IAuditLogRepository` write-only service and integrate all authentication audit event types required by US_013 and FR-006 into the existing auth pipeline (from US_011 TASK_002). The five event types covered are: `Login`, `FailedLogin`, `SessionTimeout`, `Logout`, and `RateLimitBlock`.

Key architectural decisions:
- **AD-7 (INSERT-only)**: `IAuditLogRepository` exposes only `AppendAsync` — no read, update, or delete methods at the application layer.
- **Retry-once with critical alert**: `AuditLogService` wraps `AppendAsync` with a single retry (100 ms delay). On two consecutive failures, the authentication result is still returned to the caller, but a `Serilog` `Critical` event is logged with the unsaved audit payload (no PII — email addresses are SHA-256-hashed before logging to the alert channel).
- **SessionTimeout via Redis keyspace notifications**: A `SessionExpirySubscriberService` (`IHostedService`) subscribes to Upstash Redis `__keyevent@0__:expired` channel. When a key matching `session:*` expires, it parses `userId` and `deviceId` from the key name, then calls `AuditLogService.AppendAsync` with `action=SessionTimeout` within the 5-second SLA (AC-3).
- **RateLimitBlock event**: An ASP.NET Core `OnRejected` delegate on the login rate limiter calls `AuditLogService.AppendAsync` with `action=RateLimitBlock`.
- **FailedLogin**: The existing `LoginCommandHandler` (US_011 TASK_002) must be extended to write a `FailedLogin` audit record on credential mismatch. For unknown emails, `userId` is NULL; the attempted email is SHA-256-hashed before writing to the `details` JSONB field (OWASP A04: prevent email enumeration via audit log side-channels).

## Dependent Tasks

- **US_010 / TASK_003** — `audit_logs` table, INSERT-only trigger, and `AuditLog` EF Core entity must exist before this task.
- **US_013 / TASK_002** — `audit_logs.user_id` must be nullable and `audit_logs.role` column must exist before this task writes auth-specific audit records.
- **US_011 / TASK_002** — `LoginCommandHandler`, `LogoutCommandHandler`, `SessionAliveMiddleware`, and `RedisSessionService` must exist as integration points.

## Impacted Components

| Component | Status | Location |
|-----------|--------|----------|
| `IAuditLogRepository` | NEW | `Server/Shared/Audit/IAuditLogRepository.cs` |
| `AuditLogRepository` | NEW | `Server/Infrastructure/Repositories/AuditLogRepository.cs` |
| `AuditLogService` | NEW | `Server/Shared/Audit/AuditLogService.cs` |
| `AuthAuditActions` (constants) | NEW | `Server/Shared/Audit/AuthAuditActions.cs` |
| `SessionExpirySubscriberService` | NEW | `Server/Infrastructure/BackgroundServices/SessionExpirySubscriberService.cs` |
| `LoginCommandHandler` | MODIFY | `Server/Modules/Auth/Commands/Login/LoginCommandHandler.cs` — add FailedLogin + Login audit calls |
| `LogoutCommandHandler` | MODIFY | `Server/Modules/Auth/Commands/Logout/LogoutCommandHandler.cs` — ensure audit write precedes session delete |
| `Program.cs` | MODIFY | Register `IAuditLogRepository`, `AuditLogService`, `SessionExpirySubscriberService`; wire `OnRejected` delegate |

## Implementation Plan

1. **`IAuditLogRepository` (write-only interface)**:

   ```csharp
   public interface IAuditLogRepository
   {
       Task AppendAsync(AuditLog entry, CancellationToken ct = default);
   }
   ```

   - Only one method: `AppendAsync`. No read, update, or delete methods (AD-7 write-only repository pattern).
   - `AuditLogRepository` implementation calls `_dbContext.AuditLogs.AddAsync(entry)` followed by `_dbContext.SaveChangesAsync()`. Uses a dedicated `DbContext` scope (not the request-scoped one) to prevent audit writes from being rolled back if the outer business transaction is aborted.

2. **`AuditLogService` (retry-once wrapper)**:

   ```csharp
   public sealed class AuditLogService
   {
       public async Task AppendAsync(AuditLog entry, CancellationToken ct = default)
       {
           try { await _repo.AppendAsync(entry, ct); return; }
           catch (Exception ex1)
           {
               _logger.LogWarning(ex1, "Audit log write failed (attempt 1), retrying…");
               await Task.Delay(100, ct);
           }
           try { await _repo.AppendAsync(entry, ct); }
           catch (Exception ex2)
           {
               // CRITICAL alert — include non-PII payload summary only
               _logger.LogCritical(ex2, "AUDIT_LOG_WRITE_FAILURE: action={Action} userId={UserId}",
                   entry.Action, entry.UserId);
               // Do NOT re-throw — auth action must succeed per edge case spec
           }
       }
   }
   ```

   - Retry delay: 100 ms (sufficient for transient DB connection blip).
   - On second failure: log `Critical` with `Action` and `UserId` only — no IP address or email in the Serilog alert channel (prevents secondary PHI leak through log aggregation — OWASP A09).

3. **`AuthAuditActions` constants class**:

   ```csharp
   public static class AuthAuditActions
   {
       public const string Login           = "Login";
       public const string FailedLogin     = "FailedLogin";
       public const string SessionTimeout  = "SessionTimeout";
       public const string Logout          = "Logout";
       public const string RateLimitBlock  = "RateLimitBlock";
   }
   ```

   - Eliminates magic string constants across all auth event handlers (rules/code-anti-patterns.md).

4. **`LoginCommandHandler` modifications**:

   **Success path** (after JWT + refresh token issued):
   ```csharp
   await _auditLog.AppendAsync(new AuditLog {
       UserId      = user.Id,
       Action      = AuthAuditActions.Login,
       EntityType  = "User",
       EntityId    = user.Id,
       Role        = user.Role,
       IpAddress   = _httpContextAccessor.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
       Details     = null,
       Timestamp   = DateTime.UtcNow
   }, ct);
   ```

   **Failure path** (credential mismatch — after generic 401 is prepared but before returning):
   ```csharp
   var emailHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.Email.ToLowerInvariant())));
   await _auditLog.AppendAsync(new AuditLog {
       UserId      = foundUser?.Id,   // nullable — null if email not found
       Action      = AuthAuditActions.FailedLogin,
       EntityType  = "User",
       EntityId    = foundUser?.Id ?? Guid.Empty,
       Role        = foundUser?.Role,
       IpAddress   = _httpContextAccessor.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
       Details     = JsonDocument.Parse($"{{\"emailHash\":\"{emailHash}\"}}"),
       Timestamp   = DateTime.UtcNow
   }, ct);
   // Return generic 401 AFTER audit write — never reveal which field failed
   ```

5. **`LogoutCommandHandler` modification** — audit write BEFORE session delete (AC-4 requirement):

   ```csharp
   // Step 1 — audit FIRST
   await _auditLog.AppendAsync(new AuditLog {
       UserId     = command.UserId,
       Action     = AuthAuditActions.Logout,
       EntityType = "User",
       EntityId   = command.UserId,
       Role       = command.Role,
       IpAddress  = command.IpAddress,
       Timestamp  = DateTime.UtcNow
   }, ct);
   // Step 2 — then invalidate
   await _redisSession.DeleteAsync($"session:{command.UserId}:{command.DeviceId}");
   await _refreshTokenRepo.RevokeAsync(command.RefreshTokenHash);
   ```

6. **`SessionExpirySubscriberService` (IHostedService — keyspace notifications)**:

   - On `StartAsync`: enable keyspace notifications on Upstash Redis via `CONFIG SET notify-keyspace-events KEx` (Key events + eXpiry events). Subscribe to channel `__keyevent@0__:expired` using `StackExchange.Redis` `ISubscriber.SubscribeAsync`.
   - On message received: check if key matches pattern `^session:([0-9a-f-]{36}):(.+)$`. If matched: extract `userId` and `deviceId` from key name. Call `AuditLogService.AppendAsync` with `action=SessionTimeout`, `userId`, UTC timestamp.
   - Upstash Redis note: Keyspace notifications are supported on Upstash paid plans and in the Upstash REST API; for the free tier, add a fallback: `SessionAliveMiddleware` (US_011 TASK_002) also calls `AuditLogService.AppendAsync(SessionTimeout)` when it detects a missing Redis key and the JWT claim `userId` is present. This dual-path ensures AC-3 is met regardless of Upstash tier.
   - On `StopAsync`: unsubscribe from channel, dispose subscriber.

7. **`RateLimitBlock` audit event** — wire into the login rate limiter's `OnRejected` delegate in `Program.cs`:

   ```csharp
   options.AddFixedWindowLimiter("login", opt => { ... })
       .RejectionStatusCode = 429;
   options.OnRejected = async (ctx, ct) =>
   {
       var auditSvc = ctx.HttpContext.RequestServices.GetRequiredService<AuditLogService>();
       await auditSvc.AppendAsync(new AuditLog {
           UserId    = null,   // IP-based block — user identity not yet established
           Action    = AuthAuditActions.RateLimitBlock,
           EntityType = "RateLimiter",
           EntityId  = Guid.Empty,
           IpAddress = ctx.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
           Timestamp = DateTime.UtcNow
       }, ct);
   };
   ```

8. **DI Registration in `Program.cs`**:

   ```csharp
   builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
   builder.Services.AddScoped<AuditLogService>();
   builder.Services.AddHostedService<SessionExpirySubscriberService>();
   builder.Services.AddHttpContextAccessor(); // Required for IHttpContextAccessor in handlers
   ```

## Current Project State

```
Server/
├── Modules/
│   └── Auth/
│       └── Commands/
│           ├── Login/       ← LoginCommandHandler.cs (MODIFY)
│           └── Logout/      ← LogoutCommandHandler.cs (MODIFY)
├── Infrastructure/
│   └── Repositories/        ← AuditLogRepository.cs (NEW)
└── Shared/
    └── Audit/               ← NEW — IAuditLogRepository, AuditLogService, AuthAuditActions
```

> Greenfield — no existing source files. All paths are target locations per project scaffold convention.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Shared/Audit/IAuditLogRepository.cs` | Write-only interface: `AppendAsync` only (AD-7) |
| CREATE | `Server/Infrastructure/Repositories/AuditLogRepository.cs` | EF Core INSERT-only implementation using dedicated DbContext scope |
| CREATE | `Server/Shared/Audit/AuditLogService.cs` | Retry-once wrapper with `Critical` Serilog alert on second failure |
| CREATE | `Server/Shared/Audit/AuthAuditActions.cs` | Compile-time string constants for all 5 auth event action names |
| CREATE | `Server/Infrastructure/BackgroundServices/SessionExpirySubscriberService.cs` | Redis keyspace notification subscriber: expired `session:*` key → SessionTimeout AuditLog |
| MODIFY | `Server/Modules/Auth/Commands/Login/LoginCommandHandler.cs` | Add Login (success) and FailedLogin (failure) audit write calls with SHA-256-hashed email in FailedLogin details |
| MODIFY | `Server/Modules/Auth/Commands/Logout/LogoutCommandHandler.cs` | Ensure Logout audit write executes before Redis session delete and refresh token revocation |
| MODIFY | `Server/Program.cs` | Register `IAuditLogRepository`, `AuditLogService`, `SessionExpirySubscriberService`; wire `OnRejected` delegate for `RateLimitBlock` event |

## External References

- [ASP.NET Core IHostedService](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-9.0) — Background service lifecycle: `StartAsync`, `StopAsync`, hosted service registration
- [StackExchange.Redis — Pub/Sub and Keyspace Notifications](https://stackexchange.github.io/StackExchange.Redis/PubSubOrder.html) — `ISubscriber.SubscribeAsync`, channel pattern matching
- [Upstash Redis — Keyspace Notifications](https://upstash.com/docs/redis/features/keyspace-notifications) — Enabling `notify-keyspace-events KEx` on Upstash; limitations on free tier
- [Serilog 4.x — Log Levels and Structured Events](https://serilog.net/Serilog.Docs.pdf) — `LogCritical` with structured properties for HIPAA-compliant alerting
- [OWASP: Logging & Monitoring Failures (A09)](https://owasp.org/Top10/A09_2021-Security_Logging_and_Monitoring_Failures/) — What to log, what NOT to log (no passwords, no sensitive PII in alert channel)
- [OWASP: Authentication Cheat Sheet — Audit Logging](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html#logging-and-monitoring) — Required fields for auth audit events
- [RFC 6749 — OAuth 2.0 Error Responses](https://datatracker.ietf.org/doc/html/rfc6749#section-5.2) — Generic error response shape; never reveal which credential field failed
- [EF Core — DbContext Lifetime in Background Services](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#using-a-dbcontext-factory) — `IDbContextFactory<T>` for non-request-scoped DB writes in `SessionExpirySubscriberService`
- [HIPAA Security Rule — Audit Controls (§164.312(b))](https://www.hhs.gov/hipaa/for-professionals/security/laws-regulations/index.html) — Mandatory audit log requirements for all authentication events

## Build Commands

```bash
# Build the Server project
dotnet build Server/PropelIQ.Server.csproj

# Run locally (ensure Upstash Redis TLS connection string is set in environment)
dotnet run --project Server/PropelIQ.Server.csproj

# Verify IHostedService registration (check startup logs for SessionExpirySubscriberService)
dotnet run --project Server/PropelIQ.Server.csproj 2>&1 | grep -i "SessionExpiry"

# Run tests (to be created via plan-unit-test workflow)
dotnet test --project Server.Tests/Server.Tests.csproj
```

## Implementation Validation Strategy

- [ ] Unit tests pass (to be planned separately via `plan-unit-test` workflow)
- [ ] `POST /api/auth/login` (success): `audit_logs` row inserted with `action='Login'`, `user_id` set, `role` set, `ip_address` set, `timestamp` is UTC
- [ ] `POST /api/auth/login` (wrong password): `audit_logs` row inserted with `action='FailedLogin'`, `user_id` is either set (known email) or NULL (unknown email), `details.emailHash` contains SHA-256 hash of attempted email, `password` is absent from all log fields
- [ ] `POST /api/auth/login` (wrong password): generic `401` response contains no indication of whether email or password was incorrect (OWASP A07)
- [ ] Redis session key expiry: within 5 seconds, `audit_logs` row inserted with `action='SessionTimeout'`, `user_id` and `timestamp` set — verified by manually deleting a session key in Redis and observing DB
- [ ] `POST /api/auth/logout`: `audit_logs` row is inserted BEFORE the Redis session key is deleted — verified by checking DB row timestamp vs Redis key deletion timestamp
- [ ] Rate limiter rejection: `audit_logs` row inserted with `action='RateLimitBlock'`, `ip_address` set, `user_id` NULL
- [ ] DB unavailable simulation: auth endpoint returns correct HTTP response; Serilog `Critical` event emitted; no uncaught exception propagates; second failure does not re-throw
- [ ] `audit_logs` UPDATE attempt raises PostgreSQL exception (immutability trigger from TASK_002 DB migration)

## Implementation Checklist

- [ ] Create `IAuditLogRepository` interface with single `AppendAsync` method (INSERT-only, AD-7 compliant)
- [ ] Create `AuditLogRepository` using `IDbContextFactory<AppDbContext>` for non-request-scoped audit writes (prevents rollback of outer business transaction from discarding audit entry)
- [ ] Create `AuditLogService` with retry-once (100 ms delay) and `LogCritical` on second failure — no PII/PHI in critical alert payload
- [ ] Create `AuthAuditActions` string constants class (Login, FailedLogin, SessionTimeout, Logout, RateLimitBlock)
- [ ] Modify `LoginCommandHandler`: add Login audit on success; add FailedLogin audit on credential mismatch (SHA-256-hashed email in details, nullable userId)
- [ ] Modify `LogoutCommandHandler`: confirm audit write is the FIRST async step before session/token deletion
- [ ] Create `SessionExpirySubscriberService` (`IHostedService`): subscribe to Redis `__keyevent@0__:expired`, match `session:*` key pattern, write `SessionTimeout` AuditLog; include `SessionAliveMiddleware` fallback path for free-tier Upstash
- [ ] Register all new services in `Program.cs`; wire `OnRejected` delegate for `RateLimitBlock` audit event
