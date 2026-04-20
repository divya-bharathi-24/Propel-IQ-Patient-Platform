# Task - task_001_ai_conflict_detection_service

## Requirement Reference

- **User Story:** us_044 — Data Conflict Detection, Visual Highlighting & Resolution
- **Story Location:** `.propel/context/tasks/EP-008-II/us_044/us_044.md`
- **Acceptance Criteria:**
  - AC-1: When the AI extraction pipeline detects a conflicting data element across two documents for the same patient, the `DataConflict` record created includes `fieldName`, `value1`, `sourceDocumentId1`, `value2`, `sourceDocumentId2`, `resolutionStatus = Unresolved`, and severity classification (`Critical` or `Warning`).
- **Edge Cases:**
  - New document uploaded after conflicts resolved: re-run of conflict detection must create new `DataConflict` records for newly detected conflicts while leaving previously resolved records untouched.
  - No conflicting documents: conflict detection returns an empty set; no `DataConflict` records are written.

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

| Layer                    | Technology                      | Version |
| ------------------------ | ------------------------------- | ------- |
| Backend                  | ASP.NET Core Web API            | .net 10  |
| AI/ML — Orchestration    | Microsoft Semantic Kernel       | 1.x     |
| AI/ML — Model Provider   | OpenAI API / Azure OpenAI       | GPT-4o  |
| AI/ML — Embeddings       | text-embedding-3-small (OpenAI) | Latest  |
| Vector Store             | pgvector (PostgreSQL extension) | 0.7+    |
| ORM                      | Entity Framework Core           | 9.x     |
| Database                 | PostgreSQL                      | 16+     |
| Logging                  | Serilog                         | 4.x     |

**Note:** All code and libraries MUST be compatible with versions listed above.

---

## AI References (AI Tasks Only)

| Reference Type           | Value                                                                        |
| ------------------------ | ---------------------------------------------------------------------------- |
| **AI Impact**            | Yes                                                                          |
| **AIR Requirements**     | AIR-001, AIR-003, AIR-Q03, AIR-R02, AIR-R03, AIR-S02, AIR-S03, AIR-O01, AIR-O02 |
| **AI Pattern**           | RAG                                                                          |
| **Prompt Template Path** | `prompts/conflict-detection/`                                                |
| **Guardrails Config**    | `config/ai-guardrails.json` (schema validation middleware + SK filters)      |
| **Model Provider**       | OpenAI API / Azure OpenAI (GPT-4o)                                           |

> **AI Impact:** Yes — task involves RAG-based semantic comparison of extracted clinical data fields across documents to detect contradictions, using Semantic Kernel with pgvector retrieval.

### CRITICAL: AI Implementation Requirements

**IF AI Impact = Yes:**
- **MUST** reference prompt templates from `prompts/conflict-detection/` during implementation
- **MUST** apply ACL filter before retrieval: only chunks from patient's authorized documents (AIR-S02)
- **MUST** enforce token budget of 8,000 tokens per request (AIR-O01)
- **MUST** validate output JSON schema (≥99% validity, AIR-Q03); reject malformed responses
- **MUST** fall back to flagging field for manual staff review when confidence < 80% (AIR-003)
- **MUST** log all prompts and responses via Serilog with PII redacted for HIPAA audit retention (AIR-S03)
- **MUST** handle model failures with circuit breaker (3 failures / 5 min → open, AIR-O02)

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

Implement the Semantic Kernel RAG-based conflict detection service that runs as part of the clinical data aggregation pipeline. After extracting and de-duplicating clinical fields (US_041), this service compares paired values for the same field type (e.g., medication dosage, allergy name) across different source documents using semantic similarity via pgvector. Where contradictions are found, it classifies the conflict as `Critical` (medications, allergies, diagnoses) or `Warning` (non-clinical ancillary data) and persists a `DataConflict` record for each detected conflict. The service is invoked within the existing 360-degree aggregation flow and is idempotent: re-runs on new document upload create only new conflict records without overwriting resolved ones.

---

## Dependent Tasks

- `task_004_db_data_conflict_schema.md` (EP-008-II/us_044) — `DataConflict` EF entity and migration MUST exist before this service can persist records.
- `task_003_ai_deduplication_service.md` (EP-008-I/us_041) — Aggregated and de-duplicated `ExtractedData` records MUST exist as the input data source for conflict comparison.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `ConflictDetectionPlugin` (new) | AI Module | CREATE — Semantic Kernel plugin with `DetectConflictsAsync` kernel function |
| `ConflictDetectionOrchestrator` (new) | AI Module | CREATE — Orchestrates RAG retrieval → conflict prompt → classification → persist |
| `ConflictSeverityClassifier` (new) | AI Module | CREATE — Determines `Critical` vs `Warning` based on clinical field type rules |
| `ConflictDetectionSchemaValidator` (new) | AI Module | CREATE — Validates AI output JSON against `ConflictDetectionResult` schema (AIR-Q03) |
| `IDataConflictRepository` (new) | Infrastructure | CREATE — Interface for inserting and querying `DataConflict` records |
| `DataConflictRepository` (new) | Infrastructure | CREATE — EF Core implementation; idempotent insert (skip if matching unresolved record exists) |
| `AiAuditLogger` (existing) | Infrastructure | MODIFY — Extend to log conflict detection prompt/response invocations |
| `prompts/conflict-detection/detect-conflicts.yaml` (new) | Prompts | CREATE — Versioned prompt comparing two extracted field values and classifying conflict |
| `MedicalCodingOrchestrator` (existing) | AI Module | MODIFY — Chain conflict detection after de-duplication within the aggregation pipeline call |

