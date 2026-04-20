# Task - task_001_ai_safety_guardrails_pipeline

## Requirement Reference

- **User Story:** us_049 — AI Safety Guardrails & Immutable Prompt Audit Logging
- **Story Location:** `.propel/context/tasks/EP-010/us_049/us_049.md`
- **Acceptance Criteria:**
  - AC-1: When an AI prompt is constructed for OpenAI, the PII redaction filter replaces all patient identifiers (name, DOB, email, phone, address, insurance ID) with anonymized tokens before transmission; if redaction fails, the request is blocked entirely and no prompt is sent.
  - AC-2: When a RAG pipeline retrieves document chunks, the ACL filter verifies the requesting user is authorised to access each chunk's patient context; unauthorised chunks are silently excluded from the prompt context.
  - AC-3: When the AI model returns a response, the content safety filter evaluates the output; responses containing harmful, biased, or clinically inappropriate content are blocked, a Serilog alert is logged, and the request falls back to manual review.
  - AC-4: When any AI interaction completes (prompt sent + response received), an immutable `AiPromptAuditLog` record is written containing the redacted prompt, response, model version, UTC timestamp, and requesting user ID — via `IAiPromptAuditWriter`.
- **Edge Cases:**
  - PII redaction failure: if `PiiRedactionFilter` throws, the SK pipeline must abort before sending the prompt; a Serilog `Error` is logged with the session ID; no partial or unredacted prompt reaches OpenAI.
  - Content filter false positive: the block is still enforced; the blocking event is logged as `contentFilterBlocked = true` in the audit record; staff sees "Review blocked by content filter" message surfaced by the calling handler.

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

| Layer               | Technology                 | Version    |
| ------------------- | -------------------------- | ---------- |
| AI/ML Orchestration | Microsoft Semantic Kernel  | 1.x        |
| AI/ML Provider      | OpenAI GPT-4o              | —          |
| Backend             | ASP.NET Core Web API       | .net 10     |
| Cache               | Upstash Redis              | Serverless |
| Logging             | Serilog                    | 4.x        |

**Note:** All code and libraries MUST be compatible with versions listed above.

---

## AI References (AI Tasks Only)

| Reference Type           | Value                                                       |
| ------------------------ | ----------------------------------------------------------- |
| **AI Impact**            | Yes                                                         |
| **AIR Requirements**     | AIR-S01, AIR-S02, AIR-S03, AIR-S04                         |
| **AI Pattern**           | Guardrails / Pre-prompt filter + Post-response filter       |
| **Prompt Template Path** | N/A (filter-level, not prompt-level)                        |
| **Guardrails Config**    | `Server/AI/Guardrails/`                                     |
| **Model Provider**       | OpenAI GPT-4o (via Semantic Kernel)                         |

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

Implement four interlocking Semantic Kernel safety guardrails that sit in the AI pipeline between the application layer and the external OpenAI provider. All four are implemented as SK filter interfaces registered on the `Kernel`:

1. **`PiiRedactionFilter`** (`IPromptRenderFilter`, AIR-S01) — Pre-prompt: replaces patient identifiers in the rendered prompt string with anonymized tokens (`[NAME]`, `[DOB]`, `[EMAIL]`, `[PHONE]`, `[ADDRESS]`, `[INSURANCE_ID]`) using deterministic regex patterns. Throws `PiiRedactionException` on any failure, aborting the pipeline before transmission.

2. **`RagAclFilter`** (called from RAG retrieval step, AIR-S02) — Filters retrieved document chunks by verifying `chunk.PatientId` is accessible to the requesting user (`IAuthorizationService.AuthorizeAsync`). Unauthorised chunks are removed from the context window before prompt construction. Integrated into the existing RAG retrieval orchestrator (US_040).

3. **`ContentSafetyFilter`** (`IFunctionInvocationFilter`, AIR-S04) — Post-response: evaluates the AI output string against a keyword/pattern blocklist and a custom `IContentSafetyEvaluator` (extensible for Azure AI Content Safety integration in Phase 2). On block: throws `ContentSafetyException`; Serilog `Error` logged; `contentFilterBlocked = true` written to the audit record.

