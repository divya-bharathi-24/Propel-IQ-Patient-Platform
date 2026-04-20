# Task - task_002_be_ai_metrics_api

## Requirement Reference

- **User Story:** us_048 — AI Quality Monitoring & Hallucination Rate Control
- **Story Location:** `.propel/context/tasks/EP-010/us_048/us_048.md`
- **Acceptance Criteria:**
  - AC-1: Agreement metric events (staff confirm/reject decisions) are persisted to `AiQualityMetrics` with session ID, field name, staff decision, and timestamp; the rolling AI-Human Agreement Rate is calculable from stored rows.
  - AC-2: Schema validity events are persisted for each AI output with `isSchemaValid` flag; the system-wide schema validity rate is calculable.
  - AC-3: Hallucination events (staff marks AI output incorrect) are persisted; the rolling hallucination rate is calculable against verified sample count; insufficient-data guard returns `null` when < 50 samples.
  - AC-4: `GET /api/admin/ai-metrics/summary` returns current computed rates (agreementRate, hallucinationRate, schemaValidityRate, sampleCounts) to the Admin; returns HTTP 403 for non-Admin callers.
- **Edge Cases:**
  - Concurrent metric writes during high-volume extractions: `AiQualityMetrics` is INSERT-only (no UPDATE); concurrent inserts do not require locking — append-only design eliminates write contention.
  - Zero-division guard: if no verified samples exist yet, all rates are returned as `null` in the summary response with `status = "InsufficientData"`.

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

| Layer              | Technology              | Version |
| ------------------ | ----------------------- | ------- |
| Backend            | ASP.NET Core Web API    | .net 10  |
| Backend Messaging  | MediatR                 | 12.x    |
| Backend Validation | FluentValidation        | 11.x    |
| ORM                | Entity Framework Core   | 9.x     |
| Database           | PostgreSQL              | 16+     |
| Logging            | Serilog                 | 4.x     |

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

Implement the backend persistence and query surface for AI quality metrics. This task provides:

1. **`IAiMetricsWriter` implementation** — `EfAiMetricsWriter`: writes agreement, hallucination, and schema-validity events to `AiQualityMetrics` via INSERT-only EF Core repository (consistent with AD-7 append-only pattern).
2. **`IAiMetricsReadRepository` implementation** — `EfAiMetricsReadRepository`: provides rolling-window queries used by `HallucinationRateEvaluator` and `AgreementRateEvaluator` (implemented in task_001).
3. **`GET /api/admin/ai-metrics/summary`** — Admin-only summary endpoint: returns pre-computed rolling rates (agreementRate, hallucinationRate, schemaValidityRate) and sample counts. Backed by `GetAiMetricsSummaryQuery` / handler that computes rates from the most recent 200 events per metric type.

All metric writes are fire-and-forget inserts — they do not affect the primary clinical data write path (no transaction coupling).

---

## Dependent Tasks

- `task_003_db_ai_quality_metrics_schema.md` (EP-010/us_048) — `AiQualityMetrics` table must exist before writers can insert.
- `task_001_ai_quality_monitoring_service.md` (EP-010/us_048) — `IAiMetricsWriter` and `IAiMetricsReadRepository` interfaces defined in task_001 are implemented here.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `AiMetricsController` (new) | AI Module | CREATE — Admin-only REST controller: `GET /api/admin/ai-metrics/summary` |
| `GetAiMetricsSummaryQuery` (new) | AI Module | CREATE — MediatR query: no parameters |
| `GetAiMetricsSummaryQueryHandler` (new) | AI Module | CREATE — Computes agreementRate, hallucinationRate, schemaValidityRate from last 200 events each |
| `AiMetricsSummaryResponse` (new) | Shared Contracts | CREATE — DTO: agreementRate?, hallucinationRate?, schemaValidityRate?, agreementSampleCount, hallucinationSampleCount, schemaValiditySampleCount, status |
| `IAiMetricsReadRepository` (new) | Infrastructure | CREATE — Interface: GetRecentAgreementEventsAsync, GetRecentVerifiedSamplesAsync, GetRecentSchemaValidityEventsAsync |
| `EfAiMetricsReadRepository` (new) | Infrastructure | CREATE — EF Core implementation of read repository: last-N queries per metric type |
| `EfAiMetricsWriter` (new) | Infrastructure | CREATE — Implements IAiMetricsWriter: INSERT-only EF Core writes for all three event types |
| `AiModuleRegistration` (existing) | DI Bootstrap | MODIFY — Register IAiMetricsReadRepository, EfAiMetricsWriter, controller handler |

