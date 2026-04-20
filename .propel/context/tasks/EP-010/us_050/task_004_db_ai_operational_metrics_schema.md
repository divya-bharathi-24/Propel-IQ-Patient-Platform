# Task - task_004_db_ai_operational_metrics_schema

## Requirement Reference

- **User Story:** us_050 — AI Operational Controls — Circuit Breaker, Token Budget & Model Swap
- **Story Location:** `.propel/context/tasks/EP-010/us_050/us_050.md`
- **Acceptance Criteria:**
  - AC-4: Per-request operational metrics (token consumption, latency, provider errors, circuit breaker trips) must be persisted to support dashboard queries for p95 latency, error rates, and trip counts without relying solely on ephemeral Redis state (AIR-O04).
  - AC-1/AC-2: Circuit breaker trip events and token truncation events are persisted with `MetricType` discriminator to allow auditing and trend analysis beyond the 5-minute Redis TTL window.

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

| Layer    | Technology              | Version |
| -------- | ----------------------- | ------- |
| ORM      | Entity Framework Core   | 9.x     |
| Database | PostgreSQL              | 16+     |

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

Create the `AiOperationalMetrics` PostgreSQL table via EF Core code-first migration. This table is separate from `AiQualityMetrics` (US_048) for two reasons:

1. **Different access patterns** — operational metrics (latency, token counts) are queried by time window (`WHERE MetricType = X ORDER BY RecordedAt DESC LIMIT N`) for rolling-window statistics. Quality metrics (agreement rate, hallucination rate) are queried by type for rate calculations. Mixing them in one table would create hot-spot contention at high write volume.
2. **Different cardinality** — at peak load, one operational metric row is written per AI request (token + latency = 2 rows per invocation), whereas quality metrics are written only when staff confirm/reject. Separate tables allow independent retention policies.

The entity uses a **polymorphic discriminator** pattern (same as `AiQualityMetric` in US_048) with four `MetricType` values:
- `TokenConsumption` — `ValueA` = promptTokens, `ValueB` = responseTokens
- `Latency` — `ValueA` = latencyMs (long stored as decimal)
- `ProviderError` — `Metadata` = error type (e.g., "Timeout", "RateLimit", "HTTP5xx")
- `CircuitBreakerTrip` — `ValueA` = tripCountThisHour, `Metadata` = open duration minutes

All properties use `init` accessors (INSERT-only enforcement, AD-7). No FK constraints on `SessionId` or `ModelVersion` — decoupled from clinical entities to avoid blocking metrics writes during high-volume ingestion.

---

## Dependent Tasks

- No external dependencies — foundational schema for this US.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `AiOperationalMetric` entity (new) | Domain | CREATE — EF Core entity: 8 columns, all `init`, immutable; polymorphic MetricType discriminator |
| `AiOperationalMetricType` enum (new) | Domain | CREATE — `TokenConsumption = 0`, `Latency = 1`, `ProviderError = 2`, `CircuitBreakerTrip = 3` |
| `AiOperationalMetricConfiguration` (new) | Infrastructure | CREATE — EF Core Fluent API: column types, 3 indexes |
| `AddAiOperationalMetricsTable` migration (new) | Infrastructure | CREATE — EF Core migration: CREATE TABLE + 3 CREATE INDEX |
| `ApplicationDbContext` (existing) | Infrastructure | MODIFY — Add `DbSet<AiOperationalMetric> AiOperationalMetrics` |

---

## Implementation Plan

1. **Define `AiOperationalMetricType` enum**:

   ```csharp
   public enum AiOperationalMetricType
   {
       TokenConsumption   = 0,
       Latency            = 1,
       ProviderError      = 2,
       CircuitBreakerTrip = 3,
   }
   ```