4. **`AiPromptAuditHook`** (`IFunctionInvocationFilter`, AIR-S03) — Wraps every SK function invocation: captures the rendered (post-redaction) prompt, the response text, model name, token counts, and `contentFilterBlocked` flag; writes an immutable `AiPromptAuditLog` record via `IAiPromptAuditWriter`. Runs even when `ContentSafetyFilter` blocks (called in the finally-equivalent path via `context.Exception` inspection).

The four filters are registered in priority order on `KernelBuilder`: `PiiRedactionFilter` → `ContentSafetyFilter` → `AiPromptAuditHook`. `RagAclFilter` is injected directly into the RAG retrieval pipeline (not as a global SK filter).

---

## Dependent Tasks

- `task_002_be_ai_prompt_audit_persistence.md` (EP-010/us_049) — `IAiPromptAuditWriter` must be registered before `AiPromptAuditHook` can use it.
- `task_003_db_ai_prompt_audit_log_schema.md` (EP-010/us_049) — `AiPromptAuditLog` table must exist.
- US_040 RAG retrieval orchestrator — `RagAclFilter` is injected into the existing retrieval step.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `PiiRedactionFilter` (new) | AI / Guardrails | CREATE — SK `IPromptRenderFilter`: regex-replace 6 PII categories; throw `PiiRedactionException` on failure |
| `RagAclFilter` (new) | AI / Guardrails | CREATE — Chunk-level ACL predicate: `IAuthorizationService.AuthorizeAsync` per chunk; exclude unauthorised chunks |
| `ContentSafetyFilter` (new) | AI / Guardrails | CREATE — SK `IFunctionInvocationFilter`: keyword/pattern blocklist + `IContentSafetyEvaluator`; throw `ContentSafetyException` on block |
| `AiPromptAuditHook` (new) | AI / Guardrails | CREATE — SK `IFunctionInvocationFilter`: capture prompt + response; write `AiPromptAuditLog` via `IAiPromptAuditWriter` |
| `IContentSafetyEvaluator` (new) | AI / Guardrails | CREATE — Interface for content evaluation; Phase 1 impl: `KeywordContentSafetyEvaluator` (regex-based blocklist) |
| `PiiTokenMap` (new) | AI / Guardrails | CREATE — Static regex dictionary mapping PII patterns to anonymized tokens; deterministic, reversible for audit trail |
| `RagRetrievalOrchestrator` (existing — US_040) | AI / RAG | MODIFY — Inject `RagAclFilter`; apply chunk-level ACL predicate after retrieval, before prompt construction |
| `AiModuleRegistration` (existing) | DI Bootstrap | MODIFY — Register all four filters on `KernelBuilder` in priority order; register `IContentSafetyEvaluator` |

---

## Implementation Plan

1. **Implement `PiiTokenMap`** — static dictionary of compiled `Regex` patterns:
   - Name: `\b[A-Z][a-z]+ [A-Z][a-z]+\b` → `[NAME]`
   - DOB: `\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b` → `[DOB]`
   - Email: standard RFC 5322-compatible email regex → `[EMAIL]`
   - Phone: `\b(\+?1[\s.-]?)?\(?\d{3}\)?[\s.-]?\d{3}[\s.-]?\d{4}\b` → `[PHONE]`
   - Address: street number + street name pattern → `[ADDRESS]`
   - Insurance ID: `\b[A-Z]{2,3}\d{6,12}\b` → `[INSURANCE_ID]`
   - Patterns are applied in sequence using `Regex.Replace`. Uses `RegexOptions.Compiled` for performance.

2. **Implement `PiiRedactionFilter`** — SK `IPromptRenderFilter`:
   - `OnPromptRenderAsync`: retrieve rendered prompt string from `context.RenderedPrompt`.
   - Apply all `PiiTokenMap` patterns via `Regex.Replace`. If any `Regex` throws: log `Serilog.Log.Error("PII redaction failed for session {SessionId}", ...)` and throw `PiiRedactionException("PII redaction failed — request blocked")`.
   - Set `context.RenderedPrompt = redactedPrompt` and call `await next(context)`.