---

## Implementation Plan

1. **Define `AiMetricsSummaryResponse`** DTO:
   - `decimal? AgreementRate` — null when < 50 samples
   - `decimal? HallucinationRate` — null when < 50 verified samples
   - `decimal? SchemaValidityRate` — null when < 50 samples
   - `int AgreementSampleCount`, `int HallucinationSampleCount`, `int SchemaValiditySampleCount`
   - `string Status` — `"OK"` | `"InsufficientData"` | `"AgreementRateAlert"` | `"HallucinationAlert"`

2. **Implement `IAiMetricsReadRepository` / `EfAiMetricsReadRepository`**:
   - `GetRecentAgreementEventsAsync(int windowSize) → List<AiQualityMetric>`:
     - `SELECT TOP {windowSize} FROM AiQualityMetrics WHERE MetricType = 'Agreement' ORDER BY RecordedAt DESC`
   - `GetRecentVerifiedSamplesAsync(int windowSize) → List<AiQualityMetric>`:
     - `SELECT TOP {windowSize} FROM AiQualityMetrics WHERE MetricType = 'Hallucination' ORDER BY RecordedAt DESC`
   - `GetRecentSchemaValidityEventsAsync(int windowSize) → List<AiQualityMetric>`:
     - `SELECT TOP {windowSize} FROM AiQualityMetrics WHERE MetricType = 'SchemaValidity' ORDER BY RecordedAt DESC`
   - All queries use EF Core `.Take(windowSize).OrderByDescending(x => x.RecordedAt)` — no raw SQL.

3. **Implement `EfAiMetricsWriter`** — implements `IAiMetricsWriter`:
   - `WriteAgreementEventAsync(Guid sessionId, string fieldName, bool isAgreement)`:
     - INSERT `AiQualityMetric { MetricType = "Agreement", SessionId, FieldName, IsAgreement, RecordedAt = UtcNow }`.
   - `WriteHallucinationEventAsync(Guid sessionId, string fieldName, bool isHallucination)`:
     - INSERT `AiQualityMetric { MetricType = "Hallucination", SessionId, FieldName, IsHallucination, RecordedAt = UtcNow }`.
   - `WriteSchemaValidityEventAsync(Guid sessionId, bool isValid)`:
     - INSERT `AiQualityMetric { MetricType = "SchemaValidity", SessionId, IsSchemaValid = isValid, RecordedAt = UtcNow }`.
   - All writes use `_context.AiQualityMetrics.AddAsync(entity)` + `SaveChangesAsync()`. No UPDATE or DELETE calls — INSERT-only consistent with AD-7.

4. **Implement `GetAiMetricsSummaryQueryHandler`**:
   - Call `GetRecentAgreementEventsAsync(200)` → compute `agreementRate = agreementCount / total`.
   - Call `GetRecentVerifiedSamplesAsync(200)` → compute `hallucinationRate = hallucinatedCount / total`.
   - Call `GetRecentSchemaValidityEventsAsync(200)` → compute `schemaValidityRate = validCount / total`.
   - Apply null guard: rate = null if corresponding sample count < 50.
   - Determine `Status`:
     - `"InsufficientData"` if all three rates are null.
     - `"AgreementRateAlert"` if `agreementRate != null && agreementRate < 0.98`.
     - `"HallucinationAlert"` if `hallucinationRate != null && hallucinationRate > 0.02`.
     - `"OK"` otherwise.
   - Return `AiMetricsSummaryResponse`.

5. **Implement `AiMetricsController`**:
   - `[ApiController] [Route("api/admin/ai-metrics")] [Authorize(Roles = "Admin")]`
   - `[HttpGet("summary")]` — dispatches `GetAiMetricsSummaryQuery`; returns `200 AiMetricsSummaryResponse`.
   - Annotate with `[ProducesResponseType<AiMetricsSummaryResponse>(200)]` and `[ProducesResponseType(403)]`.

6. **Register in `AiModuleRegistration`** — add `IAiMetricsReadRepository → EfAiMetricsReadRepository` (scoped), `IAiMetricsWriter → EfAiMetricsWriter` (scoped), and `GetAiMetricsSummaryQueryHandler`.

---

## Current Project State

