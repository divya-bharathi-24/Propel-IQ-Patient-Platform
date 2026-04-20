# Task - task_001_be_health_check_endpoints

## Requirement Reference

- **User Story:** us_052 — Platform Availability & Graceful Degradation Handlers
- **Story Location:** `.propel/context/tasks/EP-011/us_052/us_052.md`
- **Acceptance Criteria:**
  - AC-1: `GET /health` returns HTTP 200 with a structured JSON report listing status of each dependency: PostgreSQL, Redis, SendGrid, Twilio, OpenAI, Google Calendar, Microsoft Graph (NFR-003).
- **Edge Cases:**
  - PostgreSQL unreachable: `GET /health` returns HTTP 503; all DB-dependent API endpoints return 503 via `ExceptionHandlingMiddleware` when EF Core throws; critical alert fires immediately (Serilog Critical + Redis flag `platform:health:db_down`).

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

| Layer    | Technology                            | Version |
| -------- | ------------------------------------- | ------- |
| Backend  | ASP.NET Core Web API / .NET           | 9       |
| Health   | Microsoft.Extensions.Diagnostics.HealthChecks | 9.x |
| Cache    | Upstash Redis (StackExchange.Redis)   | Serverless |
| Logging  | Serilog                               | 4.x     |

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

Register ASP.NET Core health checks for all seven external dependencies using `Microsoft.Extensions.Diagnostics.HealthChecks`. Each check has a 5-second individual timeout to prevent a single slow dependency from blocking the overall health response.

The `/health` endpoint returns a structured JSON response (not the default plain-text format) using a custom `HealthCheckResponseWriter`. The overall HTTP status is `200 OK` for Healthy/Degraded and `503 Service Unavailable` for Unhealthy — allowing load balancers and uptime monitors to react to PostgreSQL outages.

A `DatabaseUnavailableHealthCheck` also writes a Redis flag `platform:health:db_down` (60-second TTL) when PostgreSQL is unreachable, so `ExceptionHandlingMiddleware` (from US_051) can catch unrecoverable `DbException` errors and return a consistent 503 without exposing internals.

**Security note:** The `/health` endpoint must NOT expose connection strings, hostnames, or credential details in its response. Each check returns only `{ "status": "Healthy|Degraded|Unhealthy", "description": "..." }` per dependency.

---

## Dependent Tasks

- `EP-011/us_051/task_001_be_correlation_id_and_request_logging.md` — `ExceptionHandlingMiddleware` required for DB-down 503 response path.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `PostgreSqlHealthCheck` (new) | Infrastructure | CREATE — pings `ApplicationDbContext` via `_context.Database.CanConnectAsync()`; sets Redis `platform:health:db_down` flag on failure |
| `RedisHealthCheck` (new) | Infrastructure | CREATE — `IDatabase.PingAsync()` with 5-second timeout |
| `SendGridHealthCheck` (new) | Infrastructure | CREATE — HTTP HEAD to `https://api.sendgrid.com/v3/scopes` with API key; no email sent |
| `TwilioHealthCheck` (new) | Infrastructure | CREATE — HTTP HEAD to `https://api.twilio.com/2010-04-01/Accounts/{AccountSid}` with Basic auth; no SMS sent |
| `OpenAiHealthCheck` (new) | Infrastructure | CREATE — HTTP GET to `https://api.openai.com/v1/models` (lightweight list endpoint) with Bearer token; checks HTTP 200 |
| `GoogleCalendarHealthCheck` (new) | Infrastructure | CREATE — HTTP HEAD to `https://www.googleapis.com/calendar/v3/users/me/calendarList`; OAuth token from config; returns `Degraded` (not `Unhealthy`) if unavailable — calendar is non-critical |
| `MicrosoftGraphHealthCheck` (new) | Infrastructure | CREATE — HTTP GET `https://graph.microsoft.com/v1.0/$metadata`; returns `Degraded` if unavailable |
| `HealthCheckResponseWriter` (new) | API | CREATE — custom `Func<HttpContext, HealthReport, Task>` that serialises `HealthReport` to structured JSON |
| `Program.cs` (existing) | API | MODIFY — register all 7 health checks; map `/health` with custom writer; map `/health/live` (liveness probe — DB only) |

---

## Implementation Plan

