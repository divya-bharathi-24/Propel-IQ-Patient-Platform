# Task - task_001_ai_operational_resilience_pipeline

## Requirement Reference

- **User Story:** us_050 — AI Operational Controls — Circuit Breaker, Token Budget & Model Swap
- **Story Location:** `.propel/context/tasks/EP-010/us_050/us_050.md`
- **Acceptance Criteria:**
  - AC-1: Circuit breaker trips on 3 consecutive AI provider failures within 5 minutes; short-circuits subsequent requests for 5 minutes; manual fallback prompt displayed to staff; circuit breaker alert logged (AIR-O02).
  - AC-2: Token budget of 8,000 tokens enforced per request; prompts exceeding budget are truncated (lowest-similarity RAG chunks removed first); truncation event logged; no over-budget request sent to provider (AIR-O01).
  - AC-3: AI model version reads from live configuration; updated within 5 minutes of operator change without application restart (AIR-O03).
- **Edge Cases:**
  - Circuit breaker re-trip: Three more failures after reset re-trip the breaker; exponential backoff applied for repeated trips within the same hour (`TTL * 2^(tripCount-1)`).
  - Token truncation: Lowest-similarity RAG chunks removed first (per retrieval re-ranking order); Serilog Warning includes original token count, chunk IDs dropped, and final token count.

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

| Layer    | Technology                        | Version |
| -------- | --------------------------------- | ------- |
| AI/ML    | Microsoft Semantic Kernel         | 1.x     |
| Backend  | ASP.NET Core Web API / .NET       | 9       |
| Cache    | Upstash Redis (StackExchange.Redis) | Serverless |
| Tokeniser | TiktokenSharp (GPT-4o token counting) | latest |

**Note:** All code and libraries MUST be compatible with versions listed above.

---

## AI References (AI Tasks Only)

| Reference Type           | Value                                  |
| ------------------------ | -------------------------------------- |
| **AI Impact**            | Yes                                    |
| **AIR Requirements**     | AIR-O01, AIR-O02, AIR-O03              |
| **AI Pattern**           | SK Filter Chain (IPromptRenderFilter + IFunctionInvocationFilter) |
| **Prompt Template Path** | N/A — filter-level controls, not prompt authoring |
| **Guardrails Config**    | `appsettings.json` → `AiResilience` section |
| **Model Provider**       | OpenAI (GPT-4o)                        |

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

Implement three AI operational control mechanisms as Semantic Kernel filters registered on the `KernelBuilder`. These run as part of every AI invocation — before any prompt leaves the service (token budget + model version) and after the provider response is received (circuit breaker tracking). All state is stored in Upstash Redis to support multi-instance serverless deployment on Neon free tier.

**Components:**
1. `CircuitBreakerFilter` (`IFunctionInvocationFilter`) — tracks consecutive failures in Redis; trips open state with exponential backoff; throws `CircuitBreakerOpenException` when open.
2. `TokenBudgetFilter` (`IPromptRenderFilter`) — counts GPT-4o tokens via TiktokenSharp; truncates lowest-similarity RAG chunks until within 8,000-token budget; logs dropped chunks.
3. `LiveAiModelConfig` + `AiModelVersionFilter` (`IPromptRenderFilter`) — reads current model version from Redis key (60-second in-memory cache); overrides SK model ID per invocation to implement hot-swap without restart.

These filters are registered in `AiModuleRegistration` (the existing SK DI registration class from US_040/US_049) in the order: `AiModelVersionFilter` → `TokenBudgetFilter` → `CircuitBreakerFilter`.

---

## Dependent Tasks

- `EP-010/us_040/` — AI extraction pipeline must exist (provides `KernelBuilder`, `AiModuleRegistration`, filter registration hooks).
- `EP-010/us_049/task_001_ai_safety_guardrails_pipeline.md` — safety filters already registered; this task adds after them in priority order.
- `EP-010/us_050/task_004_db_ai_operational_metrics_schema.md` — `AiOperationalMetrics` table must exist before metrics writer (from task_002) is called by these filters.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `CircuitBreakerFilter` (new) | AI Infrastructure | CREATE — SK `IFunctionInvocationFilter`: Redis-backed open/closed state, 3-failure trip, 5-min TTL, exponential backoff |
| `CircuitBreakerOpenException` (new) | AI Domain | CREATE — custom exception; caught in `AiExtractionOrchestrator` to return manual fallback `ExtractionResult` |
| `TokenBudgetFilter` (new) | AI Infrastructure | CREATE — SK `IPromptRenderFilter`: TiktokenSharp token count; truncate lowest-similarity chunks; log dropped chunks |
| `ILiveAiModelConfig` + `RedisLiveAiModelConfig` (new) | AI Infrastructure | CREATE — reads Redis key `ai:config:model_version` with 60-second in-memory cache; fallback to `appsettings.json` value |
| `AiModelVersionFilter` (new) | AI Infrastructure | CREATE — SK `IPromptRenderFilter`: calls `ILiveAiModelConfig.GetModelVersionAsync()`; no-ops if unchanged |
| `AiModuleRegistration` (existing) | Infrastructure / DI | MODIFY — register three new filters with correct priority order |
| `AiExtractionOrchestrator` (existing) | AI Application | MODIFY — catch `CircuitBreakerOpenException`; return `ExtractionResult.ManualFallback("AI provider unavailable")` |

