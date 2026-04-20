# Task - TASK_003

## Requirement Reference

- **User Story**: US_040 — AI Document Extraction RAG Pipeline
- **Story Location**: `.propel/context/tasks/EP-008-I/us_040/us_040.md`
- **Acceptance Criteria**:
  - AC-2: Top-5 chunks ≥ 0.7 cosine similarity retrieved, re-ranked, used to construct GPT-4o prompt within 8,000-token budget.
  - AC-3: GPT-4o extraction returns structured JSON; `ExtractedData` records created per clinical field with `value`, `confidence` (0–1), `sourcePageNumber`, `sourceTextSnippet`, `documentId`; fields with confidence < 80% flagged for priority staff review.
  - AC-4: `ClinicalDocument.processingStatus = Completed`; patient notified via email.
- **Edge Cases**:
  - EC-2: OpenAI API circuit breaker open → processing paused; document remains `Pending`; retried when circuit breaker resets after 5-minute window (AIR-O02).

## Design References (Frontend Tasks Only)

| Reference Type         | Value |
|------------------------|-------|
| **UI Impact**          | No    |
| **Figma URL**          | N/A   |
| **Wireframe Status**   | N/A   |
| **Wireframe Type**     | N/A   |
| **Wireframe Path/URL** | N/A   |
| **Screen Spec**        | N/A   |
| **UXR Requirements**   | N/A   |
| **Design Tokens**      | N/A   |

## Applicable Technology Stack

| Layer           | Technology                       | Version |
|-----------------|----------------------------------|---------|
| Backend         | ASP.NET Core Web API             | .net 10  |
| AI/ML           | OpenAI GPT-4o                    | Latest  |
| AI Orchestration| Microsoft Semantic Kernel        | 1.x     |
| Vector Store    | pgvector (PostgreSQL extension)  | 0.7+    |
| ORM             | Entity Framework Core            | 9.x     |
| Database        | PostgreSQL                       | 16+     |
| Logging         | Serilog                          | 4.x     |
| Mobile          | N/A                              | N/A     |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type       | Value |
|----------------------|-------|
| **AI Impact**        | Yes   |
| **AIR Requirements** | AIR-001, AIR-002, AIR-003, AIR-O01, AIR-O02, AIR-Q01, AIR-Q03, AIR-S01, AIR-S03, AIR-S04 |
| **AI Pattern**       | RAG   |
| **Prompt Template Path** | `Server/PropelIQ.Clinical/AI/Prompts/clinical-extraction.yaml` |
| **Guardrails Config** | `Server/PropelIQ.Clinical/AI/Guardrails/ExtractionGuardrailFilter.cs` |
| **Model Provider**   | OpenAI (GPT-4o) / Azure OpenAI (HIPAA BAA path) |

> **AI Impact = Yes** — this task implements the GPT-4o RAG extraction prompt, output schema validation guardrails, confidence thresholding, and circuit breaker.

### **CRITICAL: AI Implementation Requirements**
- **MUST** reference `clinical-extraction.yaml` prompt template during implementation (AIR-Prompt versioning)
- **MUST** enforce token budget ≤ 8,000 tokens per request (AIR-O01) — truncate context chunks if needed
- **MUST** implement guardrails: JSON schema validation (≥99% validity, AIR-Q03) and content filtering (AIR-S04)
- **MUST** implement fallback for low-confidence responses: fields with confidence < 80% flagged (AIR-003)
- **MUST** log prompt + response metadata to AuditLog (no patient PII in log body — AIR-S03)
- **MUST** implement circuit breaker via Semantic Kernel — open after 3 consecutive failures in 5 minutes (AIR-O02)

## Mobile References (Mobile Tasks Only)

| Reference Type      | Value |
|---------------------|-------|
| **Mobile Impact**   | No    |
| **Platform Target** | N/A   |
| **Min OS Version**  | N/A   |
| **Mobile Framework**| N/A   |

## Task Overview

