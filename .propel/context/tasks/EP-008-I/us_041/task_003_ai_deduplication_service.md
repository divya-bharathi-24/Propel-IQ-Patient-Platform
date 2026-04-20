# Task - task_003_ai_deduplication_service

## Requirement Reference

- **User Story:** us_041 — 360-Degree Patient View Aggregation & Staff Verification
- **Story Location:** `.propel/context/tasks/EP-008-I/us_041/us_041.md`
- **Acceptance Criteria:**
  - AC-1: Duplicate entries for the same clinical field across multiple documents are collapsed into a single canonical entry — de-duplication is driven by semantic similarity (AIR-R02: cosine similarity ≥ 0.7)
  - AC-2: Each canonical entry retains full source citations from all contributing documents (AIR-002); fields with `confidence < 0.80` are flagged for priority staff review (AIR-003)
- **Edge Cases:**
  - >10 documents: de-duplication pipeline still completes; response flag `exceedsSlaThreshold = true` informs the BE layer
  - Extraction failure on one document: de-duplication operates only over records with `processingStatus = Completed`; failed document chunks are excluded

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

| Layer              | Technology                              | Version |
| ------------------ | --------------------------------------- | ------- |
| Backend            | ASP.NET Core Web API                    | .NET 9  |
| AI/ML — Orchestration | Microsoft Semantic Kernel            | 1.x     |
| AI/ML — Model Provider | OpenAI API / Azure OpenAI          | GPT-4o  |
| AI/ML — Embeddings | text-embedding-3-small (OpenAI)         | Latest  |
| Vector Store       | pgvector (PostgreSQL extension)         | 0.7+    |
| ORM                | Entity Framework Core                   | 9.x     |
| Database           | PostgreSQL                              | 16+     |
| Testing — Unit     | xUnit                                   | —       |
| Mobile             | N/A                                     | N/A     |

> All code and libraries MUST be compatible with versions above.

---

## AI References (AI Tasks Only)

| Reference Type           | Value |
| ------------------------ | ----- |
| **AI Impact**            | Yes   |
| **AIR Requirements**     | AIR-002, AIR-003, AIR-R02, AIR-R03, AIR-S01, AIR-S03, AIR-O01, AIR-O02, AIR-O04 |
| **AI Pattern**           | RAG (retrieval-augmented, pgvector cosine similarity, semantic re-ranking) |
| **Prompt Template Path** | `Server/src/Infrastructure/AI/Prompts/deduplication/` |
| **Guardrails Config**    | `Server/src/Infrastructure/AI/Guardrails/deduplication-guardrails.json` |
| **Model Provider**       | OpenAI / Azure OpenAI (GPT-4o) |

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

Implement the AI-powered semantic de-duplication service that runs as part of the clinical data processing pipeline (triggered after US_040 extraction completes). The service uses Microsoft Semantic Kernel 1.x with pgvector cosine similarity to identify semantically equivalent clinical data elements extracted from different documents for the same patient, then collapses them into canonical entries stored in `ExtractedData`.

**Trust-First principle**: the service never silently discards data — it marks lower-priority entries as `isDuplicate = true` and links them to the canonical entry, preserving all source citations. Staff can review collapsed entries in the UI (task_001).

**Safety requirements enforced here:**
- **AIR-S01**: Patient identifiers (name, DOB, insurance ID) are stripped/masked before transmitting to OpenAI API (PII redaction applied at prompt construction time)
- **AIR-S03**: All prompts and responses are written to `AuditLog` with `entityType = AIPromptLog` before truncation (7-year retention)
- **AIR-O01**: Token budget capped at 8,000 tokens per request; chunked processing used for large field sets
- **AIR-O02**: Semantic Kernel circuit breaker — 3 consecutive failures within 5 minutes triggers fallback (all entries kept as-is, no de-duplication, `deduplicationStatus = FallbackManual`)

---

## Dependent Tasks

- **EP-008-I/us_040** (AI Extraction Pipeline) — `ExtractedData` records with embeddings must exist in pgvector before de-duplication can query cosine similarity
- **EP-001/us_009** — pgvector extension must be active with `ExtractedDataEmbeddings` table

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `PatientDeduplicationService` (Semantic Kernel orchestrated) | `Server/src/Infrastructure/AI/Services/PatientDeduplicationService.cs` |
| CREATE | `IPatientDeduplicationService` interface | `Server/src/Application/Clinical/Interfaces/IPatientDeduplicationService.cs` |
| CREATE | `DeduplicatePatientDataCommand` + handler (MediatR) | `Server/src/Application/Clinical/Commands/DeduplicatePatientData/` |
| CREATE | Prompt templates: `deduplication-system.txt`, `deduplication-user.txt` | `Server/src/Infrastructure/AI/Prompts/deduplication/` |
| CREATE | `deduplication-guardrails.json` (content filter config) | `Server/src/Infrastructure/AI/Guardrails/` |
| MODIFY | `ExtractedData` entity | Add `IsCanonical: bool`, `CanonicalGroupId: Guid?`, `DeduplicationStatus` (`Canonical/Duplicate/Unprocessed/FallbackManual`) |
| MODIFY | `ExtractedDataConfiguration` (EF Fluent API) | Map new fields |

