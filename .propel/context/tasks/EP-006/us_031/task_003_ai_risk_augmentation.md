# Task - task_003_ai_risk_augmentation

## Requirement Reference

- **User Story:** us_031 — No-Show Risk Score Calculation & Color-Coded Staff Display
- **Story Location:** `.propel/context/tasks/EP-006/us_031/us_031.md`
- **Acceptance Criteria:**
  - AC-1: No-show risk score incorporates AI pattern recognition over historical appointment behavioral data (AIR-007) — the AI augmentation delta adjusts the rule-based base score
  - AC-3: When the AI augmentation service is unavailable, the rule-based engine runs independently (augmentation delta = 0); degraded mode is logged via Serilog
- **Edge Cases:**
  - No prior appointment history: prompt context contains empty history array; GPT-4o returns `delta = 0.0` (neutral augmentation); rule-based score stands
  - Missing required data: prompt context notes missing fields; GPT-4o returns `delta = 0.0` and a rationale entry in the factors list

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
| **AIR Requirements**     | AIR-007 (rule-based + AI pattern recognition), AIR-O01 (8,000 token budget), AIR-O02 (circuit breaker: 3 failures / 5-min window → degraded mode), AIR-O03 (prompt version configurable without redeployment), AIR-O04 (token usage + latency audit logging) |
| **AI Pattern**           | Scoring augmentation — GPT-4o receives structured patient behavioral context and returns a score delta |
| **Prompt Template Path** | `Server/Modules/Risk/Prompts/risk-assessment-v{version}.txt` (version from `appsettings.json`) |
| **Guardrails Config**    | Output schema: `{ delta: float [-0.15, +0.15], rationale: string, confidence: float [0-1] }`; reject out-of-range delta; clamp to `[-0.15, +0.15]`; return 0.0 delta on schema failure |
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

Implement `SemanticKernelNoShowRiskAugmenter` — the concrete implementation of `IAiNoShowRiskAugmenter` defined in task_002. This service uses Microsoft Semantic Kernel 1.x to call GPT-4o with a structured prompt containing the patient's behavioral history (prior appointments, no-show events, reminder engagement patterns) and returns a score `delta` in range `[-0.15, +0.15]` that the rule-based engine (`RuleBasedNoShowRiskCalculator`) applies to the base score.

The AI augmentation is intentionally constrained:
- **Delta range `[-0.15, +0.15]`**: AI can adjust the base score by at most ±15 percentage points — the rule-based score dominates (AIR-007 "augmented by AI pattern recognition")
- **Degraded mode** (AIR-O02): when Polly circuit opens after 3 failures in 5 minutes, `AiNoShowRiskUnavailableException` is thrown → caught in task_002 handler → `delta = 0.0` (pure rule-based score used)
- **Token budget** (AIR-O01): 8,000 tokens per request; behavioral history is trimmed to last 24 months to stay within budget

---

## Dependent Tasks

- **EP-006/us_031 task_002_be_noshow_risk_engine** — `IAiNoShowRiskAugmenter` interface and `AiNoShowRiskUnavailableException` are defined there; this task implements them
- **US_006 (Foundational)** — `Appointment` history query requires the appointment table to exist

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `SemanticKernelNoShowRiskAugmenter` (implements `IAiNoShowRiskAugmenter`) | `Server/Modules/Risk/Services/SemanticKernelNoShowRiskAugmenter.cs` |
| CREATE | `AiNoShowRiskUnavailableException` | `Server/Common/Exceptions/AiNoShowRiskUnavailableException.cs` |
| CREATE | `RiskAugmentationResult` record | `Server/Modules/Risk/Models/RiskAugmentationResult.cs` |
| CREATE | `RiskAssessmentPromptBuilder` | `Server/Modules/Risk/Services/RiskAssessmentPromptBuilder.cs` |
| CREATE | `Server/Modules/Risk/Prompts/risk-assessment-v1.txt` | System prompt template for risk augmentation |
| MODIFY | `appsettings.json` | Add `RiskAssessmentPromptVersion` to existing `"Ai"` section |
| MODIFY | `Server/Program.cs` | Register `IAiNoShowRiskAugmenter → SemanticKernelNoShowRiskAugmenter` (replaces stub registered in task_002) |

---

## Implementation Plan

1. **`AiNoShowRiskUnavailableException`**:
   ```csharp
   public class AiNoShowRiskUnavailableException : Exception
   {
       public AiNoShowRiskUnavailableException()
           : base("AI augmentation service unavailable — using rule-based score only") { }
   }
   ```