1. **Health check registration pattern** (applied to all 7):

   ```csharp
   builder.Services.AddHealthChecks()
       .AddCheck<PostgreSqlHealthCheck>("postgresql", tags: ["db", "critical"],
           timeout: TimeSpan.FromSeconds(5))
       .AddCheck<RedisHealthCheck>("redis", tags: ["cache"],
           timeout: TimeSpan.FromSeconds(5))
       .AddCheck<SendGridHealthCheck>("sendgrid", tags: ["email", "degradable"],
           failureStatus: HealthStatus.Degraded,    // Email unavailable → Degraded, not Unhealthy
           timeout: TimeSpan.FromSeconds(5))
       .AddCheck<TwilioHealthCheck>("twilio", tags: ["sms", "degradable"],
           failureStatus: HealthStatus.Degraded,
           timeout: TimeSpan.FromSeconds(5))
       .AddCheck<OpenAiHealthCheck>("openai", tags: ["ai", "degradable"],
           failureStatus: HealthStatus.Degraded,
           timeout: TimeSpan.FromSeconds(5))
       .AddCheck<GoogleCalendarHealthCheck>("google-calendar", tags: ["calendar", "degradable"],
           failureStatus: HealthStatus.Degraded,
           timeout: TimeSpan.FromSeconds(5))
       .AddCheck<MicrosoftGraphHealthCheck>("microsoft-graph", tags: ["calendar", "degradable"],
           failureStatus: HealthStatus.Degraded,
           timeout: TimeSpan.FromSeconds(5));
   ```

   Only `PostgreSqlHealthCheck` uses `failureStatus: HealthStatus.Unhealthy` (default) — a DB failure makes the platform unhealthy and returns HTTP 503.

2. **`PostgreSqlHealthCheck`** — with Redis flag on failure:

   ```csharp
   public sealed class PostgreSqlHealthCheck : IHealthCheck
   {
       public async Task<HealthCheckResult> CheckHealthAsync(
           HealthCheckContext context, CancellationToken ct = default)
       {
           try
           {
               bool canConnect = await _dbContext.Database.CanConnectAsync(ct);
               if (!canConnect)
               {
                   await SetDbDownFlagAsync();
                   return HealthCheckResult.Unhealthy("PostgreSQL unreachable");
               }
               return HealthCheckResult.Healthy("PostgreSQL reachable");
           }
           catch (Exception ex)
           {
               await SetDbDownFlagAsync();
               Log.Critical(ex, "PostgreSQL health check failed — setting db_down flag");
               return HealthCheckResult.Unhealthy("PostgreSQL check threw exception", ex);
           }
       }

       private async Task SetDbDownFlagAsync()
       {
           try
           {
               await _redis.StringSetAsync("platform:health:db_down", "1",
                   TimeSpan.FromSeconds(60));
           }
           catch { /* Redis may also be down — best effort */ }
       }
   }
   ```

3. **HTTP-based health checks** (SendGrid, Twilio, OpenAI, Google Calendar, Microsoft Graph) share a common pattern using `IHttpClientFactory`:

   ```csharp
   // Example: SendGridHealthCheck
   public sealed class SendGridHealthCheck : IHealthCheck
   {
       public async Task<HealthCheckResult> CheckHealthAsync(
           HealthCheckContext context, CancellationToken ct = default)
       {
           try
           {
               var client = _httpClientFactory.CreateClient("SendGridHealth");
               // HEAD request — no email sent; just tests reachability + auth
               var request = new HttpRequestMessage(HttpMethod.Head,
                   "https://api.sendgrid.com/v3/scopes");
               request.Headers.Authorization =
                   new AuthenticationHeaderValue("Bearer", _options.Value.ApiKey);

               var response = await client.SendAsync(request, ct);
               return response.IsSuccessStatusCode
                   ? HealthCheckResult.Healthy("SendGrid reachable")
                   : HealthCheckResult.Degraded($"SendGrid returned {(int)response.StatusCode}");
           }
           catch (Exception ex)
           {
               return HealthCheckResult.Degraded("SendGrid unreachable", ex);
           }
       }
   }
   ```

   **Security:** API keys read from `IOptions<T>` bound to environment variables / Azure Key Vault — never hardcoded. The health response JSON contains only `status` and `description`; no API key, host, or credential data.

4. **`HealthCheckResponseWriter`** — structured JSON, no internals exposed:

   ```csharp
   public static class HealthCheckResponseWriter
   {
       public static Task WriteResponse(HttpContext context, HealthReport report)
       {
           context.Response.ContentType = "application/json";
           var result = new
           {
               status = report.Status.ToString(),
               totalDurationMs = (int)report.TotalDuration.TotalMilliseconds,
               checks = report.Entries.Select(e => new
               {
                   name        = e.Key,
                   status      = e.Value.Status.ToString(),
                   description = e.Value.Description ?? string.Empty,
                   durationMs  = (int)e.Value.Duration.TotalMilliseconds,
                   // No ExceptionMessage, no Data dictionary — avoids leaking connection strings
               })
           };
           return context.Response.WriteAsJsonAsync(result,
               cancellationToken: context.RequestAborted);
       }
   }
   ```