3. **Implement `RagAclFilter`**:
   - `FilterChunksAsync(IEnumerable<DocumentChunk> chunks, ClaimsPrincipal user) → IEnumerable<DocumentChunk>`:
     - For each chunk: call `IAuthorizationService.AuthorizeAsync(user, chunk.PatientId, "CanAccessPatient")`.
     - Return only chunks where authorization succeeded. Excluded chunks are logged at `Serilog.Log.Debug` level (no error — exclusion is expected behaviour).

4. **Implement `IContentSafetyEvaluator` / `KeywordContentSafetyEvaluator`**:
   - `EvaluateAsync(string responseText) → ContentSafetyResult { IsBlocked, BlockedReason }`.
   - Phase 1 implementation: match response against a configurable keyword blocklist loaded from `appsettings.json` key `AiSafety:BlockedKeywords` (list of strings).
   - Returns `IsBlocked = true` with the first matched keyword as `BlockedReason`.

5. **Implement `ContentSafetyFilter`** — SK `IFunctionInvocationFilter`:
   - `OnFunctionInvocationAsync`: after `await next(context)`, retrieve response text from `context.Result`.
   - Call `IContentSafetyEvaluator.EvaluateAsync(responseText)`.
   - If `IsBlocked`: log `Serilog.Log.Error("Content safety filter blocked response for session {SessionId}: {Reason}", ...)`, store `contentFilterBlocked = true` in `context.Arguments["contentFilterBlocked"]`, throw `ContentSafetyException(BlockedReason)`.

6. **Implement `AiPromptAuditHook`** — SK `IFunctionInvocationFilter` (registered last, so it captures the final state):
   - `OnFunctionInvocationAsync`: call `await next(context)` in a try/finally block.
   - In finally: extract `renderedPrompt` (from `context.Arguments["__renderedPrompt"]` set by `PiiRedactionFilter`), `responseText` (from `context.Result`, or null if blocked), `modelName` (from kernel metadata), `contentFilterBlocked` (from `context.Arguments`).
   - Call `await IAiPromptAuditWriter.WriteAsync(new AiPromptAuditLogEntry { ... })`.
   - Never throws — audit write failures are swallowed and logged at `Serilog.Log.Error` level (audit failure must not disrupt clinical workflow).

7. **Modify `RagRetrievalOrchestrator`** (US_040) — after `IVectorStore.SearchAsync(embedding)` returns chunks, call `RagAclFilter.FilterChunksAsync(chunks, httpContextAccessor.HttpContext.User)` before assembling the context window.

8. **Register in `AiModuleRegistration`** — on `KernelBuilder`: `.AddFilter<PiiRedactionFilter>()`, `.AddFilter<ContentSafetyFilter>()`, `.AddFilter<AiPromptAuditHook>()`; register `IContentSafetyEvaluator → KeywordContentSafetyEvaluator`; register `RagAclFilter` as scoped service.

---

## Current Project State

```
Server/
  AI/
    Guardrails/
      AiOutputSchemaValidator.cs        ← EXISTS (US_048 task_001)
    RAG/
      RagRetrievalOrchestrator.cs       ← EXISTS (US_040) — MODIFY
  DI/
    AiModuleRegistration.cs             ← EXISTS — MODIFY
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/AI/Guardrails/PiiRedactionFilter.cs` | SK IPromptRenderFilter: regex PII replacement; throw PiiRedactionException on failure |
| CREATE | `Server/AI/Guardrails/PiiTokenMap.cs` | Static compiled regex dictionary: 6 PII categories → anonymized tokens |
| CREATE | `Server/AI/Guardrails/RagAclFilter.cs` | Chunk-level ACL predicate: IAuthorizationService per chunk; exclude unauthorised |
| CREATE | `Server/AI/Guardrails/ContentSafetyFilter.cs` | SK IFunctionInvocationFilter: keyword blocklist + IContentSafetyEvaluator; throw ContentSafetyException |
| CREATE | `Server/AI/Guardrails/IContentSafetyEvaluator.cs` | Interface: EvaluateAsync → ContentSafetyResult |
| CREATE | `Server/AI/Guardrails/KeywordContentSafetyEvaluator.cs` | Phase 1 impl: appsettings.json blocklist; Regex match |
| CREATE | `Server/AI/Guardrails/AiPromptAuditHook.cs` | SK IFunctionInvocationFilter (last): try/finally capture; call IAiPromptAuditWriter; never throws |
| MODIFY | `Server/AI/RAG/RagRetrievalOrchestrator.cs` | Apply RagAclFilter.FilterChunksAsync after vector search, before prompt assembly |
| MODIFY | `Server/DI/AiModuleRegistration.cs` | Register 3 SK filters in priority order; IContentSafetyEvaluator; RagAclFilter |

