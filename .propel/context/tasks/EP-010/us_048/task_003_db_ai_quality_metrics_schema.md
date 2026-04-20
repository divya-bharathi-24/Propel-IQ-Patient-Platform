# Task - task_003_db_ai_quality_metrics_schema

## Requirement Reference

- **User Story:** us_048 — AI Quality Monitoring & Hallucination Rate Control
- **Story Location:** `.propel/context/tasks/EP-010/us_048/us_048.md`
- **Acceptance Criteria:**
  - AC-1 / AC-2 / AC-3: All three metric event types (agreement, hallucination, schema-validity) are persisted in a single `AiQualityMetrics` table with sufficient columns to support rolling-window queries per metric type.
  - Operational: Rolling-window queries by `MetricType + RecordedAt` complete efficiently (< 50 ms at Phase 1 volumes) using a composite index.
- **Edge Cases:**
  - INSERT-only enforcement: `UPDATE` and `DELETE` are never performed on this table (consistent with AD-7 append-only pattern for quality-critical records); no FK cascade deletes should touch this table.
  - Table can grow to millions of rows over HIPAA 7-year retention window — indexes must support efficient range scans.

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

Create the `AiQualityMetrics` PostgreSQL table via EF Core code-first migration. The table is a single polymorphic metrics store covering all three metric event types (Agreement, Hallucination, SchemaValidity) to avoid three separate narrow tables at Phase 1 scale. Nullable columns (`FieldName`, `IsAgreement`, `IsHallucination`, `IsSchemaValid`) carry type-specific data; `MetricType` discriminates the row kind.

Two indexes support the rolling-window read patterns required by `EfAiMetricsReadRepository`:
1. Composite index on `(MetricType, RecordedAt DESC)` — primary rolling-window access pattern.
2. Index on `SessionId` — for per-session metric lookups and future debugging queries.

No foreign key to `Users` is defined (metric events are written by the AI pipeline without a user context in all cases). `SessionId` is a denormalized reference to the AI extraction session (Guid stored as-is, not FK-constrained to allow async write decoupling).

---

## Dependent Tasks

- No external dependencies — foundational table for this US.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `AiQualityMetric` entity (new) | Domain | CREATE — EF Core entity with all columns |
| `AiQualityMetricConfiguration` (new) | Infrastructure | CREATE — EF Core Fluent API configuration: table name, required columns, nullable columns, indexes |
| `AddAiQualityMetricsTable` migration (new) | Infrastructure | CREATE — EF Core migration: CREATE TABLE + 2 CREATE INDEX |
| `ApplicationDbContext` (existing) | Infrastructure | MODIFY — Add `DbSet<AiQualityMetric> AiQualityMetrics` |

---

## Implementation Plan

1. **Define `AiQualityMetric` entity**:

   ```csharp
   public class AiQualityMetric
   {
       public Guid Id { get; init; }
       public Guid SessionId { get; init; }
       public string MetricType { get; init; }   // "Agreement" | "Hallucination" | "SchemaValidity"
       public string? FieldName { get; init; }   // null for SchemaValidity events
       public bool? IsAgreement { get; init; }   // Agreement events only
       public bool? IsHallucination { get; init; } // Hallucination events only
       public bool? IsSchemaValid { get; init; }   // SchemaValidity events only
       public DateTimeOffset RecordedAt { get; init; }
   }
   ```

   All properties use `init` accessor — entity is immutable after construction (INSERT-only enforcement at application layer).

2. **Configure `AiQualityMetricConfiguration`** (EF Core Fluent API):
   - Table: `"AiQualityMetrics"`
   - `Id`: PK, `ValueGeneratedNever()` (caller supplies Guid — avoids DB round-trip for INSERT performance).
   - `MetricType`: required, `HasMaxLength(32)`.
   - `FieldName`: optional, `HasMaxLength(256)`.
   - `IsAgreement`, `IsHallucination`, `IsSchemaValid`: optional (nullable bool).
   - `RecordedAt`: required, `HasColumnType("timestamptz")`.
   - **Index 1**: `HasIndex(x => new { x.MetricType, x.RecordedAt })` — composite, descending on `RecordedAt`; named `"IX_AiQualityMetrics_MetricType_RecordedAt"`.
   - **Index 2**: `HasIndex(x => x.SessionId)` — named `"IX_AiQualityMetrics_SessionId"`.