2. **Define `AiOperationalMetric` entity**:

   ```csharp
   public class AiOperationalMetric
   {
       public Guid Id { get; init; }
       public AiOperationalMetricType MetricType { get; init; }
       public Guid? SessionId { get; init; }          // null for CB trips (cross-session)
       public string ModelVersion { get; init; }      // e.g., "gpt-4o-2024-11-20"
       public decimal? ValueA { get; init; }          // Primary numeric value (tokens, ms, trip count)
       public decimal? ValueB { get; init; }          // Secondary numeric value (response tokens)
       public string? Metadata { get; init; }         // JSON string for error type or CB duration
       public DateTimeOffset RecordedAt { get; init; }
   }
   ```

   `ValueA` and `ValueB` use `decimal?` (nullable) — not all metric types use both. `Metadata` stores type-specific string context for error and CB trip events.

3. **Configure `AiOperationalMetricConfiguration`** (EF Core Fluent API):
   - Table: `"AiOperationalMetrics"`
   - `Id`: PK, `ValueGeneratedNever()` (caller supplies Guid).
   - `MetricType`: required, `HasConversion<int>()` (stored as integer for efficient index scan).
   - `ModelVersion`: required, `HasMaxLength(100)`.
   - `ValueA`: optional, `HasColumnType("numeric(18,4)")` — covers token counts (int range) and latency (ms, up to 8 digits) without precision loss.
   - `ValueB`: optional, `HasColumnType("numeric(18,4)")`.
   - `Metadata`: optional, `HasMaxLength(1000)` — bounded; not `text` (CB reason + error type are short strings).
   - `RecordedAt`: required, `HasColumnType("timestamptz")`.
   - **Index 1**: `HasIndex(x => new { x.MetricType, x.RecordedAt }).IsDescending(false, true)` — named `"IX_AiOperationalMetrics_MetricType_RecordedAt"`. Primary access pattern: `WHERE MetricType = X ORDER BY RecordedAt DESC LIMIT N`.
   - **Index 2**: `HasIndex(x => x.RecordedAt).IsDescending(true)` — named `"IX_AiOperationalMetrics_RecordedAt"`. Supports time-window count queries across all types (e.g., error rate per hour).
   - **Index 3**: `HasIndex(x => x.SessionId)` — named `"IX_AiOperationalMetrics_SessionId"`. Supports per-session diagnostics queries.

4. **Generate EF Core migration** `AddAiOperationalMetricsTable`:
   - `Up()`:
     - `CREATE TABLE "AiOperationalMetrics" (...)` with all 8 columns.
     - `CREATE INDEX "IX_AiOperationalMetrics_MetricType_RecordedAt" ON "AiOperationalMetrics" ("MetricType" ASC, "RecordedAt" DESC)`.
     - `CREATE INDEX "IX_AiOperationalMetrics_RecordedAt" ON "AiOperationalMetrics" ("RecordedAt" DESC)`.
     - `CREATE INDEX "IX_AiOperationalMetrics_SessionId" ON "AiOperationalMetrics" ("SessionId") WHERE "SessionId" IS NOT NULL`.
   - `Down()`: `DROP TABLE IF EXISTS "AiOperationalMetrics"` — cascades all three indexes.

5. **Add `DbSet` to `ApplicationDbContext`**:
   - `public DbSet<AiOperationalMetric> AiOperationalMetrics => Set<AiOperationalMetric>();`

---

## Current Project State

