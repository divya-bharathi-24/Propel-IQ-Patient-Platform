# Task - task_003_ai_intake_semantic_kernel

## Requirement Reference

- **User Story:** us_028 — AI Conversational Intake Chat Interface
- **Story Location:** `.propel/context/tasks/EP-005/us_028/us_028.md`
- **Acceptance Criteria:**
  - AC-2: NLU extracts structured intake fields (demographics, medical history, symptoms, medications) from free-text responses with per-field confidence scores; next question generated within 5 seconds
  - AC-3: Fields extracted with confidence below 80% set `needsClarification: true`; follow-up clarification question generated for those fields
  - AC-4: Confirmed field values correctly mapped to JSONB groups for IntakeRecord persistence
- **Edge Cases:**
  - OpenAI API unavailable: circuit breaker open after 3 consecutive failures in 5-minute window → throw `AiServiceUnavailableException` → BE returns `isFallback: true` (AIR-O02)
  - Too short / insufficient response (< 15 chars or zero fields extracted): return rephrased specific follow-up question; no fields populated in response

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

| Layer              | Technology            | Version |
| ------------------ | --------------------- | ------- |
| AI Orchestration   | Microsoft Semantic Kernel | 1.x |
| AI Model Provider  | OpenAI API / Azure OpenAI | GPT-4o |
| Resilience         | Polly                 | 8.x     |
| Backend            | ASP.NET Core Web API  | .net 10  |
| Logging            | Serilog               | 4.x     |
| Testing — Unit     | xUnit + Moq           | 2.x     |
| AI/ML              | OpenAI GPT-4o (Azure OpenAI for HIPAA production path) | GPT-4o |
| Mobile             | N/A                   | N/A     |

> All code and libraries MUST be compatible with versions above.

---

## AI References (AI Tasks Only)

| Reference Type           | Value |
| ------------------------ | ----- |
| **AI Impact**            | Yes   |
| **AIR Requirements**     | AIR-004 (multi-turn NLU structured extraction), AIR-003 (confidence < 80% flag + clarification), AIR-O01 (8,000 token budget per request), AIR-O02 (circuit breaker: 3 failures / 5-min window), AIR-O03 (prompt version configurable without redeployment), AIR-O04 (token usage + latency audit logging) |
| **AI Pattern**           | Conversational — multi-turn dialogue with structured JSON output extraction |
| **Prompt Template Path** | `Server/Modules/AI/Prompts/intake-system-v{version}.txt` (version from `appsettings.json`) |
| **Guardrails Config**    | Output schema validation: `IntakeTurnResponseSchema`; reject responses that fail JSON parse; return clarification prompt on schema failure |
| **Model Provider**       | OpenAI GPT-4o (dev/staging); Azure OpenAI GPT-4o (production HIPAA BAA path) |

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

Implement `SemanticKernelAiIntakeService` — the concrete implementation of `IAiIntakeService` defined in task_002. This service uses Microsoft Semantic Kernel 1.x to orchestrate multi-turn GPT-4o conversations for AI-assisted patient intake, enforcing the platform's AI operational requirements:

- **AIR-O01** — 8,000 token budget per request via `PromptExecutionSettings.MaxTokens`
- **AIR-O02** — Circuit breaker via Polly `AdvancedCircuitBreakerPolicy` (3 failures / 5 min)
- **AIR-O03** — Prompt template version loaded from `appsettings.json`; swap without redeployment
- **AIR-O04** — Structured audit log per turn: token counts, latency (ms), confidence distribution

The system prompt instructs GPT-4o to act as a medical intake assistant, extract structured fields as a JSON object with confidence scores, generate follow-up questions for low-confidence fields (< 0.8), and ask a more specific question when the patient's response is too short to extract any fields.

---

## Dependent Tasks