3. **Generate EF Core migration** `AddAiQualityMetricsTable`:
   - `Up()`: `CREATE TABLE AiQualityMetrics (...)` with all columns; `CREATE INDEX IX_AiQualityMetrics_MetricType_RecordedAt ON AiQualityMetrics (MetricType ASC, RecordedAt DESC)`; `CREATE INDEX IX_AiQualityMetrics_SessionId ON AiQualityMetrics (SessionId)`.
   - `Down()`: `DROP TABLE IF EXISTS AiQualityMetrics` (cascades both indexes).

4. **Add `DbSet` to `ApplicationDbContext`**:
   - `public DbSet<AiQualityMetric> AiQualityMetrics => Set<AiQualityMetric>();`

---

## Current Project State

```
Server/
  Infrastructure/
    Persistence/
      ApplicationDbContext.cs           ← EXISTS — MODIFY
      Configurations/                   ← existing EF configuration folder
      Migrations/                       ← existing migrations folder
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Domain/AiQualityMetric.cs` | Immutable entity: Id, SessionId, MetricType, FieldName?, IsAgreement?, IsHallucination?, IsSchemaValid?, RecordedAt |
| CREATE | `Server/Infrastructure/Persistence/Configurations/AiQualityMetricConfiguration.cs` | Fluent API: table name, column constraints, 2 indexes |
| CREATE | `Server/Infrastructure/Persistence/Migrations/YYYYMMDD_AddAiQualityMetricsTable.cs` | EF Core migration: CREATE TABLE + 2 indexes |
| MODIFY | `Server/Infrastructure/Persistence/ApplicationDbContext.cs` | Add `DbSet<AiQualityMetric> AiQualityMetrics` |

---

## External References

- [EF Core 9 — Code-First Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli) — `dotnet ef migrations add` and `dotnet ef database update`
- [EF Core 9 — Fluent API Index configuration](https://learn.microsoft.com/en-us/ef/core/modeling/indexes) — `HasIndex(...).IsDescending(false, true)` for composite descending index
- [PostgreSQL 16 — `timestamptz` column type](https://www.postgresql.org/docs/16/datatype-datetime.html) — `HasColumnType("timestamptz")` for UTC-aware timestamps
- [DR-011 (design.md)](../.propel/context/docs/design.md) — 7-year retention; table design must accommodate long-term growth
- [AD-7 (design.md)](../.propel/context/docs/design.md) — Append-only pattern; `init` accessors enforce immutability at application layer
- [AIR-Q01, AIR-Q03, AIR-Q04 (design.md)](../.propel/context/docs/design.md) — Data requirements driving the three MetricType event rows

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [ ] EF Core migration applies cleanly to a local PostgreSQL 16 instance
- [ ] `AiQualityMetrics` table created with all 8 columns and correct types
- [ ] `IX_AiQualityMetrics_MetricType_RecordedAt` composite index exists and is used in `EXPLAIN ANALYZE` for `MetricType = 'Agreement' ORDER BY RecordedAt DESC LIMIT 200`
- [ ] `IX_AiQualityMetrics_SessionId` index exists
- [ ] `Down()` migration drops table cleanly with no orphan indexes
- [ ] `AiQualityMetric` entity has only `init` accessors — no `set` accessor on any property

---

## Implementation Checklist

- [ ] Create `AiQualityMetric` entity: 8 columns, all `init`, immutable; `MetricType` discriminator string
- [ ] Create `AiQualityMetricConfiguration`: table name, column constraints, 2 named indexes
- [ ] Add `DbSet<AiQualityMetric>` to `ApplicationDbContext`
- [ ] Generate and review EF Core migration `AddAiQualityMetricsTable` (Up + Down)
- [ ] Verify composite index includes `RecordedAt DESC` direction for rolling-window query pattern
