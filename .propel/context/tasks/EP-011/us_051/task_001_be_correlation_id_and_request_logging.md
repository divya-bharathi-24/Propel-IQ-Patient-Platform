# Task - task_001_be_correlation_id_and_request_logging

## Requirement Reference

- **User Story:** us_051 — API Performance Instrumentation & Structured Logging
- **Story Location:** `.propel/context/tasks/EP-011/us_051/us_051.md`
- **Acceptance Criteria:**
  - AC-1: Every API endpoint request produces a structured Serilog log entry containing: correlation ID, route, HTTP method, status code, duration (ms), user ID, and role.
  - AC-2: Correlation ID is propagated consistently across all log entries for a request — including database calls and external service calls — via Serilog `LogContext` enrichment (TR-018).
- **Edge Cases:**
  - Missing correlation ID: middleware generates a new `Guid`-based correlation ID; attaches it to request via `IHttpContextAccessor`; returns it in `X-Correlation-Id` response header.

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

| Layer    | Technology                    | Version |
| -------- | ----------------------------- | ------- |
| Backend  | ASP.NET Core Web API / .NET   | 9       |
| Logging  | Serilog                       | 4.x     |
| Logging  | Serilog.AspNetCore            | 8.x     |

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

Implement the cross-cutting correlation ID and request logging infrastructure for the ASP.NET Core pipeline. Two ASP.NET Core middleware components work in tandem:

1. **`CorrelationIdMiddleware`** — reads the `X-Correlation-Id` request header; generates a new `Guid` if absent; stores the value in `HttpContext.Items["CorrelationId"]`; pushes it into `LogContext` via `LogContext.PushProperty`; adds it to the response header `X-Correlation-Id`.

2. **`RequestLoggingMiddleware`** — starts a `Stopwatch`; invokes `next()`; on completion logs a single Serilog structured message with all required fields (correlation ID, route, method, status code, duration ms, user ID, role). Uses message template enrichment (not string interpolation) for Serilog structured output.

A thin accessor abstraction — `ICorrelationIdAccessor` / `HttpContextCorrelationIdAccessor` — allows downstream MediatR handlers and repository classes to read the correlation ID without a direct `IHttpContextAccessor` dependency (used by MediatR behaviors in task_002).

Both middleware components are registered in `Program.cs` as the first two items in the middleware pipeline (before auth middleware) to ensure the correlation ID is available for all subsequent log entries.

---

## Dependent Tasks

- No external dependencies — this is a foundational cross-cutting task.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `CorrelationIdMiddleware` (new) | API Infrastructure | CREATE — reads/generates correlation ID; pushes to `LogContext`; adds response header |
| `RequestLoggingMiddleware` (new) | API Infrastructure | CREATE — `Stopwatch`-timed request/response logging with all AC-1 fields |
| `ICorrelationIdAccessor` (new) | Application | CREATE — `string GetCorrelationId()` interface; used by MediatR behaviors |
| `HttpContextCorrelationIdAccessor` (new) | API Infrastructure | CREATE — reads from `HttpContext.Items["CorrelationId"]` via `IHttpContextAccessor` |
| `SerilogEnricherExtensions` (new) | Infrastructure | CREATE — `AddCorrelationIdEnricher()` extension; `UseMiddleware` helpers registered in `Program.cs` |
| `Program.cs` (existing) | API | MODIFY — register `CorrelationIdMiddleware` + `RequestLoggingMiddleware` first in pipeline; register `ICorrelationIdAccessor` DI |

---

## Implementation Plan

1. **`CorrelationIdMiddleware`**:

   ```csharp
   public sealed class CorrelationIdMiddleware : IMiddleware
   {
       private const string HeaderName = "X-Correlation-Id";
       private const string ItemsKey   = "CorrelationId";

       public async Task InvokeAsync(HttpContext context, RequestDelegate next)
       {
           string correlationId = context.Request.Headers.TryGetValue(HeaderName, out var headerValue)
               && !string.IsNullOrWhiteSpace(headerValue)
               ? headerValue.ToString()
               : Guid.NewGuid().ToString("N"); // compact 32-char hex, no hyphens

           context.Items[ItemsKey] = correlationId;
           context.Response.Headers[HeaderName] = correlationId;

           using (LogContext.PushProperty("CorrelationId", correlationId))
           {
               await next(context);
           }
       }
   }
   ```

   Using `IMiddleware` (not `RequestDelegate` convention) so it can be resolved from DI with full constructor injection.

2. **`RequestLoggingMiddleware`**:

   ```csharp
   public sealed class RequestLoggingMiddleware : IMiddleware
   {
       public async Task InvokeAsync(HttpContext context, RequestDelegate next)
       {
           var sw = Stopwatch.StartNew();
           try
           {
               await next(context);
           }
           finally
           {
               sw.Stop();
               string userId   = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
               string role     = context.User.FindFirstValue(ClaimTypes.Role) ?? "none";
               string route    = context.GetEndpoint()?.DisplayName ?? context.Request.Path.Value ?? "unknown";
               string corrId   = context.Items["CorrelationId"] as string ?? "unknown";

               Log.Information(
                   "HTTP {Method} {Route} responded {StatusCode} in {DurationMs}ms " +
                   "[CorrelationId={CorrelationId} UserId={UserId} Role={Role}]",
                   context.Request.Method,
                   route,
                   context.Response.StatusCode,
                   sw.ElapsedMilliseconds,
                   corrId,
                   userId,
                   role);
           }
       }
   }
   ```

   All fields are message template parameters — Serilog destructures them as structured properties, not string-concatenated values. This is required for TR-018 structured log compliance.