- **EP-005/us_028 task_002_be_ai_intake_api** — `IAiIntakeService` interface and `AiServiceUnavailableException` must be defined first; this task implements the interface

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `SemanticKernelAiIntakeService` (implements `IAiIntakeService`) | `Server/Modules/AI/Services/SemanticKernelAiIntakeService.cs` |
| CREATE | `IntakeTurnResult` record | `Server/Modules/AI/Models/IntakeTurnResult.cs` |
| CREATE | `IntakePromptBuilder` (builds Semantic Kernel chat history from `ConversationTurn` list) | `Server/Modules/AI/Services/IntakePromptBuilder.cs` |
| CREATE | `Server/Modules/AI/Prompts/intake-system-v1.txt` | System prompt template v1 — instructs GPT-4o on extraction schema and follow-up logic |
| CREATE | `AiSettings` options class | `Server/Modules/AI/Options/AiSettings.cs` |
| MODIFY | `appsettings.json` | Add `Ai` section: `IntakePromptVersion`, `CircuitBreakerFailureThreshold`, `CircuitBreakerWindowSeconds`, `MaxTokensPerRequest`, `ModelDeploymentName` |
| MODIFY | `Server/Program.cs` | Register `SemanticKernelAiIntakeService`; bind `AiSettings`; add Semantic Kernel + Polly circuit breaker |

---

## Implementation Plan

1. **`AiSettings` options** (bound from `appsettings.json` section `"Ai"`):
   ```csharp
   public class AiSettings
   {
       public string ModelDeploymentName { get; set; } = "gpt-4o";
       public int MaxTokensPerRequest { get; set; } = 8000;    // AIR-O01
       public string IntakePromptVersion { get; set; } = "v1"; // AIR-O03
       public int CircuitBreakerFailureThreshold { get; set; } = 3;
       public int CircuitBreakerWindowSeconds { get; set; } = 300;
       public bool UseAzureOpenAI { get; set; } = false;       // true in production
   }
   ```
   - `OpenAI:ApiKey` and `AzureOpenAI:Endpoint` from environment variables only — never in `appsettings.json` (OWASP A02)

2. **System prompt template** (`intake-system-v1.txt`):
   ```
   You are a clinical intake assistant for a healthcare platform. Your task is to guide patients
   through a health history intake conversation and extract structured clinical data.

   EXTRACTION RULES:
   - Extract fields across four categories: demographics, medicalHistory, symptoms, medications
   - For each extracted field, provide a confidence score (0.0–1.0)
   - If confidence < 0.8 for any field, set needsClarification: true and add a targeted follow-up
   - If the patient's response is fewer than 15 words or no fields can be extracted, set
     extractedFields: [] and ask a more specific, concrete follow-up question
   - Do NOT invent field values; only extract what the patient explicitly stated

   RESPONSE FORMAT (ALWAYS valid JSON):
   {
     "extractedFields": [
       { "fieldName": "string", "value": "string", "confidence": 0.0-1.0, "needsClarification": bool }
     ],
     "nextQuestion": "string",
     "isComplete": false
   }

   FIELD NAMES (use exact names):
   demographics: name, dateOfBirth, gender, address, phone, email, emergencyContact
   medicalHistory: existingConditions, surgicalHistory, familyHistory, allergies
   symptoms: primarySymptom, symptomDuration, symptomSeverity, associatedSymptoms
   medications: currentMedications, dosages, frequency, prescribingPhysician
   ```
   - Template file path loaded from `AiSettings.IntakePromptVersion` → `Prompts/intake-system-{version}.txt`

3. **`IntakePromptBuilder`**:
   - Converts `List<ConversationTurn>` + current `List<ExtractedField>` into `ChatHistory` for Semantic Kernel
   - Prepends system message from loaded prompt template
   - Appends each turn as `ChatRole.User` or `ChatRole.Assistant`
   - Returns `ChatHistory` object ready for `IChatCompletionService.GetChatMessageContentAsync()`

4. **`SemanticKernelAiIntakeService.ProcessTurnAsync()`** steps:
   a. Load prompt template from `Prompts/intake-system-{version}.txt`
   b. Build `ChatHistory` via `IntakePromptBuilder`
   c. Set `PromptExecutionSettings { MaxTokens = AiSettings.MaxTokensPerRequest }` (AIR-O01)
   d. Call `await _kernel.GetRequiredService<IChatCompletionService>().GetChatMessageContentAsync(chatHistory, settings)` wrapped in Polly circuit breaker policy
   e. Parse response JSON → validate against `IntakeTurnResponseSchema` (System.Text.Json schema validation)
   f. If JSON parse fails: log `Serilog.Error("AiIntake_ParseFailed", sessionId)` → return `IntakeTurnResult` with `NextQuestion = "I didn't quite understand — could you describe your main health concern in a full sentence?"` + `ExtractedFields = []`
   g. Return `IntakeTurnResult { IsFallback = false, ExtractedFields, AiResponse = result.NextQuestion, IsComplete = result.IsComplete }`