---

## Implementation Plan

1. **Define output DTO** — Create `ConflictDetectionResult`: `{ fieldName, value1, sourceDocumentId1, value2, sourceDocumentId2, isConflict, severity? }`. Used as the deserialization target from the GPT-4o response.

2. **Author prompt template** — Create `prompts/conflict-detection/detect-conflicts.yaml`: system prompt instructs GPT-4o to compare two extracted values for the same clinical field, determine whether they contradict each other semantically (not just lexically), and return a JSON `ConflictDetectionResult`. Include anti-hallucination constraint: base judgment solely on provided values, not external medical knowledge.

3. **Implement `ConflictSeverityClassifier`** — Static rule mapping field types to severity:
   - `Critical`: `Medication`, `MedicationDosage`, `Allergy`, `Diagnosis`, `DiagnosisDate`
   - `Warning`: all other field types (vitals, ancillary demographics)

4. **Implement `ConflictDetectionPlugin`** — `[KernelFunction]`-annotated method `DetectConflictsAsync(string fieldName, string value1, string sourceDoc1Name, string value2, string sourceDoc2Name)`: sends the conflict detection prompt, deserialises `ConflictDetectionResult`, validates schema, returns result.

5. **Implement `ConflictDetectionOrchestrator`** — Service that:
   - Accepts the aggregated `ExtractedData` grouped by field type and patient ID.
   - For each field type with ≥ 2 distinct values from different source documents: invokes `DetectConflictsAsync` on each value pair.
   - For detected conflicts: applies `ConflictSeverityClassifier` to assign severity.
   - Calls `IDataConflictRepository.InsertIfNewAsync` for each new conflict (idempotent — skips if an Unresolved record with identical `fieldName + sourceDocumentId1 + sourceDocumentId2` already exists).
   - Wraps tool calls in the Polly circuit breaker (AIR-O02); on open circuit, logs at ERROR level and sets field as requiring manual staff review.
   - Enforces 8,000-token budget per prompt invocation (AIR-O01).

6. **Implement `IDataConflictRepository` and `DataConflictRepository`** — `InsertIfNewAsync`: checks for existing Unresolved record with same patient/field/source pair; inserts only if not found. `GetUnresolvedByPatientAsync(Guid patientId)`: returns all `Unresolved` conflicts for a patient. `GetCriticalUnresolvedCountAsync(Guid patientId)`: returns count used by the verification gate (US_041 AC-4).

7. **Extend `AiAuditLogger`** — Add `LogConflictDetectionInvocationAsync` with structured fields: `patientId` (reference only), `fieldName`, `isConflict`, `severity?`, `tokenCount`, `schemaValid`, `timestamp`. No PHI in log payload.

8. **Chain into aggregation pipeline** — Modify the existing `360-degree aggregation` orchestration to invoke `ConflictDetectionOrchestrator.DetectConflictsAsync(aggregatedData)` after de-duplication completes, before returning the 360-view response.

---

## Current Project State

```
Server/
  AI/
    Plugins/
      MedicalCodingPlugin.cs           ← existing plugin pattern to follow
      DocumentExtractionPlugin.cs      ← existing plugin pattern
    Orchestrators/
      MedicalCodingOrchestrator.cs     ← existing orchestrator pattern
    Validators/
      MedicalCodeSchemaValidator.cs    ← existing schema validator pattern
    Audit/
      AiAuditLogger.cs                 ← existing audit logger to extend
  prompts/
    medical-coding/
      icd10-suggestion.yaml            ← existing prompt template pattern
  Infrastructure/
    Persistence/
      Repositories/                    ← existing repository pattern folder
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/AI/Plugins/ConflictDetectionPlugin.cs` | SK plugin: `DetectConflictsAsync` kernel function |
| CREATE | `Server/AI/Orchestrators/ConflictDetectionOrchestrator.cs` | Orchestrator: field-pair iteration, severity classification, idempotent persist |
| CREATE | `Server/AI/Classifiers/ConflictSeverityClassifier.cs` | Static rule: field type → Critical/Warning |
| CREATE | `Server/AI/Validators/ConflictDetectionSchemaValidator.cs` | JSON schema validator for `ConflictDetectionResult` |
| CREATE | `Server/Shared/Contracts/ConflictDetectionResult.cs` | DTO: fieldName, value1, sourceDocumentId1, value2, sourceDocumentId2, isConflict, severity? |
| CREATE | `Server/Application/Clinical/Interfaces/IDataConflictRepository.cs` | Repository interface: InsertIfNewAsync, GetUnresolvedByPatientAsync, GetCriticalUnresolvedCountAsync |
| CREATE | `Server/Infrastructure/Persistence/Repositories/DataConflictRepository.cs` | EF Core implementation with idempotent insert |
| CREATE | `Server/prompts/conflict-detection/detect-conflicts.yaml` | Versioned conflict detection prompt template |
| MODIFY | `Server/AI/Audit/AiAuditLogger.cs` | Add `LogConflictDetectionInvocationAsync` |
| MODIFY | `Server/AI/Orchestrators/AggregationOrchestrator.cs` | Chain `ConflictDetectionOrchestrator` after de-duplication step |
| MODIFY | `Server/AI/DependencyInjection/AiModuleRegistration.cs` | Register plugin, orchestrator, classifier, validator, repository |

