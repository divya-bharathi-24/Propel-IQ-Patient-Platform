# Task - task_001_be_rate_limiting_validation_pipeline

## Requirement Reference

- **User Story:** us_014 — Rate Limiting, Input Validation & Encryption Controls
- **Story Location:** `.propel/context/tasks/EP-001/us_014/us_014.md`
- **Acceptance Criteria:**
  - AC-1: `POST /api/auth/login` (and all public auth endpoints) reject the 11th request within 60 seconds with HTTP 429 and a `Retry-After` header; the rate-limit event is logged via Serilog
  - AC-2: Any API endpoint that receives an invalid or missing required field is rejected with HTTP 400 and a structured error body listing each violated field and message — no internal stack trace exposed
- **Edge Cases:**
  - Excessively long strings: `MaxLength` rules in FluentValidation validators reject the payload at HTTP 400 before it reaches the handler
  - Rate limiter misconfiguration: integration test asserts legitimate traffic (under threshold) is NOT blocked; alert fires if >50% of requests return 429 within 1 minute

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

| Layer              | Technology                   | Version |
| ------------------ | ---------------------------- | ------- |
| Backend            | ASP.NET Core Web API         | .net 10  |
| Rate Limiting      | ASP.NET Core Rate Limiting   | .net 10  |
| Validation         | FluentValidation             | 11.x    |
| Messaging          | MediatR                      | 12.x    |
| Logging            | Serilog                      | 4.x     |
| Cache (rate state) | Upstash Redis                | Serverless |
| Testing — Unit     | xUnit + Moq                  | 2.x     |
| AI/ML              | N/A                          | N/A     |
| Mobile             | N/A                          | N/A     |

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

Centralise two cross-cutting security controls in the ASP.NET Core middleware pipeline:

1. **Rate Limiting** — configure ASP.NET Core's built-in `RateLimiterMiddleware` with named policies for every public-facing auth endpoint. Fixed-window policies on `/api/auth/login`, `/api/auth/register`, and `/api/auth/resend-verification`. Return HTTP 429 with `Retry-After`, log the rejection event via Serilog, and integrate Redis-backed distributed counting so limits hold under horizontal scaling (NFR-017).

2. **Global Validation Error Pipeline** — configure FluentValidation's ASP.NET Core integration to suppress the default `ModelStateDictionary` error format and emit a consistent `{ errors: [{ field, message }] }` JSON structure on HTTP 400. Register a global exception filter that maps `ValidationException` and all known domain exceptions to structured HTTP responses with no stack traces exposed (NFR-014, TR-020).

---

## Dependent Tasks

- **US_002** — API gateway middleware pipeline must be scaffolded
- **US_011** (EP-001) — Auth endpoints (`/api/auth/login`, `/api/auth/register`) must exist to apply rate limiting policies

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `RateLimitingPolicies` static class | `Server/Infrastructure/Security/` |
| CREATE | `GlobalExceptionFilter` (IExceptionFilter) | `Server/Infrastructure/Filters/` |
| CREATE | `ValidationErrorResponseModel` | `Server/Infrastructure/Models/` |
| MODIFY | `Program.cs` | Register rate limiter, FluentValidation pipeline, global exception filter |
| MODIFY | `AuthController` | Apply `[EnableRateLimiting("auth-login")]` attribute on login endpoint |

---

## Implementation Plan

1. **Named rate limiter policies** in `RateLimitingPolicies`:

   ```csharp
   // Login: 10 requests / 60 s per IP (AC-1)
   options.AddFixedWindowLimiter("auth-login", opt => {
       opt.PermitLimit = 10;
       opt.Window = TimeSpan.FromSeconds(60);
       opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
       opt.QueueLimit = 0;              // no queuing — reject immediately
   });

   // Register: 5 requests / 10 min per IP (from US_010 task_002)
   options.AddFixedWindowLimiter("auth-register", opt => {
       opt.PermitLimit = 5;
       opt.Window = TimeSpan.FromMinutes(10);
       opt.QueueLimit = 0;
   });

   // Resend verification: 3 requests / 5 min per email hash (sliding window)
   options.AddSlidingWindowLimiter("auth-resend", opt => {
       opt.PermitLimit = 3;
       opt.Window = TimeSpan.FromMinutes(5);
       opt.SegmentsPerWindow = 3;
       opt.QueueLimit = 0;
   });
   ```

2. **429 response format**: configure `OnRejected` callback on `RateLimiterOptions`:
   - Set `context.HttpContext.Response.StatusCode = 429`
   - Set `Retry-After` header: `context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)`
   - Log via `Serilog.Log.Warning("RateLimit exceeded: {Endpoint} from {IP}", ...)` (structured log, no PII beyond IP)
   - Write JSON body `{ "error": "Too many requests", "retryAfterSeconds": N }`

3. **FluentValidation pipeline** registration:
   - `builder.Services.AddFluentValidationAutoValidation(cfg => cfg.DisableDataAnnotationsValidation = true)`
   - `builder.Services.AddValidatorsFromAssemblyContaining<Program>()`
   - Disable ASP.NET Core's default `ModelStateInvalidFilter` to prevent double-error responses

4. **`GlobalExceptionFilter`** (`IExceptionFilter` / `IAsyncExceptionFilter`):
   - Catch `ValidationException` (FluentValidation) → HTTP 400, body: `{ errors: [{ field, message }] }`
   - Catch domain exceptions (`DuplicateEmailException`, `TokenExpiredException`, `TokenAlreadyUsedException`, `NotFoundException`) → mapped HTTP codes (409, 410, 409, 404)
   - Catch unhandled `Exception` → HTTP 500, body: `{ error: "An unexpected error occurred" }` — no stack trace, no exception message
   - Log all unhandled exceptions via Serilog with correlation ID from `HttpContext.TraceIdentifier`

