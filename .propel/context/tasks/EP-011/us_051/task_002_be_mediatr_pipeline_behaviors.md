# Task - task_002_be_mediatr_pipeline_behaviors

## Requirement Reference

- **User Story:** us_051 — API Performance Instrumentation & Structured Logging
- **Story Location:** `.propel/context/tasks/EP-011/us_051/us_051.md`
- **Acceptance Criteria:**
  - AC-3: All write operations use MediatR Commands; all read operations use MediatR Queries; no controller calls repositories directly (TR-019, AD-2).
  - AC-4: Invalid input produces a structured HTTP 400 response with per-field validation errors; validation failure is logged with correlation ID (TR-020, NFR-014).
  - AC-1/AC-2 (MediatR scope): Correlation ID appears in all Serilog log entries written inside MediatR handlers, including database calls.
- **Edge Cases:**
  - p95 latency breach: if computed p95 latency over the last 5-minute window exceeds 2,000ms, a Serilog Warning is emitted; Redis flag `api:perf:p95_breach` is set with a 10-minute TTL to prevent alert flooding.

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

| Layer      | Technology                          | Version    |
| ---------- | ----------------------------------- | ---------- |
| Backend    | ASP.NET Core Web API / .NET         | 9          |
| CQRS       | MediatR                             | 12.x       |
| Validation | FluentValidation                    | 11.x       |
| Logging    | Serilog                             | 4.x        |
| Cache      | Upstash Redis (StackExchange.Redis) | Serverless |

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

Implement three MediatR `IPipelineBehavior<TRequest, TResponse>` implementations registered globally for all commands and queries. These behaviors form the inner cross-cutting layer — running inside the MediatR pipeline, after HTTP middleware but inside individual handler dispatch:

1. **`LoggingBehavior<TRequest, TResponse>`** — logs request entry (command/query name + correlation ID) and exit (duration ms); pushes correlation ID into `LogContext` again inside the handler scope to ensure database and external service calls within handlers also carry the correlation ID even in non-HTTP contexts (e.g., background jobs).

2. **`ValidationBehavior<TRequest, TResponse>`** — runs all registered `IValidator<TRequest>` implementations; aggregates validation failures; throws `ValidationException` on failure. A global `ExceptionHandlingMiddleware` (also created here) catches `ValidationException` and returns a structured `ProblemDetails` HTTP 400 with per-field errors, logged with correlation ID.

3. **`PerformanceBehavior<TRequest, TResponse>`** — measures handler execution time via `Stopwatch`; pushes a latency sample to a Redis list keyed `api:perf:latency:{routeKey}` (LPUSH + LTRIM 500 samples); after every 10th write, computes p95 from the list and logs `Warning` + sets Redis flag `api:perf:p95_breach` (10-min TTL) if p95 > 2,000ms.

All three are registered in `Program.cs` via `services.AddTransient(typeof(IPipelineBehavior<,>), typeof(X<,>))` in the order: `LoggingBehavior` → `ValidationBehavior` → `PerformanceBehavior`.

**CQRS Enforcement Convention:** A `ICommand` marker interface and `IQuery<TResult>` marker interface are introduced. All MediatR requests must implement one of these markers. Controllers inject `ISender` (not `IMediator`) — enforcing no direct repository use from controllers.

---

## Dependent Tasks

- `EP-011/us_051/task_001_be_correlation_id_and_request_logging.md` — `ICorrelationIdAccessor` must be registered; `LogContext` enrichment must be configured.

---

## Impacted Components

