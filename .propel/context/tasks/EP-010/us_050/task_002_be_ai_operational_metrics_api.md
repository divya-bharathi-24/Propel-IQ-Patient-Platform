# Task - task_002_be_ai_operational_metrics_api

## Requirement Reference

- **User Story:** us_050 — AI Operational Controls — Circuit Breaker, Token Budget & Model Swap
- **Story Location:** `.propel/context/tasks/EP-010/us_050/us_050.md`
- **Acceptance Criteria:**
  - AC-4: Operator views AI metrics dashboard showing token consumption per request, p95 latency (target ≤30s), error rates, circuit breaker trips, and confidence score distributions — all data updated within 60 seconds (AIR-O04).
  - AC-3 (operator config write path): Operator changes model version in system configuration; new version effective within 5 minutes — requires POST /api/admin/ai-config to write model version to Redis key `ai:config:model_version` (AIR-O03).
- **Edge Cases:**
  - p95 latency computation: fewer than 20 samples — return `null` p95 with `"InsufficientData"` status rather than a misleading value.
  - Circuit breaker trip count: count trips in last 24 hours from the `AiOperationalMetrics` table (persisted events), not only from Redis (which is ephemeral).

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
| ORM      | Entity Framework Core                 | 9.x     |
| CQRS     | MediatR                               | 12.x    |
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

Implement two backend concerns for US_050:

1. **Operational metrics write path** — `IAiOperationalMetricsWriter` + `EfAiOperationalMetricsWriter`: fire-and-forget INSERT of per-request metrics events (token consumption, latency, error, circuit breaker trip) into the `AiOperationalMetrics` table (created in task_004).  The writer is called by `CircuitBreakerFilter` (trip events + error events) and `TokenBudgetFilter` (token consumption events) from task_001; latency is recorded by the existing `AiExtractionOrchestrator` via stopwatch timing.

2. **Admin REST endpoints**:
   - `GET /api/admin/ai-metrics/operational` (Admin-only) — returns aggregate operational metrics: p95 latency, avg/total token consumption, error rate, circuit breaker trip count (last 24h), real-time circuit breaker open state (from Redis key `ai:cb:open`), confidence score distribution summary.
   - `POST /api/admin/ai-config/model-version` (Admin-only) — accepts `{ "modelVersion": "gpt-4o-2024-11-20" }`, validates against allowed version list, writes to Redis key `ai:config:model_version`, emits AuditLog entry.

Both endpoints are CQRS via MediatR. The GET endpoint computes p95 from the last 500 latency records (configurable via `appsettings.json` `AiResilience:MetricsWindowSize`).

---

## Dependent Tasks

- `EP-010/us_050/task_004_db_ai_operational_metrics_schema.md` — `AiOperationalMetrics` table must exist.
- `EP-010/us_050/task_001_ai_operational_resilience_pipeline.md` — `CircuitBreakerFilter` and `TokenBudgetFilter` call `IAiOperationalMetricsWriter`; Redis key `ai:cb:open` read by GET endpoint.
- `EP-010/us_048/task_002_be_ai_metrics_api.md` — `GET /api/admin/ai-metrics/summary` already exists for quality metrics; this task adds the operational metrics endpoint as a sibling (different resource path, same controller file).

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `IAiOperationalMetricsWriter` (new) | AI Application | CREATE — write-only interface: `RecordTokenConsumptionAsync`, `RecordLatencyAsync`, `RecordProviderErrorAsync`, `RecordCircuitBreakerTripAsync` |
| `EfAiOperationalMetricsWriter` (new) | Infrastructure | CREATE — EF Core INSERT-only; try/catch swallows exceptions (fire-and-forget contract); Serilog Error on failure |
| `IAiOperationalMetricsReadRepository` (new) | Infrastructure | CREATE — read-only interface: `GetLatencyRecordsAsync(int n)`, `GetTokenConsumptionRecordsAsync(int n)`, `GetErrorRecordsAsync(TimeSpan window)`, `GetCircuitBreakerTripCountAsync(TimeSpan window)` |
| `EfAiOperationalMetricsReadRepository` (new) | Infrastructure | CREATE — EF Core queries against `AiOperationalMetrics`; no OFFSET pagination — keyset on `(recordedAt DESC, id DESC)` |
| `GetAiOperationalMetricsSummaryQuery` + handler (new) | Application | CREATE — MediatR query; compute p95 latency, avg tokens, error rate, CB trips, CB open state; return `AiOperationalMetricsSummaryResponse` |
| `UpdateAiModelVersionCommand` + handler (new) | Application | CREATE — MediatR command; validate model version whitelist; write to Redis; AuditLog |
| `AiMetricsController` (existing, from US_048) | API | MODIFY — add GET `/operational` route + POST `/ai-config/model-version` route |
| `AiExtractionOrchestrator` (existing) | AI Application | MODIFY — add stopwatch timing; call `IAiOperationalMetricsWriter.RecordLatencyAsync()` on completion/failure |

