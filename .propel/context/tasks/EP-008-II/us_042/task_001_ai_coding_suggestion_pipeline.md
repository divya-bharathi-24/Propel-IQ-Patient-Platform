# Task - task_001_ai_coding_suggestion_pipeline

## Requirement Reference

- **User Story:** us_042 — ICD-10 & CPT Code AI Suggestion Engine
- **Story Location:** `.propel/context/tasks/EP-008-II/us_042/us_042.md`
- **Acceptance Criteria:**
  - AC-1: When the medical coding request is triggered on a patient with a verified or processing 360-degree view, the Semantic Kernel tool-calling pipeline queries the ICD-10 code library and returns suggested codes with `code`, `description`, `confidence`, and `sourceDocumentId`.
  - AC-2: After ICD-10 analysis completes, the CPT procedure analysis runs and returns CPT code suggestions with `codeType = CPT`, `confidence`, `description`, and mapped evidence from clinical documentation.
  - AC-3: All AI output conforms to the structured JSON schema (≥99% schema validity per AIR-Q03); no hallucinated codes outside ICD-10 or CPT standard libraries are included.
  - AC-4: Codes with confidence < 80% are returned with a `lowConfidence = true` flag and highlighted supporting evidence for staff scrutiny.
- **Edge Cases:**
  - Patient has no clinical documents → pipeline returns empty suggestion set with `message: "No clinical data available for code analysis — upload documents first"`.
  - ICD-10/CPT code library lookup fails → circuit breaker activates (AIR-O02); tool is retried once; if second attempt fails, exception propagates to BE layer to trigger manual entry fallback notification.

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
| Backend                  | ASP.NET Core Web API            | .NET 9  |
| AI/ML — Orchestration    | Microsoft Semantic Kernel       | 1.x     |
| AI/ML — Model Provider   | OpenAI API / Azure OpenAI       | GPT-4o  |
| AI/ML — Embeddings       | text-embedding-3-small (OpenAI) | Latest  |
| Vector Store             | pgvector (PostgreSQL extension) | 0.7+    |
| ORM                      | Entity Framework Core           | 9.x     |
| Backend Messaging        | MediatR                         | 12.x    |
| Backend Validation       | FluentValidation                | 11.x    |
| Database                 | PostgreSQL                      | 16+     |
| Logging                  | Serilog                         | 4.x     |

**Note:** All code and libraries MUST be compatible with versions listed above.

---

## AI References (AI Tasks Only)

| Reference Type          | Value                                                               |
| ----------------------- | ------------------------------------------------------------------- |
| **AI Impact**           | Yes                                                                 |
| **AIR Requirements**    | AIR-005, AIR-006, AIR-Q03, AIR-O01, AIR-O02, AIR-S03, AIR-R02     |
| **AI Pattern**          | Tool Calling                                                        |
| **Prompt Template Path**| `prompts/medical-coding/`                                           |
| **Guardrails Config**   | `config/ai-guardrails.json` (schema validation middleware + SK filters) |
| **Model Provider**      | OpenAI API / Azure OpenAI (GPT-4o)                                  |

> **AI Impact:** Yes — task involves LLM tool-calling orchestration via Semantic Kernel for ICD-10/CPT extraction from aggregated patient data.

### CRITICAL: AI Implementation Requirements

**IF AI Impact = Yes:**
- **MUST** reference prompt templates from `prompts/medical-coding/` during implementation
- **MUST** implement input guardrails (PII redaction per AIR-S01 before sending to OpenAI)
- **MUST** enforce token budget of 8,000 tokens per request (AIR-O01)
- **MUST** implement output JSON schema validation (≥99% validity, AIR-Q03)
- **MUST** implement fallback logic when confidence < 80% (flag `lowConfidence = true`, AIR-003)
- **MUST** log all prompts and responses via Serilog with PII redacted for 7-year HIPAA audit retention (AIR-S03)
- **MUST** handle model failures: timeout, rate limit, HTTP 5xx → circuit breaker after 3 failures in 5 minutes (AIR-O02)

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