---

## Implementation Plan

1. **`CircuitBreakerFilter` — Redis-backed circuit breaker** (SK `IFunctionInvocationFilter`):

   ```csharp
   public sealed class CircuitBreakerFilter : IFunctionInvocationFilter
   {
       private const string OpenKey    = "ai:cb:open";
       private const string CountKey   = "ai:cb:failures";
       private const string TripHourKey = "ai:cb:trips:{0}"; // {hour}
       private const int FailureThreshold = 3;
       private static readonly TimeSpan WindowTtl = TimeSpan.FromMinutes(5);

       public async Task OnFunctionInvocationAsync(FunctionInvocationContext ctx, Func<FunctionInvocationContext, Task> next)
       {
           // Check if open BEFORE invoking
           if (await _redis.KeyExistsAsync(OpenKey))
               throw new CircuitBreakerOpenException("AI circuit breaker is open — manual review required");

           try
           {
               await next(ctx);
               // Success: reset consecutive failure counter
               await _redis.KeyDeleteAsync(CountKey);
           }
           catch (Exception ex) when (ex is not CircuitBreakerOpenException)
           {
               await HandleFailureAsync();
               throw;
           }
       }

       private async Task HandleFailureAsync()
       {
           var db = _redis.GetDatabase();
           long count = await db.StringIncrementAsync(CountKey);
           if (count == 1)
               await db.KeyExpireAsync(CountKey, WindowTtl); // Start 5-min window on first failure

           if (count >= FailureThreshold)
           {
               string hourKey = string.Format(TripHourKey, DateTime.UtcNow.ToString("yyyyMMddHH"));
               long trips = await db.StringIncrementAsync(hourKey);
               await db.KeyExpireAsync(hourKey, TimeSpan.FromHours(2)); // Retain for same-hour backoff

               // Exponential backoff: 5min * 2^(trips-1)
               var openTtl = TimeSpan.FromMinutes(5 * Math.Pow(2, trips - 1));
               await db.StringSetAsync(OpenKey, "1", openTtl, When.NotExists); // NX: only trip if not already open
               await db.KeyDeleteAsync(CountKey);
               Log.Error("AI circuit breaker TRIPPED — trip #{TripCount} this hour; open for {OpenMinutes}m",
                   trips, openTtl.TotalMinutes);
           }
       }
   }
   ```

2. **`CircuitBreakerOpenException`**:

   ```csharp
   public sealed class CircuitBreakerOpenException : Exception
   {
       public CircuitBreakerOpenException(string message) : base(message) { }
   }
   ```

3. **`AiExtractionOrchestrator` modification** — catch `CircuitBreakerOpenException`:

   ```csharp
   catch (CircuitBreakerOpenException)
   {
       Log.Warning("AI circuit breaker open — returning manual fallback for session {SessionId}", sessionId);
       return ExtractionResult.ManualFallback(
           reason: "AI provider temporarily unavailable. Please review documents manually.",
           sessionId: sessionId);
   }
   ```

   `ExtractionResult.ManualFallback(...)` — static factory method (add to existing `ExtractionResult` if not present); sets `NeedsManualReview = true`, `FallbackReason = reason`.

4. **`TokenBudgetFilter` — token counting and truncation** (SK `IPromptRenderFilter`):

   ```csharp
   public sealed class TokenBudgetFilter : IPromptRenderFilter
   {
       private const int MaxTokens = 8_000;
       private readonly TiktokenTokenizer _tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");

       public async Task OnPromptRenderAsync(PromptRenderContext ctx, Func<PromptRenderContext, Task> next)
       {
           await next(ctx); // Let SK render the prompt first

           int tokenCount = _tokenizer.CountTokens(ctx.RenderedPrompt!);
           if (tokenCount <= MaxTokens) return;

           // Parse structured prompt: extract RAG chunks section
           // Chunks are appended in similarity-desc order (highest first from RAG retrieval)
           // Remove from the END (lowest similarity) until within budget
           var (truncatedPrompt, droppedChunks) = TruncateChunks(ctx.RenderedPrompt!, tokenCount);

           Log.Warning("Token budget exceeded: original={OriginalTokens}, dropped={DroppedChunkCount} chunks ({ChunkIds}), final={FinalTokens}",
               tokenCount, droppedChunks.Count, string.Join(",", droppedChunks), _tokenizer.CountTokens(truncatedPrompt));

           ctx.RenderedPrompt = truncatedPrompt;
       }

       private (string truncatedPrompt, List<string> droppedChunkIds) TruncateChunks(string prompt, int currentCount)
       {
           // Implementation: split prompt on chunk delimiter markers (e.g., "<!-- CHUNK: {id} -->")
           // Remove chunks from the end until tokenCount <= MaxTokens
           // Return modified prompt and list of dropped chunk IDs
       }
   }
   ```

   **Token counting library:** Use `TiktokenSharp` or `Microsoft.ML.Tokenizers` (which includes GPT-4o encoding in .NET). Register `TiktokenTokenizer` as a singleton in DI.