---

## Implementation Plan

1. **`IAiOperationalMetricsWriter` interface**:

   ```csharp
   public interface IAiOperationalMetricsWriter
   {
       Task RecordTokenConsumptionAsync(Guid sessionId, string modelVersion, int promptTokens, int responseTokens);
       Task RecordLatencyAsync(Guid sessionId, string modelVersion, long latencyMs);
       Task RecordProviderErrorAsync(Guid sessionId, string modelVersion, string errorType);
       Task RecordCircuitBreakerTripAsync(string modelVersion, int tripCountThisHour, TimeSpan openDuration);
   }
   ```

2. **`EfAiOperationalMetricsWriter` — INSERT-only, swallows exceptions**:

   ```csharp
   public sealed class EfAiOperationalMetricsWriter : IAiOperationalMetricsWriter
   {
       public async Task RecordTokenConsumptionAsync(Guid sessionId, string modelVersion, int promptTokens, int responseTokens)
       {
           try
           {
               var entity = new AiOperationalMetric
               {
                   Id = Guid.NewGuid(),
                   MetricType = AiOperationalMetricType.TokenConsumption,
                   SessionId = sessionId,
                   ModelVersion = modelVersion,
                   ValueA = promptTokens,  // prompt tokens
                   ValueB = responseTokens, // response tokens
                   RecordedAt = DateTimeOffset.UtcNow
               };
               _context.AiOperationalMetrics.Add(entity);
               await _context.SaveChangesAsync();
           }
           catch (Exception ex)
           {
               Log.Error(ex, "Failed to record token consumption metric");
               // Swallow — metrics writes must not affect primary clinical write path
           }
       }
       // ... RecordLatencyAsync, RecordProviderErrorAsync, RecordCircuitBreakerTripAsync
   }
   ```

3. **`GetAiOperationalMetricsSummaryQueryHandler` — p95 latency, token stats, error rate**:

   ```csharp
   // p95 latency computation
   var latencyRecords = await _metricsRepo.GetLatencyRecordsAsync(windowSize); // Last 500
   double? p95Latency = latencyRecords.Count >= 20
       ? ComputePercentile(latencyRecords.Select(r => (double)r.ValueA!), 95)
       : null;

   // Token consumption
   var tokenRecords = await _metricsRepo.GetTokenConsumptionRecordsAsync(windowSize);
   double avgPromptTokens = tokenRecords.Count > 0 ? tokenRecords.Average(r => r.ValueA!.Value) : 0;
   double avgResponseTokens = tokenRecords.Count > 0 ? tokenRecords.Average(r => r.ValueB!.Value) : 0;

   // Error rate = errors / (errors + successes) in last 1 hour
   var errorCount = await _metricsRepo.GetErrorRecordsAsync(TimeSpan.FromHours(1));
   var latencyCount1h = await _metricsRepo.GetLatencyRecordsCountAsync(TimeSpan.FromHours(1));
   double errorRate = (errorCount + latencyCount1h) > 0
       ? (double)errorCount / (errorCount + latencyCount1h)
       : 0;

   // Circuit breaker — combine Redis (real-time) + DB (24h history)
   bool cbOpen = await _redis.KeyExistsAsync("ai:cb:open");
   int cbTrips24h = await _metricsRepo.GetCircuitBreakerTripCountAsync(TimeSpan.FromHours(24));
   ```

   `ComputePercentile` — in-memory LINQ sort + index: `sorted[(int)Math.Ceiling(n * percentile / 100.0) - 1]`.