Implement the AI module's Semantic Kernel tool-calling pipeline that analyzes a patient's aggregated 360-degree clinical data and produces structured ICD-10 diagnostic code and CPT procedure code suggestions. The pipeline runs two sequential tool calls (ICD-10 → CPT), validates the output schema, flags low-confidence codes, enforces the 8,000-token budget, applies a circuit breaker for OpenAI failures, and persists an audit log entry for every invocation. The pipeline is consumed by the BE Medical Coding API (task_002) and has no UI surface of its own.

---

## Dependent Tasks

- `task_002_be_360_aggregation_api.md` (EP-008-I/us_041) — 360-degree aggregation service MUST be completed and the `AggregatedPatientData` contract established before this pipeline can source its input.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `MedicalCodingPlugin` (new) | AI Module | CREATE — Semantic Kernel plugin with `SuggestIcd10CodesAsync` and `SuggestCptCodesAsync` kernel functions |
| `MedicalCodingOrchestrator` (new) | AI Module | CREATE — Orchestrates sequential tool calls, applies circuit breaker, aggregates results |
| `MedicalCodeSchemaValidator` (new) | AI Module | CREATE — Validates AI output JSON against `MedicalCodeSuggestion` schema; enforces AIR-Q03 |
| `AiAuditLogger` (existing) | Shared / Infrastructure | MODIFY — Extend to log medical coding prompt/response pairs with patient context reference |
| `prompts/medical-coding/icd10-suggestion.yaml` (new) | Prompts | CREATE — Versioned prompt template for ICD-10 tool call |
| `prompts/medical-coding/cpt-suggestion.yaml` (new) | Prompts | CREATE — Versioned prompt template for CPT tool call |
| `MedicalCodeSuggestionDto` (new) | Shared Contracts | CREATE — Shared DTO: `{ code, codeType, description, confidence, sourceDocumentId, lowConfidence }` |
| `CircuitBreakerPolicy` (existing) | AI Module | MODIFY — Register medical-coding circuit breaker (3 failures / 5 min window, per AIR-O02) |

---

## Implementation Plan

1. **Define the output DTO contract** — Create `MedicalCodeSuggestionDto` with fields: `code` (string), `codeType` (enum: ICD10 | CPT), `description` (string), `confidence` (decimal 0–1), `sourceDocumentId` (Guid), `lowConfidence` (bool, computed: `confidence < 0.80`).

2. **Author prompt templates** — Create `prompts/medical-coding/icd10-suggestion.yaml` with a structured system prompt instructing GPT-4o to act as a medical coding assistant, reference only the provided aggregated diagnosis data, return JSON conforming to `MedicalCodeSuggestionDto[]`, and include only valid ICD-10 codes. Create `prompts/medical-coding/cpt-suggestion.yaml` with equivalent instruction for CPT codes.

3. **Implement `MedicalCodingPlugin`** — Register as a Semantic Kernel plugin with two `[KernelFunction]`-annotated methods:
   - `SuggestIcd10CodesAsync(string aggregatedDiagnosticData, Guid patientId)` → returns `List<MedicalCodeSuggestionDto>`
   - `SuggestCptCodesAsync(string aggregatedProcedureData, Guid patientId)` → returns `List<MedicalCodeSuggestionDto>`
   - Each method invokes the corresponding prompt template, enforces token budget (AIR-O01: max 8,000 tokens), and deserialises the GPT-4o response.

4. **Implement `MedicalCodeSchemaValidator`** — After each tool call response, validate the returned JSON:
   - Check all required fields are present and correctly typed.
   - Reject (and log) any code not present in the in-memory ICD-10 / CPT reference lookup (anti-hallucination guard).
   - Track schema validity rate; log each violation via Serilog structured event `{MedicalCodeSchemaViolation}`.