5. **Polly Circuit Breaker** (Polly 8.x `ResiliencePipelineBuilder`):
   ```csharp
   _pipeline = new ResiliencePipelineBuilder()
       .AddCircuitBreaker(new CircuitBreakerStrategyOptions
       {
           FailureRatio = 1.0,
           MinimumThroughput = AiSettings.CircuitBreakerFailureThreshold,   // 3
           SamplingDuration = TimeSpan.FromSeconds(AiSettings.CircuitBreakerWindowSeconds), // 300s
           BreakDuration = TimeSpan.FromSeconds(60),
           OnOpened = _ => { _logger.Warning("AiCircuitBreaker_Opened"); return ValueTask.CompletedTask; },
           OnClosed = _ => { _logger.Information("AiCircuitBreaker_Closed"); return ValueTask.CompletedTask; }
       })
       .Build();
   ```
   - When circuit is open, Polly throws `BrokenCircuitException` → caught in `ProcessTurnAsync` → throw `AiServiceUnavailableException` (AIR-O02)

6. **Audit logging per turn** (AIR-O04):
   ```csharp
   _logger.Information("AiIntake_TurnProcessed {@Audit}", new {
       SessionId = sessionId,
       PromptTokens = usage.PromptTokenCount,
       CompletionTokens = usage.CompletionTokenCount,
       LatencyMs = stopwatch.ElapsedMilliseconds,
       FieldsExtracted = result.ExtractedFields.Count,
       AvgConfidence = result.ExtractedFields.Average(f => f.Confidence),
       LowConfidenceCount = result.ExtractedFields.Count(f => f.Confidence < 0.8)
   });
   ```

7. **Confidence threshold check** (AIR-003):
   - After JSON parse, iterate `extractedFields`; for each where `confidence < 0.8`: set `NeedsClarification = true`; the prompt template instructs GPT-4o to include a follow-up in `nextQuestion` for those fields — no additional post-processing required

8. **Program.cs registration**:
   ```csharp
   builder.Services.Configure<AiSettings>(builder.Configuration.GetSection("Ai"));
   // Use Azure OpenAI in production, direct OpenAI in dev
   if (aiSettings.UseAzureOpenAI)
       builder.Services.AddAzureOpenAIChatCompletion(...);
   else
       builder.Services.AddOpenAIChatCompletion(aiSettings.ModelDeploymentName, Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
   builder.Services.AddSingleton<ResiliencePipeline>(/* circuit breaker pipeline */);
   builder.Services.AddScoped<IAiIntakeService, SemanticKernelAiIntakeService>();
   ```

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no Server/ scaffold yet — greenfield ASP.NET Core project)
```

> Update with actual `Server/Modules/AI/` tree after scaffold is complete.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Modules/AI/Services/SemanticKernelAiIntakeService.cs` | `IAiIntakeService` implementation: Semantic Kernel multi-turn chat, Polly circuit breaker, JSON parse + validation |
| CREATE | `Server/Modules/AI/Services/IntakePromptBuilder.cs` | Converts `ConversationTurn` list → Semantic Kernel `ChatHistory` |
| CREATE | `Server/Modules/AI/Models/IntakeTurnResult.cs` | `{ IsFallback, ExtractedFields, AiResponse, IsComplete }` |
| CREATE | `Server/Modules/AI/Options/AiSettings.cs` | Configuration POCO: model name, token budget, circuit breaker thresholds, prompt version |
| CREATE | `Server/Modules/AI/Prompts/intake-system-v1.txt` | System prompt: extraction rules, JSON output schema, confidence thresholds, follow-up instructions |
| MODIFY | `appsettings.json` | Add `"Ai"` section: `ModelDeploymentName`, `MaxTokensPerRequest`, `IntakePromptVersion`, `CircuitBreakerFailureThreshold`, `CircuitBreakerWindowSeconds`, `UseAzureOpenAI` |
| MODIFY | `Server/Program.cs` | Register Semantic Kernel chat completion service; bind `AiSettings`; build and register Polly `ResiliencePipeline`; `IAiIntakeService → SemanticKernelAiIntakeService` |

---

## External References