5. **`ILiveAiModelConfig` + `RedisLiveAiModelConfig` — hot-swap model version**:

   ```csharp
   public interface ILiveAiModelConfig
   {
       Task<string> GetModelVersionAsync(CancellationToken ct = default);
   }

   public sealed class RedisLiveAiModelConfig : ILiveAiModelConfig
   {
       private const string RedisKey = "ai:config:model_version";
       private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
       private volatile (string version, DateTimeOffset cachedAt) _cache = (string.Empty, DateTimeOffset.MinValue);

       public async Task<string> GetModelVersionAsync(CancellationToken ct = default)
       {
           if (DateTimeOffset.UtcNow - _cache.cachedAt < CacheTtl && !string.IsNullOrEmpty(_cache.version))
               return _cache.version;

           string? redisValue = await _redis.StringGetAsync(RedisKey);
           string version = !string.IsNullOrEmpty(redisValue)
               ? redisValue
               : _options.CurrentValue.DefaultModelVersion; // fallback to appsettings

           _cache = (version, DateTimeOffset.UtcNow);
           return version;
       }
   }
   ```

6. **`AiModelVersionFilter`** (SK `IPromptRenderFilter`):

   ```csharp
   public sealed class AiModelVersionFilter : IPromptRenderFilter
   {
       public async Task OnPromptRenderAsync(PromptRenderContext ctx, Func<PromptRenderContext, Task> next)
       {
           string modelVersion = await _config.GetModelVersionAsync();
           // Override the SK execution settings model before render
           if (ctx.Arguments.ExecutionSettings is { } settings
               && settings.TryGetValue(PromptExecutionSettings.DefaultServiceId, out var s)
               && s is OpenAIPromptExecutionSettings oaiSettings
               && oaiSettings.ModelId != modelVersion)
           {
               settings[PromptExecutionSettings.DefaultServiceId] = oaiSettings with { ModelId = modelVersion };
           }
           await next(ctx);
       }
   }
   ```

7. **`AiModuleRegistration` — register new filters**:

   Register in priority order (lower number = higher priority). Existing filters from US_049:
   - `PiiRedactionFilter` (priority 0 — `IPromptRenderFilter`)
   - `ContentSafetyFilter` (priority 100 — `IFunctionInvocationFilter`)
   - `AiPromptAuditHook` (priority 200 — `IFunctionInvocationFilter`, runs last)

   New filters inserted:
   - `AiModelVersionFilter` (priority -10 — `IPromptRenderFilter`, runs before PII so model is set first)
   - `TokenBudgetFilter` (priority 10 — `IPromptRenderFilter`, runs after PII redaction so token count is accurate)
   - `CircuitBreakerFilter` (priority 50 — `IFunctionInvocationFilter`, checks open state before invocation; tracks failures after)

   ```csharp
   kernelBuilder
       .AddFilter(new AiModelVersionFilter(_config), -10)
       .AddFilter(new TokenBudgetFilter(_tokenizer), 10)
       .AddFilter(new CircuitBreakerFilter(_redis, _logger), 50);
   ```

---

## Current Project State

