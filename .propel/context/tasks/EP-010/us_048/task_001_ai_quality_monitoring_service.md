# Task - task_001_ai_quality_monitoring_service

## Requirement Reference

- **User Story:** us_048 — AI Quality Monitoring & Hallucination Rate Control
- **Story Location:** `.propel/context/tasks/EP-010/us_048/us_048.md`
- **Acceptance Criteria:**
  - AC-1: When staff confirm or reject an AI-suggested code or extracted field, an `AiAgreementMetricEvent` is emitted from the AI pipeline and persisted so the overall AI-Human Agreement Rate (AIR-Q01 ≥98%) is calculable.
  - AC-2: When any AI-generated structured JSON output is produced, the SK output filter validates it against the registered schema; outputs failing validation are rejected (not persisted), an alert is written to Serilog, and the request falls back to the manual review queue — maintaining ≥99% schema validity (AIR-Q03).
  - AC-3: When staff verify an AI-extracted field and mark it as incorrect, a hallucination event is emitted; the rolling rate is evaluated; if it exceeds 2%, a critical-level Serilog alert is raised (AIR-Q04).
  - AC-4: When the AI pipeline produces a field with confidence below 80%, the confidence gate sets `NeedsManualReview = true` and prevents auto-commit; the field is surfaced to the staff review queue (AIR-003).
- **Edge Cases:**
  - Insufficient ground truth: if fewer than 50 verified samples exist, hallucination rate computation returns `null` and the metric is displayed as "Insufficient data"; no alert is raised.
  - Agreement rate drop: if the rolling agreement rate drops below 98%, a Serilog warning-level alert is raised and a flag is set in Upstash Redis key `ai:quality:agreement_rate_below_threshold`; no automatic model rollback in Phase 1.

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

| Layer              | Technology                    | Version    |
| ------------------ | ----------------------------- | ---------- |
| AI/ML Orchestration | Microsoft Semantic Kernel    | 1.x        |
| AI/ML Provider     | OpenAI GPT-4o                 | —          |
| Backend            | ASP.NET Core Web API          | .net 10     |
| Cache              | Upstash Redis                 | Serverless |
| Logging            | Serilog                       | 4.x        |

**Note:** All code and libraries MUST be compatible with versions listed above.

---

## AI References (AI Tasks Only)

| Reference Type           | Value                                                   |
| ------------------------ | ------------------------------------------------------- |
| **AI Impact**            | Yes                                                     |
| **AIR Requirements**     | AIR-Q01, AIR-Q03, AIR-Q04, AIR-003, AIR-O01, AIR-O02   |
| **AI Pattern**           | Guardrails / Output Validation                          |
| **Prompt Template Path** | N/A (output filter, not prompt-level)                   |
| **Guardrails Config**    | `Server/AI/Guardrails/AiOutputSchemaValidator.cs`       |
| **Model Provider**       | OpenAI GPT-4o (via Semantic Kernel)                     |

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

Implement the AI quality monitoring and guardrails layer inside the Semantic Kernel pipeline. This task covers four interlocking concerns that sit between the LLM output and the application persistence layer:

1. **Schema Validation Filter (AIR-Q03)** — A Semantic Kernel `IPromptRenderFilter` / `IFunctionInvocationFilter` post-processing step that validates every structured JSON output against a registered `JsonSchema` (using `System.Text.Json.Schema`). Invalid outputs are rejected; the call falls back to manual review; a Serilog alert is written; a schema-validity failure event is emitted to the metrics pipeline.

2. **Confidence Gate (AIR-003)** — After extraction, each field's `confidence` score is evaluated. Fields below 0.80 have `NeedsManualReview = true` set and are not auto-committed to the patient record. This gate runs as part of the `ExtractionResultProcessor` that already exists from US_040, extended here.