2. **System prompt template** (`risk-assessment-v1.txt`):
   ```
   You are a healthcare analytics assistant specializing in appointment no-show prediction.
   You will receive structured data about a patient's appointment behavioral history.
   Your task is to analyze behavioral patterns and provide a score ADJUSTMENT (delta) to
   a pre-calculated rule-based no-show risk score.

   CONSTRAINTS:
   - Delta MUST be in range [-0.15, +0.15] (±15 percentage points only)
   - Positive delta = higher risk than rule-based estimate
   - Negative delta = lower risk than rule-based estimate
   - If insufficient history to form a pattern judgment, return delta: 0.0
   - Do NOT suggest delta > 0.15 or < -0.15 under any circumstances

   RESPONSE FORMAT (ALWAYS valid JSON — no markdown):
   {
     "delta": <float -0.15 to 0.15>,
     "rationale": "<one sentence explaining the adjustment>",
     "confidence": <float 0.0-1.0>
   }
   ```
   - Template file path constructed from `AiSettings.RiskAssessmentPromptVersion`

3. **`RiskAssessmentPromptBuilder`**:
   - Input: `patientId`, `appointmentId`, `baseScore`, `List<AppointmentHistoryEntry>` (last 24 months from DB)
   - `AppointmentHistoryEntry`: `{ Date, Status, WasReminded, ReminderDelivered, IntakeCompleted }`
   - Builds `ChatHistory` with:
     - System message: loaded from `risk-assessment-v{version}.txt`
     - User message: structured JSON payload:
       ```json
       {
         "baseScore": 0.62,
         "historyLast24Months": [
           { "date": "2025-11-10", "status": "NoShow", "reminderDelivered": true, "intakeCompleted": false },
           { "date": "2025-08-15", "status": "Completed", "reminderDelivered": true, "intakeCompleted": true }
         ],
         "upcomingAppointmentLeadTimeDays": 5
       }
       ```
   - History trimmed to last 24 months (max 50 records) to stay within AIR-O01 8,000-token budget

4. **`SemanticKernelNoShowRiskAugmenter.GetAugmentationDeltaAsync()`** steps:
   a. Query appointment history from DB (last 24 months, max 50 records) via EF Core
   b. Build `ChatHistory` via `RiskAssessmentPromptBuilder`
   c. Set `PromptExecutionSettings { MaxTokens = AiSettings.MaxTokensPerRequest }` (AIR-O01 — 8,000)
   d. Execute via Polly pipeline: `await _pipeline.ExecuteAsync(async ct => await _chatService.GetChatMessageContentAsync(chatHistory, settings, cancellationToken: ct))`
   e. Parse JSON response → validate `delta` is in `[-0.15, +0.15]`; clamp if out-of-range (defensive guard)
   f. If JSON parse fails or schema invalid: log `Serilog.Warning("NoShowRisk_AiParseFailed", appointmentId)` → return `delta = 0.0`
   g. Audit log: `Serilog.Information("NoShowRisk_AiAugmented {@Audit}", new { AppointmentId, Delta, Confidence, PromptTokens, LatencyMs })`  (AIR-O04)
   h. Return `delta`

5. **Polly Circuit Breaker** (shared with US_028 AI layer if using same `ResiliencePipeline` singleton):
   - 3 consecutive failures within 5-minute window → circuit opens
   - `BrokenCircuitException` caught → throw `AiNoShowRiskUnavailableException` (AIR-O02)
   - On open: `Serilog.Warning("AiCircuitBreaker_Opened_RiskAugmenter")`
   - Note: if the same `ResiliencePipeline` registered for US_028 is reused, ensure it is keyed separately to prevent US_028 chat failures from tripping the risk augmenter circuit

6. **`Program.cs` registration** (replaces stub from task_002):
   ```csharp
   services.AddScoped<IAiNoShowRiskAugmenter, SemanticKernelNoShowRiskAugmenter>();
   ```
   - Re-uses `AiSettings`, Semantic Kernel chat completion service, and Polly pipeline already registered by US_028 task_003 (or registers them independently if US_028 is not yet scaffolded)

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no Server/ scaffold yet — greenfield ASP.NET Core project)
```

> Update with actual `Server/Modules/Risk/` tree after scaffold is complete.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Common/Exceptions/AiNoShowRiskUnavailableException.cs` | Thrown when Polly circuit opens; caught in task_002 `CalculateNoShowRiskCommandHandler` |
| CREATE | `Server/Modules/Risk/Services/SemanticKernelNoShowRiskAugmenter.cs` | `IAiNoShowRiskAugmenter` implementation; Semantic Kernel GPT-4o call; Polly circuit breaker; JSON parse + clamp; audit log |
| CREATE | `Server/Modules/Risk/Services/RiskAssessmentPromptBuilder.cs` | Converts patient history + base score → Semantic Kernel `ChatHistory`; trims to last 24 months / max 50 records |
| CREATE | `Server/Modules/Risk/Models/RiskAugmentationResult.cs` | `{ Delta, Rationale, Confidence }` |
| CREATE | `Server/Modules/Risk/Prompts/risk-assessment-v1.txt` | System prompt: delta constraints `[-0.15, +0.15]`, JSON output schema, neutral fallback instruction |
| MODIFY | `appsettings.json` | Add `RiskAssessmentPromptVersion: "v1"` inside existing `"Ai"` section |
| MODIFY | `Server/Program.cs` | Register `IAiNoShowRiskAugmenter → SemanticKernelNoShowRiskAugmenter` (scoped); ensure Polly pipeline keyed for risk augmenter |