```
Server/
  AI/
    Metrics/                            ← folder created in task_001
    Guardrails/                         ← folder created in task_001
  Infrastructure/
    Persistence/
      AuditLog/                         ← EXISTS (US_047) — read-only repo pattern to follow
  DI/
    AiModuleRegistration.cs             ← EXISTS — MODIFY
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/AI/Controllers/AiMetricsController.cs` | Admin-only GET /summary controller |
| CREATE | `Server/AI/Queries/GetAiMetricsSummaryQuery.cs` | MediatR query (no params) |
| CREATE | `Server/AI/Queries/GetAiMetricsSummaryQueryHandler.cs` | Compute three rolling rates; null guard; status field |
| CREATE | `Server/Infrastructure/Persistence/AiMetrics/IAiMetricsReadRepository.cs` | Read-only interface: three rolling-window query methods |
| CREATE | `Server/Infrastructure/Persistence/AiMetrics/EfAiMetricsReadRepository.cs` | EF Core: Take(200) OrderByDescending per metric type |
| CREATE | `Server/Infrastructure/Persistence/AiMetrics/EfAiMetricsWriter.cs` | INSERT-only writer: agreement, hallucination, schema-validity events |
| CREATE | `Server/Shared/Contracts/AiMetricsSummaryResponse.cs` | Response DTO with nullable rates, sample counts, status |
| MODIFY | `Server/DI/AiModuleRegistration.cs` | Register IAiMetricsReadRepository, EfAiMetricsWriter, query handler |

---

## External References

- [EF Core 9 — AddAsync + SaveChangesAsync (INSERT-only pattern)](https://learn.microsoft.com/en-us/ef/core/saving/basic) — Consistent with AD-7 append-only strategy
- [MediatR 12.x — IRequest / IRequestHandler](https://github.com/jbogard/MediatR/wiki) — Query dispatch; no command needed (writes are internal)
- [ASP.NET Core 9 — Role-Based Authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-9.0) — `[Authorize(Roles = "Admin")]`
- [AIR-Q01 (design.md)](../.propel/context/docs/design.md) — ≥98% AI-Human Agreement Rate
- [AIR-Q03 (design.md)](../.propel/context/docs/design.md) — ≥99% schema validity
- [AIR-Q04 (design.md)](../.propel/context/docs/design.md) — <2% hallucination rate
- [AIR-O04 (design.md)](../.propel/context/docs/design.md) — Track and report AI model usage metrics
- [NFR-011 (design.md)](../.propel/context/docs/design.md) — AI-Human Agreement Rate system-level requirement
- [AD-7 (design.md)](../.propel/context/docs/design.md) — Append-only INSERT pattern; no UPDATE or DELETE on metric rows

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [ ] Unit tests pass (xUnit + Moq)
- [ ] `EfAiMetricsWriter.WriteAgreementEventAsync` inserts a row with `MetricType = "Agreement"` and correct `IsAgreement` value
- [ ] `EfAiMetricsWriter` never calls `Update` or `Remove` — INSERT-only verified in unit test
- [ ] `GetAiMetricsSummaryQueryHandler`: 180 agreement events (160 agree, 20 reject) → `agreementRate = 0.889`; `status = "AgreementRateAlert"`
- [ ] `GetAiMetricsSummaryQueryHandler`: 30 samples across all types → all rates `null`; `status = "InsufficientData"`
- [ ] `GetAiMetricsSummaryQueryHandler`: hallucination rate 3% with 60 samples → `status = "HallucinationAlert"`
- [ ] `GET /api/admin/ai-metrics/summary` returns HTTP 403 for Staff and Patient callers
- [ ] `GET /api/admin/ai-metrics/summary` returns HTTP 200 with populated `AiMetricsSummaryResponse` for Admin

---

## Implementation Checklist

- [ ] Create `AiMetricsSummaryResponse` DTO (nullable rates, sample counts, status string)
- [ ] Create `IAiMetricsReadRepository` interface (three rolling-window query methods)
- [ ] Create `EfAiMetricsReadRepository` (Take(200) OrderByDescending per MetricType; EF Core only)
- [ ] Create `EfAiMetricsWriter` implementing `IAiMetricsWriter` (INSERT-only; three event type methods)
- [ ] Create `GetAiMetricsSummaryQuery` and `GetAiMetricsSummaryQueryHandler` (compute rates, null guard, status field)
- [ ] Create `AiMetricsController` with single `[HttpGet("summary")]` Admin-only action
- [ ] Register `IAiMetricsReadRepository`, `EfAiMetricsWriter`, query handler in `AiModuleRegistration`
- [ ] Verify no `UPDATE` or `DELETE` calls exist in any `AiMetrics` persistence path