| Component                                              | Module      | Action                                                                                                                                                   |
| ------------------------------------------------------ | ----------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ICommand` / `IQuery<TResult>` marker interfaces (new) | Application | CREATE — empty marker interfaces for CQRS type safety                                                                                                    |
| `LoggingBehavior<TRequest, TResponse>` (new)           | Application | CREATE — `IPipelineBehavior`; logs entry/exit; pushes correlation ID to `LogContext`                                                                     |
| `ValidationBehavior<TRequest, TResponse>` (new)        | Application | CREATE — `IPipelineBehavior`; runs all `IValidator<TRequest>`; throws `ValidationException` on failure                                                   |
| `PerformanceBehavior<TRequest, TResponse>` (new)       | Application | CREATE — `IPipelineBehavior`; Redis LPUSH latency sample; p95 computation every 10th write; Serilog Warning + Redis flag on breach                       |
| `ExceptionHandlingMiddleware` (new)                    | API         | CREATE — catches `ValidationException` → structured `ProblemDetails` HTTP 400 with per-field errors + correlation ID in log                              |
| `Program.cs` (existing)                                | API         | MODIFY — register three behaviors as open-generic `IPipelineBehavior<,>`; register `ExceptionHandlingMiddleware`; register `FluentValidation` validators |

---

## Implementation Plan

1. **CQRS marker interfaces**:

   ```csharp
   // Marker for write operations
   public interface ICommand : IRequest { }
   public interface ICommand<TResponse> : IRequest<TResponse> { }

   // Marker for read operations
   public interface IQuery<TResponse> : IRequest<TResponse> { }
   ```

   All existing and future MediatR requests must implement `ICommand` or `IQuery<T>`. This does not affect dispatch — MediatR resolves handlers by `IRequest<T>` regardless of marker.

2. **`LoggingBehavior<TRequest, TResponse>`**:

   ```csharp
   public sealed class LoggingBehavior<TRequest, TResponse>
       : IPipelineBehavior<TRequest, TResponse>
       where TRequest : IRequest<TResponse>
   {
       private readonly ICorrelationIdAccessor _correlationId;

       public async Task<TResponse> Handle(
           TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
       {
           string correlationId = _correlationId.GetCorrelationId();
           string requestName   = typeof(TRequest).Name;

           using (LogContext.PushProperty("CorrelationId", correlationId))
           using (LogContext.PushProperty("RequestName", requestName))
           {
               Log.Information("Handling {RequestName} [CorrelationId={CorrelationId}]",
                   requestName, correlationId);

               var sw = Stopwatch.StartNew();
               var response = await next();
               sw.Stop();

               Log.Information("Handled {RequestName} in {DurationMs}ms [CorrelationId={CorrelationId}]",
                   requestName, sw.ElapsedMilliseconds, correlationId);

               return response;
           }
       }
   }
   ```

3. **`ValidationBehavior<TRequest, TResponse>`**:

   ```csharp
   public sealed class ValidationBehavior<TRequest, TResponse>
       : IPipelineBehavior<TRequest, TResponse>
       where TRequest : IRequest<TResponse>
   {
       private readonly IEnumerable<IValidator<TRequest>> _validators;

       public async Task<TResponse> Handle(
           TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
       {
           if (!_validators.Any()) return await next();

           var context = new ValidationContext<TRequest>(request);
           var results = await Task.WhenAll(_validators.Select(v => v.ValidateAsync(context, ct)));
           var failures = results
               .SelectMany(r => r.Errors)
               .Where(f => f is not null)
               .ToList();

           if (failures.Count > 0)
               throw new ValidationException(failures);

           return await next();
       }
   }
   ```

4. **`ExceptionHandlingMiddleware`** — catches `FluentValidation.ValidationException` → HTTP 400 `ProblemDetails`:

   ```csharp
   public sealed class ExceptionHandlingMiddleware : IMiddleware
   {
       public async Task InvokeAsync(HttpContext context, RequestDelegate next)
       {
           try
           {
               await next(context);
           }
           catch (ValidationException ex)
           {
               string correlationId = context.Items["CorrelationId"] as string ?? "unknown";

               Log.Warning("Validation failed for request to {Path} [CorrelationId={CorrelationId}]: {Errors}",
                   context.Request.Path,
                   correlationId,
                   ex.Errors.Select(e => new { e.PropertyName, e.ErrorMessage }));

               var problem = new ValidationProblemDetails(
                   ex.Errors
                     .GroupBy(f => f.PropertyName)
                     .ToDictionary(
                         g => g.Key,
                         g => g.Select(f => f.ErrorMessage).ToArray()))
               {
                   Status  = StatusCodes.Status400BadRequest,
                   Title   = "One or more validation errors occurred.",
                   Type    = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                   Extensions = { ["correlationId"] = correlationId }
               };

               context.Response.StatusCode = StatusCodes.Status400BadRequest;
               context.Response.ContentType = "application/problem+json";
               await context.Response.WriteAsJsonAsync(problem, ct: context.RequestAborted);
           }
       }
   }
   ```

5. **`PerformanceBehavior<TRequest, TResponse>`** — Redis latency sampling + p95 alert:

   ```csharp
   public sealed class PerformanceBehavior<TRequest, TResponse>
       : IPipelineBehavior<TRequest, TResponse>
       where TRequest : IRequest<TResponse>
   {
       private const int SampleWindow   = 500;   // Keep last 500 samples per route key
       private const int EvalInterval   = 10;    // Compute p95 every Nth sample
       private const long SlaMs         = 2_000; // 2-second SLA (NFR-001)
       private const string AlertKey    = "api:perf:p95_breach";
       private static readonly TimeSpan AlertTtl = TimeSpan.FromMinutes(10);

       public async Task<TResponse> Handle(
           TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
       {
           var sw = Stopwatch.StartNew();
           var response = await next();
           sw.Stop();

           // Fire-and-forget latency recording — does not affect response path
           _ = RecordLatencyAsync(typeof(TRequest).Name, sw.ElapsedMilliseconds);
           return response;
       }

       private async Task RecordLatencyAsync(string requestName, long latencyMs)
       {
           try
           {
               string listKey  = $"api:perf:latency:{requestName}";
               var db = _redis.GetDatabase();
               long sampleCount = await db.ListLeftPushAsync(listKey, latencyMs.ToString());
               await db.ListTrimAsync(listKey, 0, SampleWindow - 1);

               // Evaluate p95 every EvalInterval samples
               if (sampleCount % EvalInterval != 0) return;

               var rawSamples = await db.ListRangeAsync(listKey, 0, SampleWindow - 1);
               var samples = rawSamples
                   .Where(v => v.HasValue)
                   .Select(v => long.Parse(v!))
                   .OrderBy(x => x)
                   .ToList();

               if (samples.Count < 20) return; // Insufficient data guard

               long p95 = samples[(int)Math.Ceiling(samples.Count * 0.95) - 1];

               if (p95 > SlaMs)
               {
                   // Guard against repeated alerts within TTL window
                   bool alreadyFlagged = await db.KeyExistsAsync(AlertKey);
                   if (!alreadyFlagged)
                   {
                       await db.StringSetAsync(AlertKey, requestName, AlertTtl);
                       Log.Warning(
                           "API p95 latency SLA breach: {RequestName} p95={P95Ms}ms (SLA={SlaMs}ms) " +
                           "based on last {SampleCount} samples",
                           requestName, p95, SlaMs, samples.Count);
                   }
               }
           }
           catch (Exception ex)
           {
               Log.Error(ex, "PerformanceBehavior failed to record latency for {RequestName}", requestName);
               // Swallow — never affects primary response path
           }
       }
   }
   ```

6. **`Program.cs` registration**:

   ```csharp
   // FluentValidation — scan all validators in the assembly
   builder.Services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Transient);

   // MediatR behaviors — registered in pipeline execution order
   builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
   builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
   builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));

   // Exception handling middleware
   builder.Services.AddTransient<ExceptionHandlingMiddleware>();

   // Pipeline order: CorrelationId → RequestLogging → ExceptionHandling → [Auth] → ...
   app.UseMiddleware<CorrelationIdMiddleware>();     // from task_001
   app.UseMiddleware<RequestLoggingMiddleware>();    // from task_001
   app.UseMiddleware<ExceptionHandlingMiddleware>(); // from this task
   app.UseAuthentication();
   app.UseAuthorization();
   ```

---

## Current Project State

```
Server/
  Application/
    Common/
      ICorrelationIdAccessor.cs         ← EXISTS (task_001) — used by LoggingBehavior
    Behaviors/                          ← create new folder
  API/
    Middleware/
      CorrelationIdMiddleware.cs        ← EXISTS (task_001)
      RequestLoggingMiddleware.cs       ← EXISTS (task_001)
    Program.cs                         ← EXISTS — MODIFY