3. **Agreement Metric Hook (AIR-Q01)** — When a staff member confirms or rejects an AI-suggested value (triggered from the US_043 confirmation API handler), a domain event `AiAgreementMetricEvent` is raised via MediatR's notification interface. This task implements the `IAiAgreementEventEmitter` called from the confirmation handler and the `IAiMetricsWriter` that persists the event.

4. **Hallucination & Alert Logic (AIR-Q04)** — When staff marks a field as incorrect (ground truth diverges from AI output), `AiHallucinationMetricEvent` is raised. `HallucinationRateEvaluator` computes the rolling rate over the most recent N verified samples (using `IAiMetricsReadRepository`); if rate > 2% and sample count ≥ 50, a Serilog `Critical` event is raised and the Redis flag `ai:quality:hallucination_rate_above_threshold` is set.

---

## Dependent Tasks

- `task_003_db_ai_quality_metrics_schema.md` (EP-010/us_048) — `AiQualityMetrics` table must exist before metric events can be persisted.
- `task_002_be_ai_metrics_api.md` (EP-010/us_048) — MediatR handlers for persisting metric events must be registered before this layer can emit them.
- US_040 `ExtractionResultProcessor` — confidence gate extends this existing processor.
- US_043 `ConfirmMedicalCodesCommandHandler` — agreement metric hook is called from this existing handler.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `AiOutputSchemaValidator` (new) | AI / Guardrails | CREATE — SK `IFunctionInvocationFilter`: post-process JSON output; validate against schema; reject on failure; emit schema-validity event |
| `ExtractionResultProcessor` (existing — US_040) | AI / Extraction | MODIFY — Add confidence gate: fields < 0.80 → `NeedsManualReview = true`; never auto-committed |
| `IAiAgreementEventEmitter` (new) | AI / Metrics | CREATE — Interface: `EmitAgreementEventAsync(sessionId, fieldName, staffDecision)` |
| `IAiMetricsWriter` (new) | AI / Metrics | CREATE — Interface: `WriteAgreementEventAsync(...)`, `WriteHallucinationEventAsync(...)`, `WriteSchemaValidityEventAsync(...)` |
| `HallucinationRateEvaluator` (new) | AI / Metrics | CREATE — Computes rolling hallucination rate from `IAiMetricsReadRepository`; raises alert + sets Redis flag if > 2% with ≥ 50 samples |
| `AgreementRateEvaluator` (new) | AI / Metrics | CREATE — Computes rolling agreement rate; raises Serilog warning + sets Redis flag `ai:quality:agreement_rate_below_threshold` if < 98% |
| `ConfirmMedicalCodesCommandHandler` (existing — US_043) | Clinical / Commands | MODIFY — Call `IAiAgreementEventEmitter.EmitAgreementEventAsync` after each confirmed/rejected code |
| `AiModuleRegistration` (existing) | DI Bootstrap | MODIFY — Register schema validator filter, IAiAgreementEventEmitter, IAiMetricsWriter, evaluators |

---

## Implementation Plan

1. **Implement `AiOutputSchemaValidator`** — SK `IFunctionInvocationFilter`:
   - On `OnFunctionInvocationAsync` post-step: extract `FunctionResult` content as string.
   - Attempt `JsonDocument.Parse(content)` — catch `JsonException` as schema fail.
   - Validate against the function's registered `JsonSchema` (stored as a metadata key on the `KernelFunction`).
   - On failure: log `Serilog.Log.Error("AI schema validation failed for {FunctionName}", ...)`, call `IAiMetricsWriter.WriteSchemaValidityEventAsync(isValid: false)`, set `context.Result = FunctionResult.Failure(...)`, and throw `AiSchemaValidationException` to trigger manual review fallback in the caller.
   - On success: call `IAiMetricsWriter.WriteSchemaValidityEventAsync(isValid: true)`.