5. **Endpoint mapping** in `Program.cs`:

   ```csharp
   // Full health report — all 7 dependencies
   app.MapHealthChecks("/health", new HealthCheckOptions
   {
       ResponseWriter = HealthCheckResponseWriter.WriteResponse,
       ResultStatusCodes =
       {
           [HealthStatus.Healthy]   = StatusCodes.Status200OK,
           [HealthStatus.Degraded]  = StatusCodes.Status200OK,   // Degraded = partial, not fatal
           [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
       }
   });

   // Liveness probe — DB only; used by container orchestrators
   app.MapHealthChecks("/health/live", new HealthCheckOptions
   {
       Predicate = check => check.Tags.Contains("critical"),
       ResponseWriter = HealthCheckResponseWriter.WriteResponse,
       ResultStatusCodes =
       {
           [HealthStatus.Healthy]   = StatusCodes.Status200OK,
           [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
       }
   });
   ```

---

## Current Project State

```
Server/
  API/
    Program.cs                          ← EXISTS — MODIFY
    Middleware/
      CorrelationIdMiddleware.cs        ← EXISTS (US_051)
      ExceptionHandlingMiddleware.cs    ← EXISTS (US_051)
  Infrastructure/
    HealthChecks/                       ← create new folder
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Infrastructure/HealthChecks/PostgreSqlHealthCheck.cs` | `IHealthCheck`: `CanConnectAsync`; sets Redis `platform:health:db_down` on failure; `Log.Critical` |
| CREATE | `Server/Infrastructure/HealthChecks/RedisHealthCheck.cs` | `IHealthCheck`: `IDatabase.PingAsync()` with 5-second timeout |
| CREATE | `Server/Infrastructure/HealthChecks/SendGridHealthCheck.cs` | `IHealthCheck`: HTTP HEAD to SendGrid scopes endpoint; API key from `IOptions` |
| CREATE | `Server/Infrastructure/HealthChecks/TwilioHealthCheck.cs` | `IHealthCheck`: HTTP HEAD to Twilio Accounts endpoint; Basic auth from `IOptions` |
| CREATE | `Server/Infrastructure/HealthChecks/OpenAiHealthCheck.cs` | `IHealthCheck`: HTTP GET to OpenAI models list; Bearer token from `IOptions` |
| CREATE | `Server/Infrastructure/HealthChecks/GoogleCalendarHealthCheck.cs` | `IHealthCheck`: HTTP HEAD to Google Calendar API; `Degraded` (not `Unhealthy`) on failure |
| CREATE | `Server/Infrastructure/HealthChecks/MicrosoftGraphHealthCheck.cs` | `IHealthCheck`: HTTP GET to Graph `$metadata`; `Degraded` on failure |
| CREATE | `Server/API/HealthChecks/HealthCheckResponseWriter.cs` | Custom writer: structured JSON with no credentials/internals in output |
| MODIFY | `Server/API/Program.cs` | Register all 7 checks; map `/health` + `/health/live`; configure `ResultStatusCodes` |

---

## External References

- [ASP.NET Core 9 — Health Checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-9.0) — `IHealthCheck`, `AddHealthChecks()`, `MapHealthChecks()`, `HealthCheckOptions`
- [HealthStatus enum](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthstatus) — `Healthy`, `Degraded`, `Unhealthy`; `failureStatus` parameter controls which level a check failure reports
- [NFR-003 (design.md)](../../../docs/design.md) — 99.9% monthly uptime; health checks enable uptime monitoring tools
- [NFR-018 (design.md)](../../../docs/design.md) — Graceful degradation; external service unavailability must not block core workflows
- [AG-6 (design.md)](../../../docs/design.md) — Core booking and clinical workflows remain available during external service failures

---

## Build Commands

- Refer to [`.propel/build/`](../../../../build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [ ] `GET /health` returns HTTP 200 and structured JSON with 7 named checks when all dependencies are reachable
- [ ] `GET /health` returns HTTP 503 when PostgreSQL is unreachable; Redis key `platform:health:db_down` is set
- [ ] `GET /health` returns HTTP 200 (not 503) when SendGrid is unreachable — Degraded maps to 200
- [ ] Health response JSON does NOT contain any API key, connection string, or exception message text — only `name`, `status`, `description`, `durationMs`
- [ ] `GET /health/live` only includes the `postgresql` check (tagged `critical`); returns 200/503 for healthy/unhealthy DB

---

## Implementation Checklist

- [ ] Create `PostgreSqlHealthCheck`: `CanConnectAsync()`; set Redis `platform:health:db_down` (60-second TTL) + `Log.Critical` on failure
- [ ] Create `RedisHealthCheck`: `IDatabase.PingAsync()` with 5-second timeout; `Degraded` on timeout/exception
- [ ] Create HTTP-based checks for SendGrid, Twilio, OpenAI, Google Calendar, Microsoft Graph: `IHttpClientFactory`; credentials from `IOptions`; all return `Degraded` on failure; response JSON exposes no credential data
- [ ] Create `HealthCheckResponseWriter`: structured JSON output; no `ExceptionMessage`, no `Data` dictionary in output
- [ ] Modify `Program.cs`: register all 7 checks with correct `failureStatus` (only PostgreSQL = `Unhealthy`); map `/health` + `/health/live`; configure `ResultStatusCodes` (Degraded → 200, Unhealthy → 503)