```
Server/
  AI/
    Filters/
      PiiRedactionFilter.cs              ← EXISTS (US_049)
      ContentSafetyFilter.cs            ← EXISTS (US_049)
      AiPromptAuditHook.cs              ← EXISTS (US_049)
    Orchestration/
      AiExtractionOrchestrator.cs       ← EXISTS — MODIFY
    Registration/
      AiModuleRegistration.cs           ← EXISTS — MODIFY
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/AI/Filters/CircuitBreakerFilter.cs` | SK `IFunctionInvocationFilter`: Redis-backed open/closed; 3-failure trip; exponential backoff |
| CREATE | `Server/AI/Filters/TokenBudgetFilter.cs` | SK `IPromptRenderFilter`: TiktokenSharp token count; chunk truncation; log dropped chunks |
| CREATE | `Server/AI/Filters/AiModelVersionFilter.cs` | SK `IPromptRenderFilter`: reads `ILiveAiModelConfig`; overrides SK model ID per-invocation |
| CREATE | `Server/AI/Config/ILiveAiModelConfig.cs` | Interface: `GetModelVersionAsync()` |
| CREATE | `Server/AI/Config/RedisLiveAiModelConfig.cs` | Redis-backed with 60-second in-memory cache; fallback to `appsettings.json` `AiResilience:DefaultModelVersion` |
| CREATE | `Server/AI/Exceptions/CircuitBreakerOpenException.cs` | Custom exception; caught by `AiExtractionOrchestrator` |
| MODIFY | `Server/AI/Orchestration/AiExtractionOrchestrator.cs` | Catch `CircuitBreakerOpenException`; return `ExtractionResult.ManualFallback(...)` |
| MODIFY | `Server/AI/Registration/AiModuleRegistration.cs` | Register three new filters with correct priority ordering |
| MODIFY | `appsettings.json` | Add `AiResilience` section: `DefaultModelVersion`, `TokenBudgetLimit`, `CircuitBreakerFailureThreshold`, `CircuitBreakerWindowMinutes` |

---

## External References

- [Semantic Kernel 1.x — Function Invocation Filters](https://learn.microsoft.com/en-us/semantic-kernel/concepts/filters/function-invocation-filters) — `IFunctionInvocationFilter` priority registration
- [Semantic Kernel 1.x — Prompt Render Filters](https://learn.microsoft.com/en-us/semantic-kernel/concepts/filters/prompt-rendering-filters) — `IPromptRenderFilter` for pre-prompt token control
- [TiktokenSharp / Microsoft.ML.Tokenizers](https://github.com/microsoft/tokenizer) — GPT-4o BPE tokenizer for .NET; `CountTokens()` for exact budget enforcement
- [StackExchange.Redis — StringIncrementAsync](https://stackexchange.github.io/StackExchange.Redis/Basics.html) — atomic counter increment for failure window tracking
- [AIR-O01, AIR-O02, AIR-O03 (design.md)](../../../docs/design.md) — token budget, circuit breaker, model hot-swap requirements
- [AD-6 (design.md)](../../../docs/design.md) — SK for AI orchestration; model switching without vendor lock-in
- [AD-8 (design.md)](../../../docs/design.md) — Redis-backed state; multi-instance serverless safety

---

## Build Commands

- Refer to [`.propel/build/`](../../../../build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [ ] Circuit breaker opens after exactly 3 consecutive failures within a 5-minute window (verify Redis key `ai:cb:open` is set with ~5-min TTL)
- [ ] Circuit breaker resets after TTL expires; third subsequent failure re-trips with doubled TTL (exponential backoff)
- [ ] `AiExtractionOrchestrator` returns `ExtractionResult` with `NeedsManualReview = true` and non-null `FallbackReason` when circuit breaker is open
- [ ] Token budget: a prompt with 8,001 tokens is truncated to ≤8,000; Serilog Warning emitted with chunk IDs
- [ ] Token budget: a prompt with ≤8,000 tokens passes through unmodified
- [ ] Model version: updating Redis key `ai:config:model_version` is reflected in SK invocations within 60 seconds
- [ ] Model version fallback: if Redis key absent, `appsettings.json` value is used without error
- [ ] All filters registered in correct priority order (verify by checking SK kernel filter chain)

---

## Implementation Checklist

- [ ] Create `CircuitBreakerOpenException` custom exception class
- [ ] Create `CircuitBreakerFilter`: check `ai:cb:open` before `next()`; INCR `ai:cb:failures` on exception; trip to open with exponential backoff TTL on 3rd failure; reset counter on success
- [ ] Create `ILiveAiModelConfig` + `RedisLiveAiModelConfig`: 60-second in-memory volatile cache; Redis read; fallback to `appsettings.json` `AiResilience:DefaultModelVersion`
- [ ] Create `AiModelVersionFilter`: reads `ILiveAiModelConfig`; overrides `OpenAIPromptExecutionSettings.ModelId` in `ctx.Arguments.ExecutionSettings`
- [ ] Create `TokenBudgetFilter`: TiktokenSharp token count after render; chunk delimiter parse + truncation loop; Serilog Warning with dropped chunk IDs
- [ ] Modify `AiExtractionOrchestrator`: catch `CircuitBreakerOpenException`; return `ExtractionResult.ManualFallback(...)`
- [ ] Modify `AiModuleRegistration`: register all three filters with correct priority numbers; register `RedisLiveAiModelConfig` as `ILiveAiModelConfig` singleton
- [ ] Add `AiResilience` config section to `appsettings.json` and validate with FluentValidation on startup