---

## External References

- [Semantic Kernel 1.x — IPromptRenderFilter](https://learn.microsoft.com/en-us/semantic-kernel/concepts/filters?tabs=csharp#prompt-render-filter) — Pre-prompt filter: modify rendered prompt string before LLM call
- [Semantic Kernel 1.x — IFunctionInvocationFilter](https://learn.microsoft.com/en-us/semantic-kernel/concepts/filters?tabs=csharp#function-invocation-filter) — Pre/post function execution hooks; `context.Result` for response capture
- [System.Text.RegularExpressions — RegexOptions.Compiled](https://learn.microsoft.com/en-us/dotnet/api/system.text.regularexpressions.regexoptions) — Performance-compiled regex for PII pattern matching
- [ASP.NET Core — IAuthorizationService](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased?view=aspnetcore-9.0) — Resource-based authorization for per-chunk ACL checks
- [AIR-S01 (design.md)](../.propel/context/docs/design.md) — PII redaction before external provider transmission; NFR-013 HIPAA
- [AIR-S02 (design.md)](../.propel/context/docs/design.md) — Document-level ACL in retrieval pipeline; NFR-006 RBAC
- [AIR-S03 (design.md)](../.propel/context/docs/design.md) — Immutable prompt/response audit log; DR-011 7-year retention
- [AIR-S04 (design.md)](../.propel/context/docs/design.md) — Content filtering; harmful/biased outputs blocked; NFR-013
- [AD-6 (design.md)](../.propel/context/docs/design.md) — Semantic Kernel for AI orchestration; SK filters for guardrails

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [ ] Unit tests pass (xUnit + Moq)
- [ ] `PiiRedactionFilter`: prompt containing "John Smith, 01/15/1980, john@email.com" → all tokens replaced before `next()` called; original not forwarded
- [ ] `PiiRedactionFilter`: throws `PiiRedactionException` when redaction fails; `next()` never called; no prompt forwarded
- [ ] `RagAclFilter`: chunk with unauthorised `PatientId` excluded from result; authorised chunk retained
- [ ] `ContentSafetyFilter`: response containing blocklisted keyword → `ContentSafetyException` thrown; Serilog error logged
- [ ] `AiPromptAuditHook`: audit record written for both successful and blocked interactions (try/finally)
- [ ] `AiPromptAuditHook`: audit write failure does not propagate exception to calling handler
- [ ] Filters registered in correct priority order: PiiRedaction → ContentSafety → AuditHook

---

## Implementation Checklist

- [ ] Create `PiiTokenMap` with 6 compiled regex patterns (Name, DOB, Email, Phone, Address, InsuranceID)
- [ ] Create `PiiRedactionFilter` (`IPromptRenderFilter`): apply PiiTokenMap; throw `PiiRedactionException` on failure; block pipeline
- [ ] Create `RagAclFilter`: `FilterChunksAsync` using `IAuthorizationService.AuthorizeAsync` per chunk; exclude unauthorised
- [ ] Create `IContentSafetyEvaluator` interface and `KeywordContentSafetyEvaluator` (appsettings.json blocklist)
- [ ] Create `ContentSafetyFilter` (`IFunctionInvocationFilter`): evaluate response; throw `ContentSafetyException`; set `contentFilterBlocked` argument
- [ ] Create `AiPromptAuditHook` (`IFunctionInvocationFilter`): try/finally; capture redacted prompt + response + flags; call `IAiPromptAuditWriter`; never throws
- [ ] Modify `RagRetrievalOrchestrator` to apply `RagAclFilter.FilterChunksAsync` after vector search
- [ ] Register all components in `AiModuleRegistration` in priority order