4. **`UpdateAiModelVersionCommand` — model version hot-swap**:

   ```csharp
   // Validation: whitelist of allowed model versions from appsettings
   var allowed = _options.CurrentValue.AllowedModelVersions; // string[]
   if (!allowed.Contains(request.ModelVersion))
       return Result.Fail($"Model version '{request.ModelVersion}' is not in the allowed list");

   await _redis.StringSetAsync("ai:config:model_version", request.ModelVersion);
   await _auditLog.RecordAsync(new AuditLogEntry
   {
       ActionType = "AiModelVersionUpdated",
       EntityType = "AiConfig",
       Before = JsonSerializer.Serialize(new { oldVersion = currentVersion }),
       After = JsonSerializer.Serialize(new { newVersion = request.ModelVersion }),
       UserId = _currentUser.UserId
   });
   ```

5. **`AiMetricsController` — add two routes** (sibling to existing `/summary` route from US_048):

   ```csharp
   [HttpGet("operational")]
   [Authorize(Roles = "Admin")]
   public async Task<IActionResult> GetOperationalSummary(CancellationToken ct)
       => Ok(await _mediator.Send(new GetAiOperationalMetricsSummaryQuery(), ct));

   [HttpPost("model-version")]  // Route: POST /api/admin/ai-config/model-version
   [Authorize(Roles = "Admin")]
   public async Task<IActionResult> UpdateModelVersion([FromBody] UpdateAiModelVersionRequest request, CancellationToken ct)
       => Ok(await _mediator.Send(new UpdateAiModelVersionCommand(request.ModelVersion), ct));
   ```

   The model version update route belongs on a new `AiConfigController` (or separate route group) to keep concerns separate from metrics. Add `[Route("api/admin/ai-config")]` as new controller.

6. **`AiExtractionOrchestrator` — stopwatch timing for latency recording**:

   ```csharp
   var sw = Stopwatch.StartNew();
   try
   {
       var result = await InvokeKernelAsync(sessionId, document, ct);
       sw.Stop();
       _ = _metricsWriter.RecordLatencyAsync(sessionId, _modelConfig.GetModelVersionAsync().Result, sw.ElapsedMilliseconds);
       return result;
   }
   catch (Exception ex)
   {
       sw.Stop();
       _ = _metricsWriter.RecordProviderErrorAsync(sessionId, "unknown", ex.GetType().Name);
       throw;
   }
   ```

   Fire-and-forget calls use `_ =` discard pattern (the writer already swallows exceptions, so no `await` required for the metrics write).

---

## Current Project State

```
Server/
  API/
    Controllers/
      AiMetricsController.cs            ← EXISTS (US_048) — MODIFY
  Application/
    Queries/
      GetAiMetricsSummaryQuery.cs       ← EXISTS (US_048)
    Commands/                           ← existing
  Infrastructure/
    Persistence/
      ApplicationDbContext.cs           ← EXISTS
      Repositories/                     ← existing
  AI/
    Orchestration/
      AiExtractionOrchestrator.cs       ← EXISTS — MODIFY
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Application/AI/IAiOperationalMetricsWriter.cs` | Write-only interface: 4 record methods (token consumption, latency, provider error, CB trip) |
| CREATE | `Server/Infrastructure/AI/EfAiOperationalMetricsWriter.cs` | EF Core INSERT-only; try/catch swallows exceptions |
| CREATE | `Server/Infrastructure/AI/EfAiOperationalMetricsReadRepository.cs` | Read-only repository: keyset queries on `AiOperationalMetrics` |
| CREATE | `Server/Application/Queries/GetAiOperationalMetricsSummaryQuery.cs` | MediatR query + handler: p95 latency, avg tokens, error rate, CB state |
| CREATE | `Server/Application/Commands/UpdateAiModelVersionCommand.cs` | MediatR command + handler: whitelist validation, Redis write, AuditLog |
| CREATE | `Server/API/Controllers/AiConfigController.cs` | POST /api/admin/ai-config/model-version (Admin-only) |
| CREATE | `Server/Application/DTOs/AiOperationalMetricsSummaryResponse.cs` | Response DTO: p95LatencyMs?, avgPromptTokens, avgResponseTokens, errorRate, cbTrips24h, cbOpen, status |
| MODIFY | `Server/API/Controllers/AiMetricsController.cs` | Add GET /api/admin/ai-metrics/operational route |
| MODIFY | `Server/AI/Orchestration/AiExtractionOrchestrator.cs` | Add stopwatch timing; call `IAiOperationalMetricsWriter.RecordLatencyAsync` + `RecordProviderErrorAsync` |