---

## External References

- [Semantic Kernel .NET RAG Pattern](https://learn.microsoft.com/en-us/semantic-kernel/concepts/plugins/using-data-retrieval-functions-for-rag?pivots=programming-language-csharp) — Plugin and retrieval function patterns
- [Semantic Kernel Prompt Templates (YAML)](https://learn.microsoft.com/en-us/semantic-kernel/concepts/prompts/prompt-template-syntax) — YAML authoring with variable substitution
- [Polly v8 Circuit Breaker](https://www.pollydocs.org/strategies/circuit-breaker.html) — `CircuitBreakerStrategyOptions` for AIR-O02
- [pgvector cosine similarity (0.7 threshold)](https://github.com/pgvector/pgvector) — AIR-R02: top-5 chunks, ≥0.7 cosine similarity
- [AIR-001, AIR-Q03, AIR-O01, AIR-O02 (design.md)](../.propel/context/docs/design.md) — AI functional/operational requirements
- [FR-054 (spec.md)](../.propel/context/docs/spec.md) — AI conflict detection requirement
- [DR-008 DataConflict entity (design.md)](../.propel/context/docs/design.md) — Canonical attribute list

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [ ] Unit tests pass (xUnit + Moq — orchestrator, classifier, repository)
- [ ] Integration tests pass (orchestrator → SK plugin mock → EF Core in-memory)
- [ ] **[AI Tasks]** Prompt template validated with test pairs: same-medication/different-dosage (should detect conflict), same-value duplicate (should not detect conflict)
- [ ] **[AI Tasks]** Guardrails tested: malformed AI response rejected by schema validator; ACL filter prevents cross-patient document access
- [ ] **[AI Tasks]** Idempotency tested: re-running detection on same document pair does not create duplicate `DataConflict` records
- [ ] **[AI Tasks]** Severity classification tested: Medication/Allergy/Diagnosis fields → `Critical`; ancillary fields → `Warning`
- [ ] **[AI Tasks]** Circuit breaker activates after 3 failures within 5-minute window; ERROR log emitted
- [ ] **[AI Tasks]** Audit log captures all invocations with `patientId`, `fieldName`, `isConflict`, no PHI in payload
- [ ] **[AI Tasks]** Token budget of 8,000 tokens enforced per prompt call (AIR-O01)
- [ ] New document upload re-run creates new conflict records; previously resolved records are unaffected

---

## Implementation Checklist

- [ ] Create `ConflictDetectionResult` DTO contract
- [ ] Author `prompts/conflict-detection/detect-conflicts.yaml` (system prompt with semantic contradiction instruction + anti-hallucination constraint)
- [ ] Implement `ConflictSeverityClassifier` static rule: Medication/Allergy/Diagnosis → Critical; others → Warning
- [ ] Implement `ConflictDetectionPlugin` with `DetectConflictsAsync` kernel function and 8,000-token budget enforcement
- [ ] Implement `ConflictDetectionSchemaValidator` for `ConflictDetectionResult` (AIR-Q03)
- [ ] Implement `IDataConflictRepository` interface and `DataConflictRepository` EF Core implementation (idempotent insert, unresolved query, critical count query)
- [ ] Implement `ConflictDetectionOrchestrator`: field-pair iteration → plugin call → severity classify → idempotent persist → circuit breaker wrap
- [ ] Extend `AiAuditLogger.LogConflictDetectionInvocationAsync` with structured fields; verify no PHI in payload
- [ ] Chain `ConflictDetectionOrchestrator` into aggregation pipeline after de-duplication step
- [ ] Register all new components in `AiModuleRegistration`
- [ ] **[AI Tasks - MANDATORY]** Reference prompt templates from `prompts/conflict-detection/` during implementation
- [ ] **[AI Tasks - MANDATORY]** Implement and test guardrails (schema validation, ACL filter, PHI redaction) before marking complete
- [ ] **[AI Tasks - MANDATORY]** Verify AIR-001, AIR-003, AIR-Q03, AIR-O01, AIR-O02, AIR-S02, AIR-S03 requirements are met