---

## Implementation Plan

1. **Semantic similarity query** (pgvector cosine) — AIR-R02:
   ```csharp
   // Retrieve all ExtractedData embeddings for patient grouped by dataType + fieldName
   // Use pgvector cosine_distance (<=>): retrieve top-5 most similar per field
   var similarPairs = await _dbContext.ExtractedDataEmbeddings
       .FromSqlRaw("""
           SELECT a.id AS id1, b.id AS id2,
                  1 - (a.embedding <=> b.embedding) AS similarity
           FROM   extracted_data_embeddings a
           JOIN   extracted_data_embeddings b ON a.data_type = b.data_type
                  AND a.patient_id = b.patient_id
                  AND a.id < b.id
           WHERE  a.patient_id = {0}
             AND  1 - (a.embedding <=> b.embedding) >= 0.7
       """, patientId)
       .ToListAsync(ct);
   ```
   - Note: parameterised via EF Core `FromSqlRaw` with positional placeholder — safe from SQL injection (OWASP A03)

2. **Canonical selection** — within each similarity cluster:
   - Choose the entry with the highest `confidence` as `IsCanonical = true`
   - Mark remaining entries as `IsCanonical = false`, `DeduplicationStatus = Duplicate`, set `CanonicalGroupId` to the canonical entry's id

3. **GPT-4o semantic de-duplication confirmation** (Semantic Kernel, AIR-O01: ≤ 8,000 tokens):
   - For ambiguous clusters (similarity 0.70–0.85), use GPT-4o to confirm whether two field values represent the same clinical fact
   - **PII redaction** (AIR-S01): replace `patient.name`, `patient.dob`, `patient.insuranceId` with `[REDACTED]` before constructing prompt
   - Prompt template: `deduplication-system.txt` (role + constraints) + `deduplication-user.txt` (field pair JSON)
   - Validate JSON response schema; reject malformed outputs (guardrails)
   - **Audit log** (AIR-S03): write `AuditLog { entityType = "AIPromptLog", details = { prompt_hash, response_hash, tokens_used } }` before processing response

4. **Confidence flagging** (AIR-003):
   - After selecting canonical entries, any `canonical.confidence < 0.80` has `IsLowConfidence = true` (stored flag, read by BE aggregation)

5. **Circuit breaker** (AIR-O02 via Semantic Kernel `ResiliencePipeline`):
   ```csharp
   // Configured in DI: 3 consecutive OpenAI failures in 5-min window → open circuit
   // On open circuit: skip GPT-4o step, apply similarity-only de-dup, set DeduplicationStatus = FallbackManual
   ```
   Document: `"Fallback: Similarity-only de-dup applied (GPT-4o circuit breaker open)"`

6. **Token budget enforcement** (AIR-O01):
   - Count tokens before sending; if field-pair batch exceeds 7,800 tokens, split into sub-batches
   - Log token consumption per request to `AuditLog` (AIR-O04)

7. **`DeduplicatePatientDataCommandHandler`**:
   - Calls `IPatientDeduplicationService.DeduplicateAsync(patientId)`
   - On completion, updates `ExtractedData` batch with canonical flags in a single EF `SaveChangesAsync` transaction

8. **Service registration** (DI):
   - Register `PatientDeduplicationService` as scoped via `IPatientDeduplicationService`
   - Inject `ISemanticKernelClientFactory` (config-driven model provider, supports both OpenAI and Azure OpenAI per AIR-O03)

---

## Current Project State

```
Server/
├── src/
│   ├── Application/
│   │   └── Clinical/
│   │       ├── Interfaces/
│   │       │   └── IPatientDeduplicationService.cs    # CREATE
│   │       └── Commands/
│   │           └── DeduplicatePatientData/            # CREATE
│   └── Infrastructure/
│       └── AI/
│           ├── Services/
│           │   └── PatientDeduplicationService.cs     # CREATE
│           ├── Prompts/
│           │   └── deduplication/                     # CREATE (prompt templates)
│           └── Guardrails/
│               └── deduplication-guardrails.json      # CREATE
```