Implement `IExtractionOrchestrator` and its concrete `ExtractionOrchestrator` which coordinates the full GPT-4o extraction pass: retrieves relevant chunks from pgvector (via task_002's `IVectorStoreService`), constructs a structured extraction prompt from `clinical-extraction.yaml` within the 8,000-token budget (AIR-O01), sends the prompt to GPT-4o via Semantic Kernel, validates the JSON response against the `ClinicalExtractionSchema` guardrail (AIR-Q03), applies content filtering (AIR-S04), maps each extracted field to an `ExtractedData` record with `confidence`, `sourcePageNumber`, `sourceTextSnippet`, and `documentId` (AC-3, AIR-002), flags fields with confidence < 0.80 for priority staff review (AIR-003), and handles circuit breaker open state by leaving `processingStatus = Pending` for retry (EC-2, AIR-O02).

## Dependent Tasks

- **task_001_ai_pdf_chunking_embedding_service.md** — chunks and embeddings must be generated first.
- **task_002_ai_vector_store_retrieval.md** — `IVectorStoreService.RetrieveRelevantChunksAsync` must be implemented.
- **task_005_db_extraction_schema_migration.md** — `ExtractedData` table must exist.

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `IExtractionOrchestrator` | `PropelIQ.Clinical` | CREATE |
| `ExtractionOrchestrator` | `PropelIQ.Clinical` | CREATE |
| `clinical-extraction.yaml` | `PropelIQ.Clinical/AI/Prompts/` | CREATE |
| `ExtractionGuardrailFilter` | `PropelIQ.Clinical/AI/Guardrails/` | CREATE |
| `IExtractedDataRepository` | `PropelIQ.Clinical` | CREATE |
| `ExtractedDataRepository` | `PropelIQ.Infrastructure` | CREATE |
| `ClinicalExtractionSchema` | `PropelIQ.Clinical/AI/Models/` | CREATE |

## Implementation Plan

1. **Create `clinical-extraction.yaml` prompt template** — Semantic Kernel YAML prompt format. System message establishes role (clinical data extractor). User message template: `"Extract all clinical data from the following document excerpts. Return ONLY a valid JSON object matching the schema. Document context: {{$context}} Fields to extract: vitals, medications, diagnoses, allergies, immunizations, surgical_history."` Include schema definition in the prompt for GPT-4o structured output.
2. **Token budget enforcement (AIR-O01)** — before prompt construction, count tokens of `system + user + context chunks` using the same tokenizer from task_001. If total > 7,500 (leaving 500 for completion), truncate lowest-relevance chunks until budget is satisfied. Log tokens used.
3. **Implement `ExtractionOrchestrator.ExtractAsync(Guid documentId, CancellationToken ct)`**:
   a. Query `IVectorStoreService.RetrieveRelevantChunksAsync` for top-5 chunks (AC-2).
   b. Construct prompt from `clinical-extraction.yaml` with `{{$context}}` = joined chunk texts.
   c. Check circuit breaker state via Semantic Kernel's built-in `IChatCompletionService` with circuit breaker policy. If OPEN → return `ExtractionResult.CircuitBreakerOpen` without calling API (EC-2, AIR-O02).
   d. Call `GPT-4o` via Semantic Kernel `IChatCompletionService.GetChatMessageContentAsync`.
   e. Validate response JSON against `ClinicalExtractionSchema` (AIR-Q03).
   f. Apply `ExtractionGuardrailFilter` for harmful content detection (AIR-S04).
   g. Map each field to `ExtractedData` record with confidence, page reference, snippet (AIR-001, AIR-002).
   h. Flag fields where `confidence < 0.80` as `priorityReview = true` (AIR-003).
4. **Implement `ExtractionGuardrailFilter`** as a Semantic Kernel `IPromptRenderFilter` or `IFunctionInvocationFilter` — validates that the JSON output contains no harmful clinical recommendations (AIR-S04) and schema is valid (AIR-Q03). Invalid schema → throw `ExtractionSchemaValidationException`; caller sets `processingStatus = Failed`.
5. **Persist `ExtractedData` records** — call `IExtractedDataRepository.InsertBatchAsync(fields)`. Each record: `documentId`, `patientId`, `dataType` (Vital/History/Medication/Allergy/Diagnosis), `fieldName`, `value`, `confidence`, `sourcePageNumber`, `sourceTextSnippet`.
6. **Priority review flagging** — fields with confidence < 0.80: set a `priorityReview = true` flag (new boolean column on `ExtractedData` — added in task_005). Staff verification UI uses this flag to surface low-confidence fields first (AIR-003).
7. **Audit logging** — write to `AuditLog`: action = `ClinicalExtractionCompleted`, entityId = documentId, details = `{ fieldCount, lowConfidenceCount, tokenUsed, model }` — no patient name, DOB, or clinical values in audit log body (AIR-S03).
8. **Circuit breaker state** — Semantic Kernel tracks consecutive failures. After 3 failures within 5 minutes, circuit opens. While open, `ExtractAsync` returns `ExtractionResult.CircuitBreakerOpen`; the pipeline worker in task_004 leaves `processingStatus = Pending` and will retry in the next poll cycle (AIR-O02).

### Prompt Template: `clinical-extraction.yaml`

```yaml
name: ClinicalDataExtraction
description: Extracts structured clinical data from patient document excerpts
template_format: semantic-kernel
input_variables:
  - name: context
    description: Relevant document excerpts retrieved via RAG
    is_required: true
execution_settings:
  default:
    max_tokens: 500
    temperature: 0.0
    response_format: json_object
template: |
  <message role="system">
  You are a clinical data extraction assistant. Extract structured data ONLY from the provided document excerpts.
  Return ONLY a valid JSON object. Do not infer, hallucinate, or add data not present in the excerpts.
  </message>
  <message role="user">
  Extract all clinical data from these document excerpts:

  {{$context}}

  Return a JSON object with this exact schema:
  {
    "vitals": [{"fieldName": "", "value": "", "confidence": 0.0, "sourcePageNumber": 0, "sourceTextSnippet": ""}],
    "medications": [{"fieldName": "", "value": "", "confidence": 0.0, "sourcePageNumber": 0, "sourceTextSnippet": ""}],
    "diagnoses": [{"fieldName": "", "value": "", "confidence": 0.0, "sourcePageNumber": 0, "sourceTextSnippet": ""}],
    "allergies": [{"fieldName": "", "value": "", "confidence": 0.0, "sourcePageNumber": 0, "sourceTextSnippet": ""}],
    "immunizations": [{"fieldName": "", "value": "", "confidence": 0.0, "sourcePageNumber": 0, "sourceTextSnippet": ""}],
    "surgicalHistory": [{"fieldName": "", "value": "", "confidence": 0.0, "sourcePageNumber": 0, "sourceTextSnippet": ""}]
  }
  </message>
```

### Pseudocode

```csharp
// ExtractionOrchestrator.cs
public async Task<ExtractionResult> ExtractAsync(Guid documentId, CancellationToken ct)
{
    var doc = await _docRepo.GetByIdAsync(documentId, ct);
    var authorizedDocIds = new[] { documentId.ToString() }; // ACL: only this doc

    var chunks = await _vectorStore.RetrieveRelevantChunksAsync(
        queryEmbedding: await _embeddingService.EmbedQueryAsync("clinical data extraction", ct),
        authorizedDocIds, topK: 5, threshold: 0.7f, ct);

    var context = string.Join("\n---\n", chunks.Select(c => c.Text));
    var tokenCount = _tokenCounter.Count(context);

    if (tokenCount > 7_500)
        context = TruncateToTokenBudget(chunks, 7_500); // AIR-O01

    // EC-2: circuit breaker check via Semantic Kernel
    if (_circuitBreaker.IsOpen)
        return ExtractionResult.CircuitBreakerOpen;

    var result = await _kernel.InvokePromptAsync<ClinicalExtractionOutput>(
        "ClinicalDataExtraction", new KernelArguments { ["context"] = context }, ct);

    _guardrail.Validate(result); // AIR-Q03, AIR-S04

    var fields = MapToExtractedData(result, documentId, doc.PatientId);
    await _extractedDataRepo.InsertBatchAsync(fields, ct);

    await _auditLog.LogAsync("system", "ClinicalExtractionCompleted", documentId.ToString(),
        new { fieldCount = fields.Count, lowConfidenceCount = fields.Count(f => f.Confidence < 0.8),
              tokenUsed = tokenCount, model = "gpt-4o" }, ct);

    return ExtractionResult.Success(fields);
}
```

## Current Project State

```
Server/
├── PropelIQ.Clinical/
│   └── AI/
│       ├── Services/
│       │   ├── DocumentChunkingService.cs    # From task_001
│       │   ├── EmbeddingGenerationService.cs # From task_001
│       │   └── VectorStoreService.cs         # From task_002
│       ├── Prompts/
│       │   └── (empty — to be created)
│       └── Guardrails/
│           └── (empty — to be created)
└── PropelIQ.Infrastructure/
    └── Repositories/
        └── (ExtractedDataRepository to be created)
```

> Placeholder — update with actual paths as dependent tasks complete.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/PropelIQ.Clinical/AI/Services/IExtractionOrchestrator.cs` | Orchestrator interface |
| CREATE | `Server/PropelIQ.Clinical/AI/Services/ExtractionOrchestrator.cs` | Full RAG + GPT-4o extraction flow |
| CREATE | `Server/PropelIQ.Clinical/AI/Prompts/clinical-extraction.yaml` | Versioned SK prompt template |
| CREATE | `Server/PropelIQ.Clinical/AI/Guardrails/ExtractionGuardrailFilter.cs` | Schema validation + content filter |
| CREATE | `Server/PropelIQ.Clinical/AI/Models/ClinicalExtractionOutput.cs` | Deserialization target for GPT-4o JSON |
| CREATE | `Server/PropelIQ.Clinical/AI/Models/ExtractionResult.cs` | Result discriminated union (Success/Failed/CircuitBreakerOpen) |
| CREATE | `Server/PropelIQ.Clinical/AI/Repositories/IExtractedDataRepository.cs` | Repository interface |
| CREATE | `Server/PropelIQ.Infrastructure/Repositories/ExtractedDataRepository.cs` | EF Core repository |

## External References

- [Semantic Kernel — Prompt templates YAML format](https://learn.microsoft.com/en-us/semantic-kernel/concepts/prompts/yaml-schema)
- [Semantic Kernel — IFunctionInvocationFilter (guardrails)](https://learn.microsoft.com/en-us/semantic-kernel/concepts/filters)
- [Semantic Kernel — Circuit breaker / resilience policies](https://learn.microsoft.com/en-us/semantic-kernel/concepts/kernel-api/kernel-resiliency)
- [OpenAI GPT-4o structured outputs (JSON mode)](https://platform.openai.com/docs/guides/structured-outputs)
- [AIR-O01 — 8,000-token budget per request](../docs/design.md)
- [AIR-O02 — Circuit breaker: 3 failures in 5 min](../docs/design.md)
- [AIR-Q03 — 99% output schema validity](../docs/design.md)
- [AIR-S04 — Content filtering for harmful outputs](../docs/design.md)
- [AIR-003 — 80% confidence threshold for priority flagging](../docs/design.md)

## Build Commands

```bash
cd Server

# Semantic Kernel is already added in task_001
dotnet restore
dotnet build PropelIQ.sln

# Validate prompt template loads correctly
dotnet run --project PropelIQ.Api
# Check startup logs for SK kernel prompt registration
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Prompt constructed within 8,000 total tokens (AIR-O01)
- [ ] GPT-4o response deserialized to `ClinicalExtractionOutput` without schema errors
- [ ] `ExtractedData` records created for each field in response with correct `confidence`, `sourcePageNumber`, `sourceTextSnippet`, `documentId`
- [ ] Fields with `confidence < 0.80` have `priorityReview = true` (AIR-003)
- [ ] Circuit breaker open → `ExtractionResult.CircuitBreakerOpen` returned; no API call attempted (EC-2)
- [ ] Schema validation failure → `ExtractionSchemaValidationException` thrown; caller sets `processingStatus = Failed`
- [ ] AuditLog entry contains fieldCount, lowConfidenceCount, tokenUsed, model — no clinical values or patient PII (AIR-S03)

## Implementation Checklist

- [ ] Create `clinical-extraction.yaml` prompt template with system message, extraction schema, and `{{$context}}` variable (prompt versioning per AIR-O03)
- [ ] Implement token budget enforcement: count tokens of context + prompt; truncate lowest-relevance chunks if total > 7,500 (AIR-O01)
- [ ] Implement circuit breaker check via Semantic Kernel before each GPT-4o call: open after 3 failures in 5 min; return `CircuitBreakerOpen` without API call (AIR-O02)
- [ ] Implement `ExtractionGuardrailFilter`: validate JSON schema completeness (AIR-Q03) and content filtering for harmful recommendations (AIR-S04)
- [ ] Map GPT-4o JSON response to `ExtractedData` records: value, confidence, sourcePageNumber, sourceTextSnippet, documentId (AIR-001, AIR-002)
- [ ] Flag fields with confidence < 0.80 as `priorityReview = true` (AIR-003)
- [ ] Write AuditLog entry: fieldCount, lowConfidenceCount, tokenUsed, model — no PII in log body (AIR-S03)
- [ ] Load GPT-4o model name and Azure OpenAI endpoint from `IConfiguration` — never hardcode (OWASP A02)