2. **Extend `ExtractionResultProcessor`** (confidence gate, AIR-003):
   - For each `ExtractedField` in the result: if `field.Confidence < 0.80`, set `field.NeedsManualReview = true`.
   - Do NOT call `IExtractedDataRepository.CommitAsync()` for fields with `NeedsManualReview = true` — they remain in a `PendingReview` state surfaced to the staff queue.
   - Fields with `Confidence >= 0.80` are committed normally.

3. **Implement `IAiAgreementEventEmitter` / inline emitter**:
   - `EmitAgreementEventAsync(Guid sessionId, string fieldName, StaffDecision decision)`:
     - Maps `decision` to `isAgreement` bool (`Accepted | Modified → true`, `Rejected → false`).
     - Calls `IAiMetricsWriter.WriteAgreementEventAsync(sessionId, fieldName, isAgreement)`.
     - After write, calls `AgreementRateEvaluator.EvaluateAsync()`.

4. **Implement `AgreementRateEvaluator`**:
   - Calls `IAiMetricsReadRepository.GetRecentAgreementEventsAsync(windowSize: 200)`.
   - Computes `rate = agreementCount / totalCount`.
   - If `rate < 0.98` and `totalCount >= 50`: log `Serilog.Log.Warning("AI-Human Agreement Rate {Rate:P1} below 98% threshold", rate)` and `await redisDb.StringSetAsync("ai:quality:agreement_rate_below_threshold", "true", TimeSpan.FromHours(1))`.

5. **Implement `HallucinationRateEvaluator`**:
   - Calls `IAiMetricsReadRepository.GetRecentVerifiedSamplesAsync(windowSize: 200)`.
   - If `totalVerified < 50`: return (insufficient data — no alert).
   - Compute `hallucinationRate = hallucinatedCount / totalVerified`.
   - If `hallucinationRate > 0.02`: log `Serilog.Log.Fatal("Hallucination rate {Rate:P1} exceeds 2% threshold — model review required", rate)` and `await redisDb.StringSetAsync("ai:quality:hallucination_rate_above_threshold", "true", TimeSpan.FromHours(1))`.

6. **Modify `ConfirmMedicalCodesCommandHandler`** (US_043):
   - After persisting each confirmed/rejected code: call `IAiAgreementEventEmitter.EmitAgreementEventAsync(code.SessionId, code.FieldName, code.VerificationStatus)`.

7. **Register all components in `AiModuleRegistration`** — add `AiOutputSchemaValidator` as SK filter, register `IAiAgreementEventEmitter`, `IAiMetricsWriter`, evaluators as scoped services.

---

## Current Project State

```
Server/
  AI/
    Extraction/
      ExtractionResultProcessor.cs      ← EXISTS (US_040) — MODIFY (confidence gate)
    Guardrails/                         ← folder to create
    Metrics/                            ← folder to create
  Clinical/
    Commands/
      ConfirmMedicalCodesCommandHandler.cs ← EXISTS (US_043) — MODIFY (agreement hook)
  DI/
    AiModuleRegistration.cs             ← EXISTS — MODIFY
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/AI/Guardrails/AiOutputSchemaValidator.cs` | SK IFunctionInvocationFilter: validate JSON output against schema; reject + alert on failure |
| CREATE | `Server/AI/Metrics/IAiAgreementEventEmitter.cs` | Interface: EmitAgreementEventAsync |
| CREATE | `Server/AI/Metrics/IAiMetricsWriter.cs` | Interface: WriteAgreementEventAsync, WriteHallucinationEventAsync, WriteSchemaValidityEventAsync |
| CREATE | `Server/AI/Metrics/AgreementRateEvaluator.cs` | Rolling rate computation; Serilog warning + Redis flag if < 98% |
| CREATE | `Server/AI/Metrics/HallucinationRateEvaluator.cs` | Rolling rate computation; Serilog critical + Redis flag if > 2%; insufficient-data guard |
| MODIFY | `Server/AI/Extraction/ExtractionResultProcessor.cs` | Add confidence gate: fields < 0.80 → NeedsManualReview = true, not auto-committed |
| MODIFY | `Server/Clinical/Commands/ConfirmMedicalCodesCommandHandler.cs` | Call IAiAgreementEventEmitter after each code confirmation/rejection |
| MODIFY | `Server/DI/AiModuleRegistration.cs` | Register schema validator filter, emitter, writer, evaluators |