> Placeholder — update tree once US_040 extraction pipeline is confirmed.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/src/Application/Clinical/Interfaces/IPatientDeduplicationService.cs` | Interface: `DeduplicateAsync(Guid patientId, CancellationToken ct)` |
| CREATE | `Server/src/Infrastructure/AI/Services/PatientDeduplicationService.cs` | Semantic Kernel de-dup: similarity query, GPT-4o confirmation, canonical selection, confidence flagging, circuit breaker |
| CREATE | `Server/src/Application/Clinical/Commands/DeduplicatePatientData/DeduplicatePatientDataCommand.cs` | MediatR command record |
| CREATE | `Server/src/Application/Clinical/Commands/DeduplicatePatientData/DeduplicatePatientDataCommandHandler.cs` | Calls service, persists canonical flags in transaction |
| CREATE | `Server/src/Infrastructure/AI/Prompts/deduplication/deduplication-system.txt` | System role prompt (clinical de-duplication instructions + output JSON schema) |
| CREATE | `Server/src/Infrastructure/AI/Prompts/deduplication/deduplication-user.txt` | User turn template (field pair with PII redacted) |
| CREATE | `Server/src/Infrastructure/AI/Guardrails/deduplication-guardrails.json` | Content filter config (reject harmful/biased clinical outputs — AIR-S04) |
| MODIFY | `Server/src/Domain/Entities/ExtractedData.cs` | Add `IsCanonical`, `CanonicalGroupId`, `DeduplicationStatus` properties |
| MODIFY | `Server/src/Infrastructure/Persistence/Configurations/ExtractedDataConfiguration.cs` | EF Fluent API for new fields |

---

## External References

- [Microsoft Semantic Kernel 1.x — .NET docs](https://learn.microsoft.com/en-us/semantic-kernel/overview/)
- [Semantic Kernel — Resilience / Circuit Breaker](https://learn.microsoft.com/en-us/semantic-kernel/concepts/kernel-filters)
- [pgvector — Cosine distance operator](https://github.com/pgvector/pgvector#querying)
- [OpenAI text-embedding-3-small](https://platform.openai.com/docs/models/embeddings)
- [AIR-O01 Token budget (8,000 tokens)](https://platform.openai.com/docs/guides/production-best-practices)
- [HIPAA AI audit logging — OWASP LLM Top 10 LLM09](https://owasp.org/www-project-top-10-for-large-language-model-applications/)
- [OWASP A03 Injection — parameterised SQL](https://cheatsheetseries.owasp.org/cheatsheets/DotNet_Security_Cheat_Sheet.html)

---

## Build Commands

- Backend build: `dotnet build` (from `Server/` folder)
- Backend tests: `dotnet test`

---

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] **[AI Tasks]** Prompt templates validated with test inputs (field pairs from synthetic patient data)
- [ ] **[AI Tasks]** Guardrails tested — malformed/harmful GPT-4o response rejected and logged
- [ ] **[AI Tasks]** Fallback logic tested: when circuit breaker is open, de-dup falls back to similarity-only and marks `DeduplicationStatus = FallbackManual`
- [ ] **[AI Tasks]** Token budget enforcement verified — batches >7,800 tokens are split, not rejected
- [ ] **[AI Tasks]** Audit logging verified — each AI call produces an `AuditLog` entry with `prompt_hash` and `response_hash` (no raw PII in logs — AIR-S03)
- [ ] Cosine similarity threshold 0.7 correctly collapses duplicate fields across documents
- [ ] Canonical entry selected as highest-confidence in each similarity cluster
- [ ] Fields with `confidence < 0.80` have `IsLowConfidence = true` in persisted canonical records (AIR-003)
- [ ] PII redaction verified: patient name/DOB/insuranceId absent from prompt logs (AIR-S01)

---

## Implementation Checklist

- [ ] Create `IPatientDeduplicationService` interface with `DeduplicateAsync` signature
- [ ] Implement `PatientDeduplicationService` — pgvector cosine similarity query (parameterised, OWASP A03 safe), cluster grouping, canonical selection by highest confidence
- [ ] Integrate GPT-4o via Semantic Kernel for ambiguous cluster confirmation (similarity 0.70–0.85); apply PII redaction on prompt construction (AIR-S01)
- [ ] Register AI audit logging before response processing: write `AuditLog { entityType = "AIPromptLog", details = { prompt_hash, response_hash, tokens_used } }` (AIR-S03)
- [ ] Implement token budget enforcement (AIR-O01): count tokens per batch; split batches exceeding 7,800 tokens
- [ ] Configure Semantic Kernel `ResiliencePipeline` for circuit breaker — 3 failures / 5-min window → `FallbackManual` path (AIR-O02)
- [ ] Set `IsLowConfidence = true` on canonical entries with `confidence < 0.80` (AIR-003)
- [ ] Create `DeduplicatePatientDataCommandHandler` — calls service, persists all `ExtractedData` canonical flag updates in a single EF `SaveChangesAsync` transaction
- [ ] **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation
- [ ] **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- [ ] **[AI Tasks - MANDATORY]** Verify AIR-002, AIR-003, AIR-R02, AIR-S01, AIR-S03, AIR-O01, AIR-O02 requirements are met