5. **`ValidationErrorResponseModel`**:
   ```csharp
   public record ValidationErrorResponse(IEnumerable<FieldError> Errors);
   public record FieldError(string Field, string Message);
   ```
   Used by `GlobalExceptionFilter` for consistent 400 shape across all validators.

6. **Apply `[EnableRateLimiting]` attributes** to all public auth controller actions:
   - `AuthController.Login` → `[EnableRateLimiting("auth-login")]`
   - `AuthController.Register` → `[EnableRateLimiting("auth-register")]`
   - `AuthController.ResendVerification` → `[EnableRateLimiting("auth-resend")]`

7. **Redis-backed distributed rate limiting** (NFR-017 + NFR-010 horizontal scale):
   - `builder.Services.AddStackExchangeRedisCache(...)` pointing to Upstash Redis connection string from environment variable `REDIS_CONNECTION_STRING`
   - Configure `RateLimiterOptions` with `AddRedisRateLimiting()` via `RedisRateLimitStore` (or equivalent Redis-backed partition key per IP)
   - Partition key: `HttpContext.Connection.RemoteIpAddress` for IP-based limits; `SHA-256(email)` for email-keyed limits (never raw email as partition key)

8. **`Program.cs` registration order**: `UseRateLimiter()` placed after `UseRouting()` and before `UseAuthentication()` to enforce limits before JWT validation overhead.

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
| CREATE | `Server/Infrastructure/Security/RateLimitingPolicies.cs` | Named rate limiter policy definitions (login, register, resend) |
| CREATE | `Server/Infrastructure/Filters/GlobalExceptionFilter.cs` | Catches ValidationException + domain exceptions → structured HTTP responses, no stack traces |
| CREATE | `Server/Infrastructure/Models/ValidationErrorResponse.cs` | Canonical 400 error shape: `{ errors: [{ field, message }] }` |
| MODIFY | `Server/Program.cs` | Register `AddRateLimiter`, `AddFluentValidationAutoValidation`, `GlobalExceptionFilter`; set middleware order |
| MODIFY | `Server/Modules/Auth/AuthController.cs` | Apply `[EnableRateLimiting("auth-login")]`, `[EnableRateLimiting("auth-register")]`, `[EnableRateLimiting("auth-resend")]` |

---

## External References

- [ASP.NET Core Rate Limiting (.NET 7+)](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-9.0)
- [ASP.NET Core Rate Limiting — OnRejected callback](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-9.0#limiter-with-onrejected-retryafter-and-globallimiter)
- [FluentValidation 11 — ASP.NET Core Auto Validation](https://docs.fluentvalidation.net/en/latest/aspnet.html#automatic-validation)
- [FluentValidation — Disable ModelState](https://docs.fluentvalidation.net/en/latest/aspnet.html#compatibility-with-mvc-attribute-based-validation)
- [Serilog — Structured Logging](https://serilog.net/)
- [OWASP A03 — Injection Prevention](https://owasp.org/Top10/A03_2021-Injection/)
- [OWASP A05 — Security Misconfiguration (rate limiter defaults)](https://owasp.org/Top10/A05_2021-Security_Misconfiguration/)
- [Redis Rate Limiting for ASP.NET Core](https://github.com/cristipufu/aspnetcore-redis-rate-limiting)

---

## Build Commands

```bash
# Restore packages
dotnet restore

# Build
dotnet build

# Run unit tests
dotnet test

# Verify rate limiting middleware order
dotnet run --project Server/Server.csproj
```

---

## Implementation Validation Strategy

- [ ] Unit tests pass: `GlobalExceptionFilter` maps `ValidationException` → 400 with `{ errors }` body, no stack trace in response
- [ ] Unit tests pass: `GlobalExceptionFilter` maps unhandled exception → 500 with generic message, no exception detail
- [ ] Integration test: 10 POST `/api/auth/login` requests from same IP succeed; 11th returns 429 with `Retry-After` header
- [ ] Integration test: legitimate traffic under the rate limit threshold is NOT blocked (AC edge case)
- [ ] Integration test: invalid `RegisterPatientCommand` payload returns 400 with per-field `{ errors }` body
- [ ] Serilog log entry written on every 429 response with endpoint and (hashed) IP
- [ ] No stack trace present in any 400, 404, 409, 410, or 500 response body
- [ ] `UseRateLimiter()` appears after `UseRouting()` and before `UseAuthentication()` in middleware order
- [ ] Integration tests pass (if applicable)

---

## Implementation Checklist

- [ ] Create `RateLimitingPolicies` with fixed-window policies: `auth-login` (10/60s), `auth-register` (5/10min), sliding-window `auth-resend` (3/5min)
- [ ] Configure `OnRejected` callback: set status 429, write `Retry-After` header, log structured event via Serilog
- [ ] Register Redis-backed distributed rate limiting keyed by `RemoteIpAddress` (IP-based) and `SHA-256(email)` (email-based)
- [ ] Register `AddFluentValidationAutoValidation` with `DisableDataAnnotationsValidation = true`; disable default `ModelStateInvalidFilter`
- [ ] Create `GlobalExceptionFilter`: map `ValidationException` → 400, domain exceptions → correct codes, unhandled → 500 (no stack trace exposed)
- [ ] Create `ValidationErrorResponse` record with `IEnumerable<FieldError>` for consistent 400 shape
- [ ] Apply `[EnableRateLimiting]` attributes to `AuthController.Login`, `.Register`, `.ResendVerification`
- [ ] Register rate limiter, FluentValidation auto-validation, and `GlobalExceptionFilter` in `Program.cs` with correct middleware ordering