```
Server/
  Infrastructure/
    Persistence/
      ApplicationDbContext.cs                                    ← EXISTS — MODIFY
      Configurations/
        AiQualityMetricConfiguration.cs                         ← EXISTS (US_048)
        AiPromptAuditLogConfiguration.cs                        ← EXISTS (US_049)
      Migrations/                                               ← existing migrations folder
  Domain/
    AiQualityMetric.cs                                          ← EXISTS (US_048) — reference for entity pattern
    AiPromptAuditLog.cs                                         ← EXISTS (US_049) — reference for entity pattern
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Domain/AiOperationalMetricType.cs` | Enum: `TokenConsumption`, `Latency`, `ProviderError`, `CircuitBreakerTrip` |
| CREATE | `Server/Domain/AiOperationalMetric.cs` | Immutable entity: 8 columns, all `init` accessors; `ValueA`/`ValueB` numeric; `Metadata` string |
| CREATE | `Server/Infrastructure/Persistence/Configurations/AiOperationalMetricConfiguration.cs` | Fluent API: column types, 3 indexes (composite MetricType+RecordedAt, RecordedAt, partial SessionId) |
| CREATE | `Server/Infrastructure/Persistence/Migrations/YYYYMMDD_AddAiOperationalMetricsTable.cs` | EF Core migration: CREATE TABLE + 3 indexes; Down() drops table |
| MODIFY | `Server/Infrastructure/Persistence/ApplicationDbContext.cs` | Add `DbSet<AiOperationalMetric> AiOperationalMetrics` |

---

## External References

- [EF Core 9 — Composite Index Sort Order](https://learn.microsoft.com/en-us/ef/core/modeling/indexes#index-sort-order) — `.IsDescending(false, true)` for `(MetricType ASC, RecordedAt DESC)` composite index
- [PostgreSQL 16 — Partial Indexes](https://www.postgresql.org/docs/16/indexes-partial.html) — `WHERE "SessionId" IS NOT NULL` to skip null session rows (CB trip events)
- [PostgreSQL 16 — `numeric` type](https://www.postgresql.org/docs/16/datatype-numeric.html) — `numeric(18,4)` for token counts and latency ms without float precision issues
- [AiQualityMetric (US_048 task_003)](../us_048/task_003_db_ai_quality_metrics_schema.md) — reference polymorphic discriminator pattern with `MetricType` enum
- [AIR-O04 (design.md)](../../../docs/design.md) — Persist token consumption, latency, error rates, CB trips for operational monitoring
- [AD-7 (design.md)](../../../docs/design.md) — Append-only audit/metrics tables; `init` accessor pattern

---

## Build Commands

- Refer to [`.propel/build/`](../../../../build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [ ] EF Core migration applies cleanly to local PostgreSQL 16 instance
- [ ] `AiOperationalMetrics` table created with 8 columns and correct types (`numeric`, `timestamptz`, `integer`, `uuid`, `varchar`)
- [ ] `IX_AiOperationalMetrics_MetricType_RecordedAt` composite index confirmed via `\d "AiOperationalMetrics"` in psql; verify `MetricType ASC, RecordedAt DESC` sort order
- [ ] `IX_AiOperationalMetrics_SessionId` partial index (WHERE SessionId IS NOT NULL) verified in `pg_indexes`
- [ ] `Down()` migration drops table and all three indexes cleanly without errors
- [ ] `AiOperationalMetric` entity has only `init` accessors — no `set` accessor on any property
- [ ] `EfAiOperationalMetricsWriter` (task_002) can INSERT all four metric types without EF Core mapping errors

---

## Implementation Checklist

- [ ] Create `AiOperationalMetricType` enum with 4 values: `TokenConsumption`, `Latency`, `ProviderError`, `CircuitBreakerTrip`
- [ ] Create `AiOperationalMetric` entity: `Id`, `MetricType`, `SessionId?`, `ModelVersion`, `ValueA?`, `ValueB?`, `Metadata?`, `RecordedAt` — all `init` accessors
- [ ] Create `AiOperationalMetricConfiguration`: `ValueA`/`ValueB` as `numeric(18,4)`; `MetricType` stored as int; 3 indexes (composite MetricType+RecordedAt; RecordedAt; partial SessionId IS NOT NULL)
- [ ] Add `DbSet<AiOperationalMetric>` to `ApplicationDbContext`
- [ ] Generate and review EF Core migration `AddAiOperationalMetricsTable`; verify composite index uses `MetricType ASC, RecordedAt DESC`; verify partial index SQL in `Up()`