3. **`ICorrelationIdAccessor` + `HttpContextCorrelationIdAccessor`**:

   ```csharp
   public interface ICorrelationIdAccessor
   {
       string GetCorrelationId();
   }

   public sealed class HttpContextCorrelationIdAccessor : ICorrelationIdAccessor
   {
       private readonly IHttpContextAccessor _httpContextAccessor;

       public HttpContextCorrelationIdAccessor(IHttpContextAccessor httpContextAccessor)
           => _httpContextAccessor = httpContextAccessor;

       public string GetCorrelationId()
           => _httpContextAccessor.HttpContext?.Items["CorrelationId"] as string
              ?? "no-context";
   }
   ```

4. **`Program.cs` modifications** — middleware pipeline order:

   ```csharp
   // Register middleware as IMiddleware (requires DI registration)
   builder.Services.AddTransient<CorrelationIdMiddleware>();
   builder.Services.AddTransient<RequestLoggingMiddleware>();
   builder.Services.AddHttpContextAccessor();
   builder.Services.AddScoped<ICorrelationIdAccessor, HttpContextCorrelationIdAccessor>();

   // Pipeline order (MUST be before UseAuthentication)
   app.UseMiddleware<CorrelationIdMiddleware>();
   app.UseMiddleware<RequestLoggingMiddleware>();
   app.UseAuthentication();
   app.UseAuthorization();
   ```

5. **Serilog configuration** (`Program.cs` Serilog setup) — ensure `LogContext` enrichment is enabled:

   ```csharp
   Log.Logger = new LoggerConfiguration()
       .Enrich.FromLogContext()            // Required for LogContext.PushProperty to appear in sinks
       .Enrich.WithMachineName()
       .Enrich.WithEnvironmentName()
       .WriteTo.Console(new JsonFormatter()) // Structured JSON for production log aggregation
       .WriteTo.Seq("http://localhost:5341") // Or Application Insights in production
       .CreateLogger();
   ```

   The `.Enrich.FromLogContext()` call is the critical enabler — without it, `LogContext.PushProperty("CorrelationId", ...)` values are silently dropped.

---

## Current Project State

```
Server/
  API/
    Program.cs                      ← EXISTS — MODIFY
    Middleware/                     ← may exist or require creation
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/API/Middleware/CorrelationIdMiddleware.cs` | `IMiddleware`: reads/generates correlation ID from `X-Correlation-Id` header; pushes to `LogContext`; writes response header |
| CREATE | `Server/API/Middleware/RequestLoggingMiddleware.cs` | `IMiddleware`: `Stopwatch`-timed; logs structured Serilog entry with all 7 AC-1 fields |
| CREATE | `Server/Application/Common/ICorrelationIdAccessor.cs` | Thin interface for correlation ID access; used by MediatR behaviors |
| CREATE | `Server/API/Infrastructure/HttpContextCorrelationIdAccessor.cs` | `IHttpContextAccessor`-backed implementation |
| MODIFY | `Server/API/Program.cs` | Register both middleware as `IMiddleware`; register `ICorrelationIdAccessor`; configure Serilog with `.Enrich.FromLogContext()` |

---

## External References

- [Serilog 4.x — Log Context](https://github.com/serilog/serilog/wiki/Enrichment#log-context) — `LogContext.PushProperty`; requires `.Enrich.FromLogContext()` in logger config
- [Serilog.AspNetCore 8.x](https://github.com/serilog/serilog-aspnetcore) — `UseSerilogRequestLogging()` alternative (note: we implement custom middleware for AC-1's specific required fields; `UseSerilogRequestLogging()` does not include user ID + role out of box)
- [ASP.NET Core — `IMiddleware` vs Convention Middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/write?view=aspnetcore-9.0#iMiddleware) — `IMiddleware` is preferred for DI-resolved middleware
- [TR-018 (design.md)](../../../docs/design.md) — Structured logging with Serilog; correlation IDs propagated across services
- [AD-4 (design.md)](../../../docs/design.md) — API Gateway injects/generates correlation ID before routing

---

## Build Commands

- Refer to [`.propel/build/`](../../../../build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [ ] Send a request without `X-Correlation-Id` header — verify response contains `X-Correlation-Id` header with a generated Guid value
- [ ] Send a request with `X-Correlation-Id: test-abc-123` — verify response echoes the same value in `X-Correlation-Id` response header
- [ ] Check Serilog log output — verify structured JSON log entry contains all 7 fields: `CorrelationId`, `Method`, `Route`, `StatusCode`, `DurationMs`, `UserId`, `Role`
- [ ] Verify `CorrelationId` property appears in ALL log entries for a request (including EF Core `CommandExecuted` logs if EF Core logging is enabled) — confirms `LogContext.PushProperty` scope is active
- [ ] Confirm middleware is registered BEFORE `UseAuthentication()` in pipeline

---

## Implementation Checklist

- [ ] Create `CorrelationIdMiddleware` (`IMiddleware`): read `X-Correlation-Id` header; generate new Guid if absent; store in `HttpContext.Items["CorrelationId"]`; push to `LogContext.PushProperty`; write `X-Correlation-Id` response header
- [ ] Create `RequestLoggingMiddleware` (`IMiddleware`): `Stopwatch` wrap; finally block logs all 7 AC-1 structured fields using message template (not string interpolation)
- [ ] Create `ICorrelationIdAccessor` interface + `HttpContextCorrelationIdAccessor` implementation (reads from `HttpContext.Items`)
- [ ] Modify `Program.cs`: register both middleware as `IMiddleware` (transient); register `IHttpContextAccessor`; register `ICorrelationIdAccessor` as scoped; configure Serilog with `.Enrich.FromLogContext()`
- [ ] Verify `UseMiddleware<CorrelationIdMiddleware>()` and `UseMiddleware<RequestLoggingMiddleware>()` are called before `UseAuthentication()` in the middleware pipeline