---

## External References

- [MediatR 12.x Docs](https://github.com/jbogard/MediatR/wiki) — `IRequest<T>` + `IRequestHandler<TRequest, TResponse>` CQRS pattern
- [EF Core 9 — Querying](https://learn.microsoft.com/en-us/ef/core/querying/) — LINQ `OrderByDescending`, `Take`, `Where` for keyset-style queries
- [StackExchange.Redis — KeyExistsAsync](https://stackexchange.github.io/StackExchange.Redis/Basics.html) — Check `ai:cb:open` real-time circuit breaker state
- [AIR-O03 (design.md)](../../../docs/design.md) — Model version change effective within 5 minutes; Redis key as config store
- [AIR-O04 (design.md)](../../../docs/design.md) — Token consumption, latency, error rates, CB trips, confidence distributions

---

## Build Commands

- Refer to [`.propel/build/`](../../../../build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [ ] `EfAiOperationalMetricsWriter.RecordTokenConsumptionAsync` inserts a row in `AiOperationalMetrics` with `MetricType = TokenConsumption`
- [ ] `EfAiOperationalMetricsWriter` swallows exceptions without propagating to caller (fire-and-forget contract)
- [ ] GET /api/admin/ai-metrics/operational returns `null` p95LatencyMs with `status = "InsufficientData"` when fewer than 20 latency records exist
- [ ] GET /api/admin/ai-metrics/operational reflects `cbOpen = true` within 1 second of Redis key `ai:cb:open` being set by `CircuitBreakerFilter`
- [ ] POST /api/admin/ai-config/model-version rejects model versions not in `AllowedModelVersions` whitelist with HTTP 400
- [ ] POST /api/admin/ai-config/model-version writes to Redis key `ai:config:model_version` and emits AuditLog entry
- [ ] `AiExtractionOrchestrator` records latency on success; records provider error on exception; does not await metrics writes (fire-and-forget)
- [ ] Both endpoints return HTTP 403 for non-Admin authenticated callers

---

## Implementation Checklist

- [ ] Create `IAiOperationalMetricsWriter` interface with 4 record methods (token consumption, latency, provider error, circuit breaker trip)
- [ ] Create `EfAiOperationalMetricsWriter`: INSERT-only, try/catch swallow, Serilog Error on failure; register as `IAiOperationalMetricsWriter` singleton
- [ ] Create `EfAiOperationalMetricsReadRepository`: keyset queries for last-N records per `MetricType`; `GetCircuitBreakerTripCountAsync` for 24h window
- [ ] Create `GetAiOperationalMetricsSummaryQuery` + handler: p95 latency (null guard < 20 samples), avg tokens, error rate, CB trips from DB, CB open from Redis
- [ ] Create `UpdateAiModelVersionCommand` + handler: whitelist validation from `AiResilience:AllowedModelVersions`; Redis write; AuditLog; return model version confirmation
- [ ] Create `AiConfigController` with POST /api/admin/ai-config/model-version; `[Authorize(Roles = "Admin")]`
- [ ] Modify `AiMetricsController`: add GET `/operational` route delegating to `GetAiOperationalMetricsSummaryQuery`
- [ ] Modify `AiExtractionOrchestrator`: `Stopwatch` timing; fire-and-forget `RecordLatencyAsync` on success; fire-and-forget `RecordProviderErrorAsync` on exception