---

## External References

- [Semantic Kernel 1.x — IFunctionInvocationFilter](https://learn.microsoft.com/en-us/semantic-kernel/concepts/filters) — Post-process function results for validation and guardrails
- [System.Text.Json.Schema (.net 10)](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/schema) — `JsonSchemaExporter` for JSON schema validation in .net 10
- [Serilog 4.x — Structured Logging](https://serilog.net/) — `Log.Warning(...)`, `Log.Fatal(...)` with property capture
- [StackExchange.Redis — StringSetAsync with TTL](https://stackexchange.github.io/StackExchange.Redis/Basics.html) — Setting alert flags with 1-hour expiry
- [AIR-Q01 (design.md)](../.propel/context/docs/design.md) — ≥98% AI-Human Agreement Rate target
- [AIR-Q03 (design.md)](../.propel/context/docs/design.md) — ≥99% schema validity; invalid outputs rejected
- [AIR-Q04 (design.md)](../.propel/context/docs/design.md) — <2% hallucination rate; measured against verified ground truth
- [AIR-003 (design.md)](../.propel/context/docs/design.md) — 80% confidence floor; below threshold → manual review, not auto-committed
- [NFR-011 (design.md)](../.propel/context/docs/design.md) — ≥98% AI-Human Agreement Rate overall system requirement
- [AD-6 (design.md)](../.propel/context/docs/design.md) — Semantic Kernel as AI orchestration layer; SK filters for guardrails

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [ ] Unit tests pass (xUnit + Moq)
- [ ] `AiOutputSchemaValidator`: valid JSON → `isValid` event written, result passes through
- [ ] `AiOutputSchemaValidator`: invalid JSON → `AiSchemaValidationException` thrown; Serilog error logged; `isValid = false` event written
- [ ] Confidence gate: field with `confidence = 0.79` → `NeedsManualReview = true`; not passed to commit
- [ ] Confidence gate: field with `confidence = 0.80` → `NeedsManualReview = false`; committed normally
- [ ] `AgreementRateEvaluator`: rate 97% with 100 samples → Serilog warning logged + Redis flag set
- [ ] `AgreementRateEvaluator`: rate 97% with 30 samples → no alert (insufficient data)
- [ ] `HallucinationRateEvaluator`: rate 3% with 60 samples → Serilog Critical logged + Redis flag set
- [ ] `HallucinationRateEvaluator`: 40 verified samples → returns null rate; no alert raised

---

## Implementation Checklist

- [ ] Create `AiOutputSchemaValidator` (SK `IFunctionInvocationFilter`): validate JSON output, reject on failure, emit schema-validity metric event, log Serilog error
- [ ] Create `IAiAgreementEventEmitter` and `IAiMetricsWriter` interfaces
- [ ] Create `AgreementRateEvaluator`: rolling window ≤200 samples; Serilog warning + Redis flag if rate < 98% with ≥50 samples
- [ ] Create `HallucinationRateEvaluator`: rolling window ≤200 samples; Serilog Critical + Redis flag if rate > 2% with ≥50 samples; null guard for < 50 samples
- [ ] Modify `ExtractionResultProcessor`: add confidence gate (< 0.80 → `NeedsManualReview = true`; never auto-committed)
- [ ] Modify `ConfirmMedicalCodesCommandHandler`: call `IAiAgreementEventEmitter.EmitAgreementEventAsync` per code decision
- [ ] Register all new components in `AiModuleRegistration`
- [ ] Verify `AiOutputSchemaValidator` is registered as SK `IFunctionInvocationFilter` (not just DI service)