5. **Implement `MedicalCodingOrchestrator`** — Service class that:
   - Accepts `AggregatedPatientData` input from the BE layer.
   - Guards the empty-data edge case: if no clinical documents exist, return `MedicalCodingSuggestionResult { Suggestions = [], Message = "No clinical data available for code analysis — upload documents first" }`.
   - Calls `SuggestIcd10CodesAsync` first, then `SuggestCptCodesAsync` sequentially (ICD-10 evidence informs CPT context).
   - Merges both suggestion lists into a single result object, setting `lowConfidence = true` for any code with `confidence < 0.80`.
   - Wraps both tool calls within the registered circuit breaker policy (AIR-O02): retries once on failure; after second failure, throws `MedicalCodingUnavailableException` for the BE layer to handle.

6. **Extend `AiAuditLogger`** — Log each invocation with:
   - `patientId` (reference only, no PHI payload in log body — redact PII per AIR-S01/AIR-S03)
   - `toolName` (ICD10 | CPT)
   - `tokenCount` (total prompt + completion tokens)
   - `schemaValid` (bool)
   - `suggestionCount` (int)
   - `timestamp` (UTC)

7. **Register circuit breaker policy** — Configure Polly circuit breaker in DI registration: 3 consecutive exceptions → open for 60 seconds; exponential back-off on half-open retry; alert logged at ERROR level.

8. **Register plugin with Semantic Kernel kernel builder** — Add `MedicalCodingPlugin` in the AI module's DI bootstrap.

---

## Current Project State

```
Server/
  AI/
    Plugins/
      DocumentExtractionPlugin.cs      ← existing pattern to follow
    Orchestrators/
      ExtractionOrchestrator.cs        ← existing orchestration pattern
    Validators/
      ExtractionSchemaValidator.cs     ← existing schema validator pattern
    Audit/
      AiAuditLogger.cs                 ← existing audit logger to extend
  prompts/
    extraction/
      document-extraction.yaml         ← existing prompt template pattern
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/AI/Plugins/MedicalCodingPlugin.cs` | Semantic Kernel plugin with ICD-10 and CPT `[KernelFunction]` tool methods |
| CREATE | `Server/AI/Orchestrators/MedicalCodingOrchestrator.cs` | Service orchestrating sequential ICD-10 → CPT tool calls with circuit breaker |
| CREATE | `Server/AI/Validators/MedicalCodeSchemaValidator.cs` | JSON output schema validator enforcing AIR-Q03 |
| CREATE | `Server/Shared/Contracts/MedicalCodeSuggestionDto.cs` | Shared DTO: code, codeType, description, confidence, sourceDocumentId, lowConfidence |
| CREATE | `Server/Shared/Contracts/MedicalCodingSuggestionResult.cs` | Wrapper: `List<MedicalCodeSuggestionDto> Suggestions`, `string? Message` |
| CREATE | `Server/Shared/Exceptions/MedicalCodingUnavailableException.cs` | Thrown when circuit breaker is open after 2 failed retries |
| CREATE | `Server/prompts/medical-coding/icd10-suggestion.yaml` | Versioned GPT-4o prompt template for ICD-10 tool call |
| CREATE | `Server/prompts/medical-coding/cpt-suggestion.yaml` | Versioned GPT-4o prompt template for CPT tool call |
| MODIFY | `Server/AI/Audit/AiAuditLogger.cs` | Add `LogMedicalCodingInvocationAsync` method for structured audit events |
| MODIFY | `Server/AI/DependencyInjection/AiModuleRegistration.cs` | Register `MedicalCodingPlugin`, `MedicalCodingOrchestrator`, circuit breaker policy |

---

## External References