- [Microsoft Semantic Kernel 1.x — Chat Completion (GetChatMessageContentAsync)](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/?tabs=csharp)
- [Microsoft Semantic Kernel 1.x — Azure OpenAI integration](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/azure-openai?tabs=csharp)
- [Polly 8.x — Circuit Breaker resilience strategy](https://www.pollydocs.org/strategies/circuit-breaker)
- [Polly 8.x — ResiliencePipelineBuilder (.NET 8/9)](https://www.pollydocs.org/pipelines/)
- [OpenAI GPT-4o — PromptExecutionSettings MaxTokens](https://learn.microsoft.com/en-us/dotnet/api/microsoft.semantickernel.connectors.openai.openaipromptexecutionsettings.maxtokens)
- [OWASP A02 — Cryptographic Failures: API keys in environment variables only](https://owasp.org/Top10/A02_2021-Cryptographic_Failures/)
- [Azure OpenAI — HIPAA BAA path for production](https://learn.microsoft.com/en-us/azure/ai-services/openai/concepts/models)
- [AIR-O01 — 8,000 token budget per request (design.md line 174)](design.md)
- [AIR-O02 — Circuit breaker: 3 failures / 5-min window (design.md line 175)](design.md)
- [AIR-O03 — Prompt version config without redeployment (design.md line 176)](design.md)
- [AIR-O04 — Token usage + latency audit metrics (design.md line 177)](design.md)

---

## Build Commands

```bash
# Restore packages (includes Semantic Kernel + Polly)
dotnet restore

# Build
dotnet build

# Run API (ensure OPENAI_API_KEY env var is set)
OPENAI_API_KEY=sk-... dotnet run --project Server/Server.csproj

# Run unit tests (mock IAiIntakeService in BE tests; mock IChatCompletionService in AI layer tests)
dotnet test
```

---

## Implementation Validation Strategy

- [ ] `SemanticKernelAiIntakeService.ProcessTurnAsync()` with a valid user message returns `IsFallback = false` and at least one `ExtractedField`
- [ ] Field with GPT-4o confidence < 0.8 returned with `NeedsClarification = true`
- [ ] Token usage in Serilog structured log after each turn: `PromptTokens`, `CompletionTokens`, `LatencyMs` all present (AIR-O04)
- [ ] Force 3 consecutive OpenAI HTTP 500 errors → circuit opens → `AiServiceUnavailableException` thrown
- [ ] Update `Ai:IntakePromptVersion` in `appsettings.json` to `v2` (with `intake-system-v2.txt` present) → next call uses new prompt without restart (AIR-O03)
- [ ] Prompt tokens for a full session do not exceed 8,000 (AIR-O01): assert `PromptExecutionSettings.MaxTokens = 8000` in unit test
- [ ] Short user message (< 15 words): `extractedFields = []`; `NextQuestion` is a non-empty rephrased follow-up
- [ ] `OPENAI_API_KEY` not present in any committed config file (security gate — OWASP A02)

---

## Implementation Checklist

- [ ] Create `AiSettings` options class; bind from `appsettings.json "Ai"` section; load `OPENAI_API_KEY` from environment variable only (OWASP A02)
- [ ] Create `intake-system-v1.txt` prompt template: extraction rules, JSON output schema, confidence thresholds, short-response guard, follow-up instructions
- [ ] Create `IntakePromptBuilder`: convert `List<ConversationTurn>` + system prompt → Semantic Kernel `ChatHistory`
- [ ] Create `SemanticKernelAiIntakeService.ProcessTurnAsync()`: build `ChatHistory`, apply `MaxTokens = 8000`, call `IChatCompletionService`, parse JSON, validate schema, return `IntakeTurnResult`
- [ ] Add Polly `AdvancedCircuitBreakerPolicy` (3 failures / 300s window): wrap `IChatCompletionService` call; `BrokenCircuitException` → throw `AiServiceUnavailableException` (AIR-O02)
- [ ] Implement structured Serilog audit log per turn: `PromptTokens`, `CompletionTokens`, `LatencyMs`, `FieldsExtracted`, `AvgConfidence`, `LowConfidenceCount` (AIR-O04)
- [ ] Support prompt version switching: load template from `Prompts/intake-system-{AiSettings.IntakePromptVersion}.txt` — file-based, no redeployment required (AIR-O03)
- [ ] Register `SemanticKernelAiIntakeService` in `Program.cs`; add Semantic Kernel chat completion (OpenAI dev / Azure OpenAI prod); register Polly `ResiliencePipeline` singleton