---

## External References

- [Microsoft Semantic Kernel 1.x — Chat Completion](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/?tabs=csharp)
- [Polly 8.x — Circuit Breaker strategy (ResiliencePipelineBuilder)](https://www.pollydocs.org/strategies/circuit-breaker)
- [Polly 8.x — Pipeline keying (separate pipelines per service)](https://www.pollydocs.org/pipelines/)
- [AIR-007 — Rule-based + AI pattern recognition augmentation (design.md line 155)](design.md)
- [AIR-O01 — 8,000 token budget per AI request (design.md line 174)](design.md)
- [AIR-O02 — Circuit breaker: 3 failures / 5-min window (design.md line 175)](design.md)
- [AIR-O04 — Token usage + latency metrics (design.md line 177)](design.md)
- [OWASP A02 — API keys in environment variables only; never in appsettings.json](https://owasp.org/Top10/A02_2021-Cryptographic_Failures/)
- [Azure OpenAI — HIPAA BAA path for production](https://learn.microsoft.com/en-us/azure/ai-services/openai/concepts/models)

---

## Build Commands

```bash
# Restore packages
dotnet restore

# Build
dotnet build

# Run unit tests (mock IChatCompletionService; inject canned GPT-4o JSON responses)
dotnet test

# Run API (ensure OPENAI_API_KEY env var set)
OPENAI_API_KEY=sk-... dotnet run --project Server/Server.csproj
```

---

## Implementation Validation Strategy

- [ ] `SemanticKernelNoShowRiskAugmenter.GetAugmentationDeltaAsync()` with patient having 2 prior no-shows → `delta > 0` (positive upward adjustment)
- [ ] `GetAugmentationDeltaAsync()` with no history → `delta = 0.0`
- [ ] Out-of-range GPT-4o response `delta = 0.3` → clamped to `0.15` (defensive guard)
- [ ] Invalid JSON from GPT-4o → Serilog warning `NoShowRisk_AiParseFailed`; returns `delta = 0.0`; no exception propagated
- [ ] Force 3 consecutive HTTP 500 from OpenAI → circuit opens → `AiNoShowRiskUnavailableException` thrown → task_002 handler catches → `degradedMode = true` in AuditLog; Serilog warning logged (AC-3)
- [ ] Audit log entry `NoShowRisk_AiAugmented` present after successful augmentation: `Delta`, `Confidence`, `PromptTokens`, `LatencyMs` all populated (AIR-O04)
- [ ] History trimmed to last 24 months: unit test with 60-month history confirms only 24 months passed to prompt builder
- [ ] `OPENAI_API_KEY` absent from all committed config files (security gate — OWASP A02)

---

## Implementation Checklist

- [X] Create `AiNoShowRiskUnavailableException`; ensure it is caught in task_002 `CalculateNoShowRiskCommandHandler` (coordinate with task_002 stub)
- [X] Create `risk-assessment-v1.txt` prompt: delta constraints `[-0.15, +0.15]`, strict JSON-only output, neutral fallback instruction for no/insufficient history
- [X] Create `RiskAssessmentPromptBuilder`: system prompt + structured JSON user message with `baseScore`, `historyLast24Months` (max 50 entries), `upcomingAppointmentLeadTimeDays`; history trimmed to last 24 months for AIR-O01 budget compliance
- [X] Create `SemanticKernelNoShowRiskAugmenter`: Semantic Kernel `IChatCompletionService` call; `MaxTokens = 8000`; Polly circuit breaker pipeline (keyed for risk augmenter); JSON parse + delta clamp `[-0.15, +0.15]`; return `0.0` on parse failure
- [X] Add Polly circuit breaker for risk augmenter (3 failures / 300s window, keyed independently from US_028 intake circuit breaker); `BrokenCircuitException` → throw `AiNoShowRiskUnavailableException`
- [X] Implement Serilog audit log per augmentation call: `NoShowRisk_AiAugmented { AppointmentId, Delta, Confidence, PromptTokens, LatencyMs }` (AIR-O04)
- [X] Support prompt version switching: load from `Prompts/risk-assessment-{AiSettings.RiskAssessmentPromptVersion}.txt`; add `RiskAssessmentPromptVersion` key to `appsettings.json "Ai"` section (AIR-O03)
- [X] Register `IAiNoShowRiskAugmenter → SemanticKernelNoShowRiskAugmenter` in `Program.cs`; verify `OPENAI_API_KEY` loaded from environment variable only (OWASP A02)