- [Semantic Kernel .NET Tool Calling Docs](https://learn.microsoft.com/en-us/semantic-kernel/concepts/plugins/using-data-retrieval-functions-for-rag?pivots=programming-language-csharp) — `[KernelFunction]` attribute and plugin registration pattern
- [Semantic Kernel Prompt Templates (YAML)](https://learn.microsoft.com/en-us/semantic-kernel/concepts/prompts/prompt-template-syntax) — YAML prompt authoring with variable substitution
- [Polly Circuit Breaker (.NET 8/9)](https://www.pollydocs.org/strategies/circuit-breaker.html) — `CircuitBreakerStrategyOptions` configuration for Polly v8
- [OpenAI Structured Outputs / JSON Mode (GPT-4o)](https://platform.openai.com/docs/guides/structured-outputs) — Enforcing JSON schema on GPT-4o responses
- [ICD-10-CM Code Set (CMS)](https://www.cms.gov/medicare/coding-billing/icd-10-codes) — Authoritative ICD-10 code lookup source
- [CPT Code Set (AMA)](https://www.ama-assn.org/practice-management/cpt/cpt-overview-and-code-approval) — Authoritative CPT code reference
- [AIR-O01 Token Budget: 8,000 tokens/request (design.md)](../.propel/context/docs/design.md)
- [AIR-O02 Circuit Breaker: 3 failures / 5 min (design.md)](../.propel/context/docs/design.md)
- [AIR-Q03 Schema Validity ≥ 99% (design.md)](../.propel/context/docs/design.md)

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for applicable .NET build and run commands.

---

## Implementation Validation Strategy

- [ ] Unit tests pass (xUnit + Moq)
- [ ] Integration tests pass (clinical module → AI module contract)
- [ ] **[AI Tasks]** Prompt templates (`icd10-suggestion.yaml`, `cpt-suggestion.yaml`) validated with test inputs covering: normal patient data, empty data, high-volume data
- [ ] **[AI Tasks]** Guardrails tested: PII redaction verified, hallucinated codes rejected by schema validator
- [ ] **[AI Tasks]** Fallback logic tested: empty clinical data → empty set with message; library lookup failure → circuit breaker activates → `MedicalCodingUnavailableException` thrown
- [ ] **[AI Tasks]** Token budget enforcement verified: requests with >8,000 tokens are truncated or rejected before dispatch
- [ ] **[AI Tasks]** Audit logging verified: all invocations logged with patientId reference, no PHI in log body, schemaValid and tokenCount fields populated
- [ ] Schema validity rate ≥ 99% confirmed across sample test invocations (AIR-Q03)
- [ ] Circuit breaker opens after 3 consecutive failures within 5-minute window (AIR-O02)

---

## Implementation Checklist

- [ ] Create `MedicalCodeSuggestionDto` and `MedicalCodingSuggestionResult` shared contracts
- [ ] Create `MedicalCodingUnavailableException` for circuit breaker open-state signalling
- [ ] Author `prompts/medical-coding/icd10-suggestion.yaml` with system + user turn, JSON output schema instruction, and anti-hallucination constraint referencing only provided clinical data
- [ ] Author `prompts/medical-coding/cpt-suggestion.yaml` following same structure for CPT codes
- [ ] Implement `MedicalCodingPlugin` with `SuggestIcd10CodesAsync` and `SuggestCptCodesAsync` kernel functions; enforce 8,000-token budget per call (AIR-O01)
- [ ] Implement `MedicalCodeSchemaValidator` with required-field presence check, field-type validation, and code-library membership check (anti-hallucination)
- [ ] Implement `MedicalCodingOrchestrator`: empty-data guard → ICD-10 tool call → CPT tool call → merge & flag low confidence → return result
- [ ] Wrap tool calls in Polly circuit breaker (retry once, open after 3 failures / 5 min, ERROR log on open)
- [ ] Extend `AiAuditLogger.LogMedicalCodingInvocationAsync` with structured fields; confirm no PHI in log payload
- [ ] Register `MedicalCodingPlugin`, `MedicalCodingOrchestrator`, and circuit breaker policy in `AiModuleRegistration`
- [ ] **[AI Tasks - MANDATORY]** Reference prompt templates from `prompts/medical-coding/` during implementation
- [ ] **[AI Tasks - MANDATORY]** Implement and test guardrails (schema validation, PII redaction, code library membership) before marking task complete
- [ ] **[AI Tasks - MANDATORY]** Verify AIR-005, AIR-006, AIR-Q03, AIR-O01, AIR-O02, AIR-S03 requirements are all met