```

---

## Expected Changes

| Action | File Path                                              | Description                                                                                                                                                    |
| ------ | ------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| CREATE | `Server/Application/Common/ICommand.cs`                | CQRS marker interfaces: `ICommand`, `ICommand<T>`, `IQuery<T>`                                                                                                 |
| CREATE | `Server/Application/Behaviors/LoggingBehavior.cs`      | `IPipelineBehavior`: logs request entry/exit with correlation ID; `LogContext.PushProperty` scope                                                              |
| CREATE | `Server/Application/Behaviors/ValidationBehavior.cs`   | `IPipelineBehavior`: aggregates `IValidator<TRequest>` results; throws `ValidationException` on failure                                                        |
| CREATE | `Server/Application/Behaviors/PerformanceBehavior.cs`  | `IPipelineBehavior`: Redis LPUSH latency; p95 computation every 10th sample; Serilog Warning + Redis flag on SLA breach                                        |
| CREATE | `Server/API/Middleware/ExceptionHandlingMiddleware.cs` | `IMiddleware`: catches `ValidationException` → `ValidationProblemDetails` HTTP 400 with correlation ID                                                         |
| MODIFY | `Server/API/Program.cs`                                | Register all three behaviors as open-generic; register `ExceptionHandlingMiddleware`; `AddValidatorsFromAssemblyContaining`; correct middleware pipeline order |

---

## External References

- [MediatR 12.x — Pipeline Behaviors](https://github.com/jbogard/MediatR/wiki/Behaviors) — `IPipelineBehavior<TRequest, TResponse>` and `RequestHandlerDelegate<T>`
- [FluentValidation 11.x — ASP.NET Core Integration](https://docs.fluentvalidation.net/en/latest/aspnet.html) — `AddValidatorsFromAssemblyContaining` auto-registration
- [FluentValidation 11.x — `ValidationException`](https://docs.fluentvalidation.net/en/latest/async.html) — `ValidationException.Errors` collection
- [ASP.NET Core 9 — `ValidationProblemDetails`](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-9.0#problem-details) — RFC 9457-compliant per-field error responses
- [StackExchange.Redis — `ListLeftPushAsync` + `ListTrimAsync`](https://stackexchange.github.io/StackExchange.Redis/Basics.html) — fixed-size sliding sample window
- [TR-019 (design.md)](../../../docs/design.md) — MediatR for in-process CQRS; all write/read paths through MediatR
- [TR-020 (design.md)](../../../docs/design.md) — FluentValidation for all API endpoint validation
- [NFR-001 (design.md)](../../../docs/design.md) — 2-second p95 latency SLA for user-facing API requests

---

## Build Commands

- Refer to [`.propel/build/`](../../../../build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [ ] Send a request with invalid input (e.g., missing required field) — verify HTTP 400 `ProblemDetails` response with `errors` object containing per-field messages and `correlationId` extension field
- [ ] Verify Serilog log output for a valid request includes `RequestName`, `DurationMs`, and `CorrelationId` from `LoggingBehavior`
- [ ] Verify Serilog log output for a validation failure includes `CorrelationId` from `ExceptionHandlingMiddleware`
- [ ] Verify `PerformanceBehavior` does not add measurable latency to the response path (fire-and-forget pattern with `_ =` discard)
- [ ] Manually push 20+ latency samples > 2,000ms to Redis list and confirm Serilog Warning emitted; Redis key `api:perf:p95_breach` set with ~10-min TTL
- [ ] Confirm no controller class directly calls a repository interface — all use `ISender.Send()`

---

## Implementation Checklist

- [x] Create `ICommand` / `ICommand<T>` / `IQuery<T>` marker interfaces in `Application/Common/`
- [x] Create `LoggingBehavior<TRequest, TResponse>`: `LogContext.PushProperty("CorrelationId")` scope wrapping `next()`; log entry + exit with duration using message templates
- [x] Create `ValidationBehavior<TRequest, TResponse>`: inject `IEnumerable<IValidator<TRequest>>`; `Task.WhenAll` parallel validation; throw `ValidationException` with all failures if any
- [x] Create `PerformanceBehavior<TRequest, TResponse>`: fire-and-forget `RecordLatencyAsync`; Redis LPUSH+LTRIM(500); p95 computation every 10th sample; Serilog Warning + Redis NX flag (10-min TTL) on SLA breach
- [x] Create `ExceptionHandlingMiddleware`: catch `ValidationException`; return `ValidationProblemDetails` HTTP 400 with `correlationId` extension field; log with `Log.Warning`
- [x] Modify `Program.cs`: register behaviors as open-generic `IPipelineBehavior<,>`; `AddValidatorsFromAssemblyContaining<Program>`; correct middleware order (CorrelationId → RequestLogging → ExceptionHandling → Auth)
- [ ] Verify controllers inject `ISender` (not `IMediator`) and call `sender.Send(new SomeCommand(...))` — no direct repository injection in any controller
