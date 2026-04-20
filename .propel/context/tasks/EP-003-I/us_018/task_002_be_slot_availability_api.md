# Task - task_002_be_slot_availability_api

## Requirement Reference

- **User Story:** us_018 — Real-Time Slot Availability with Redis Cache
- **Story Location:** `.propel/context/tasks/EP-003-I/us_018/us_018.md`
- **Acceptance Criteria:**
  - AC-1: `GET /api/appointments/slots` returns available slots reflecting confirmed and pending bookings; response data is served from Redis cache with a TTL of ≤5 seconds (NFR-020)
  - AC-2: When a booking or cancellation mutates appointment state, the relevant Redis cache key is invalidated so the next request within 5 seconds returns accurate data
  - AC-3: When Redis is unavailable, the system falls back to a direct PostgreSQL query via EF Core, returns the slot list successfully, and logs a structured Serilog `Warning` event `"SlotCache_Miss"` with `{SpecialtyId}`, `{Date}`, `{Reason}`
  - AC-4: When all time slots for a requested date are booked, the response marks all slots with `isAvailable = false` so the frontend can render the fully-booked state
- **Edge Cases:**
  - Two concurrent booking attempts for the same slot: optimistic locking (`xmin` concurrency token) on `Appointment` row causes the second `SaveChangesAsync()` to throw `DbUpdateConcurrencyException` → handler returns HTTP 409 `{ message: "Slot no longer available" }` (booking write path; invalidation of slot cache still fires to ensure immediate consistency)
  - Stale cache serves an already-booked slot as available: same optimistic locking catches conflict at booking commit; after conflict, booking command handler calls `ISlotCacheService.InvalidateAsync()` to force cache refresh for next request

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

| Layer              | Technology                          | Version    |
| ------------------ | ----------------------------------- | ---------- |
| Backend            | ASP.NET Core Web API                | .NET 9     |
| Backend Messaging  | MediatR                             | 12.x       |
| Backend Validation | FluentValidation                    | 11.x       |
| ORM                | Entity Framework Core               | 9.x        |
| Cache              | Upstash Redis (StackExchange.Redis) | Serverless |
| Logging            | Serilog                             | 4.x        |
| Testing — Unit     | xUnit + Moq                         | 2.x        |
| Database           | PostgreSQL                          | 16+        |
| AI/ML              | N/A                                 | N/A        |
| Mobile             | N/A                                 | N/A        |

> All code and libraries MUST be compatible with versions above.

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

Implement the appointment slot availability read path in the ASP.NET Core .NET 9 Appointment Module. The core endpoint `GET /api/appointments/slots` is backed by a two-tier data strategy:

1. **Redis cache (primary)** — key `slots:{specialtyId}:{date}` with a TTL of 5 seconds (satisfying NFR-020 ≤5s staleness). Served via `ISlotCacheService`.
2. **PostgreSQL fallback** — when Redis is unavailable or the key is absent, the handler queries the `appointments` table via EF Core to compute available slots from the configured time grid minus booked/pending records. A structured Serilog warning is emitted on fallback.

A companion `ISlotCacheService.InvalidateAsync(specialtyId, date)` method is also implemented and wired to the appointment write path (booking and cancellation commands) so that mutations immediately evict the stale cache key, ensuring the next GET reflects reality within the 5-second window (AC-2).

RBAC: `[Authorize(Roles = "Patient")]` — only authenticated Patients may query available slots (NFR-006, OWASP A01).

---

## Dependent Tasks

- **US_006** (EP-001, foundational) — `appointments` table and `Appointment` EF Core entity must exist
- **US_011** (EP-001) — JWT authentication middleware must be active for `[Authorize]` to resolve Patient identity
- **US_014 task_001** (EP-001) — Rate limiting policies (`RateLimitingPolicies`) and `GlobalExceptionFilter` must be registered

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `AppointmentsController` (or extend if exists) | `Server/Modules/Appointment/` |
| CREATE | `GetAvailableSlotsQuery` + `GetAvailableSlotsQueryHandler` | Appointment Module — Application Layer |
| CREATE | `GetAvailableSlotsValidator` (FluentValidation) | Appointment Module — Application Layer |
| CREATE | `ISlotCacheService` + `RedisSlotCacheService` | Appointment Module — Infrastructure Layer |
| CREATE | `SlotDto` + `SlotAvailabilityResponseDto` | Appointment Module — Application Layer |
| CREATE | `SlotConfiguration` (app settings POCO) | `Server/Infrastructure/Configuration/` |
| MODIFY | Appointment booking command handler | Call `ISlotCacheService.InvalidateAsync()` after successful booking |
| MODIFY | Appointment cancellation command handler | Call `ISlotCacheService.InvalidateAsync()` after successful cancellation |
| MODIFY | `Program.cs` | Register `ISlotCacheService`, `RedisSlotCacheService`, new MediatR handlers |

---

## Implementation Plan

1. **`SlotConfiguration`** (app settings POCO, bound from `appsettings.json`):
   ```json
   "SlotConfiguration": {
     "SlotDurationMinutes": 30,
     "BusinessHoursStart": "09:00",
     "BusinessHoursEnd": "17:00"
   }
   ```
   - Used by the query handler to generate the full time-slot grid for a given date, independent of database content
   - Bound via `services.Configure<SlotConfiguration>(config.GetSection("SlotConfiguration"))` in `Program.cs`

2. **`ISlotCacheService`** interface:
   ```csharp
   Task<IReadOnlyList<SlotDto>?> GetAsync(string specialtyId, DateOnly date);
   Task SetAsync(string specialtyId, DateOnly date, IReadOnlyList<SlotDto> slots, TimeSpan ttl);
   Task InvalidateAsync(string specialtyId, DateOnly date);
   ```

3. **`RedisSlotCacheService`** (implements `ISlotCacheService`):
   - Cache key: `$"slots:{specialtyId}:{date:yyyy-MM-dd}"`
   - TTL: `TimeSpan.FromSeconds(5)` (hard-coded to satisfy NFR-020 ≤5s staleness)
   - `GetAsync`: `IDatabase.StringGetAsync(key)` → deserialize JSON → return; on `RedisException` / connection failure → return `null` (signals fallback to caller) + log `Warning` `"SlotCache_Miss"` with `{SpecialtyId}`, `{Date}`, `{Reason}`
   - `SetAsync`: `IDatabase.StringSetAsync(key, json, ttl)` — fire-and-forget (`_ = await` pattern); on `RedisException` → log `Warning` and swallow (non-blocking: failure to cache must not fail the request per NFR-018 graceful degradation)
   - `InvalidateAsync`: `IDatabase.KeyDeleteAsync(key)` — called by write-path handlers

4. **`GetAvailableSlotsQuery`** (MediatR `IRequest<SlotAvailabilityResponseDto>`):
   - Input: `SpecialtyId: Guid`, `Date: DateOnly`
   - Handler steps:
     a. Attempt `ISlotCacheService.GetAsync(specialtyId, date)` — return cached result if non-null
     b. Cache miss or Redis unavailable: query EF Core `DbSet<Appointment>` for the date/specialty — filter `status IN (Booked, Arrived)` (not Cancelled/Completed)
     c. Generate full slot grid from `SlotConfiguration` (loop from BusinessHoursStart to BusinessHoursEnd by SlotDurationMinutes)
     d. Mark each grid slot as `isAvailable = false` if it overlaps with any booked appointment's `(timeSlotStart, timeSlotEnd)`
     e. Store result via `ISlotCacheService.SetAsync(specialtyId, date, slots, TimeSpan.FromSeconds(5))`
     f. Return `SlotAvailabilityResponseDto` (date, specialtyId, list of `SlotDto`)

5. **`GetAvailableSlotsValidator`** (FluentValidation):
   - `SpecialtyId`: `NotEmpty()` — must be a valid non-empty GUID
   - `Date`: `NotEmpty()` — must be today or a future date (`GreaterThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow.Date))`)

6. **`AppointmentsController`** endpoint:
   - `[HttpGet("slots")]` `[Authorize(Roles = "Patient")]`
   - Binds `[FromQuery] GetAvailableSlotsQuery query` → dispatches via MediatR → returns 200 `SlotAvailabilityResponseDto`
   - Input validation handled automatically by `AddFluentValidationAutoValidation` (registered in US_014 task_001); HTTP 400 on validation failure

7. **Cache invalidation wiring** (in existing booking/cancellation handlers):
   - After `SaveChangesAsync()` succeeds in the booking command handler: `await _slotCacheService.InvalidateAsync(appointment.SpecialtyId, DateOnly.FromDateTime(appointment.Date))`
   - Same pattern in cancellation command handler
   - Ensures AC-2 (next request after mutation returns fresh data)

8. **`SlotDto`** shape:
   ```csharp
   record SlotDto(TimeOnly TimeSlotStart, TimeOnly TimeSlotEnd, bool IsAvailable);
   ```

9. **Rate limiting** (reuse US_014 policies):
   - Apply `slots-query` policy (e.g., sliding window 60 requests/minute/IP) to `GET /api/appointments/slots` to prevent scraping abuse (NFR-017)

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no Server/ scaffold yet — greenfield ASP.NET Core project)
```

> Update with actual `Server/` tree after scaffold is complete.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Modules/Appointment/AppointmentsController.cs` | `GET /api/appointments/slots` endpoint |
| CREATE | `Server/Modules/Appointment/Queries/GetAvailableSlotsQuery.cs` | MediatR query input + result type |
| CREATE | `Server/Modules/Appointment/Queries/GetAvailableSlotsQueryHandler.cs` | Redis-first, PostgreSQL fallback handler |
| CREATE | `Server/Modules/Appointment/Validators/GetAvailableSlotsValidator.cs` | FluentValidation: SpecialtyId non-empty, Date ≥ today |
| CREATE | `Server/Modules/Appointment/Dtos/SlotDto.cs` | Read-only slot record: TimeSlotStart, TimeSlotEnd, IsAvailable |
| CREATE | `Server/Modules/Appointment/Dtos/SlotAvailabilityResponseDto.cs` | Response envelope: Date, SpecialtyId, Slots |
| CREATE | `Server/Infrastructure/Cache/ISlotCacheService.cs` | Cache contract: GetAsync, SetAsync, InvalidateAsync |
| CREATE | `Server/Infrastructure/Cache/RedisSlotCacheService.cs` | StackExchange.Redis implementation; Serilog Warning on fallback |
| CREATE | `Server/Infrastructure/Configuration/SlotConfiguration.cs` | POCO for slot duration + business hours; bound from appsettings.json |
| MODIFY | `appsettings.json` | Add `SlotConfiguration` section |
| MODIFY | `Server/Modules/Appointment/Commands/<BookingCommandHandler>.cs` | Call `ISlotCacheService.InvalidateAsync()` after successful booking |
| MODIFY | `Server/Modules/Appointment/Commands/<CancellationCommandHandler>.cs` | Call `ISlotCacheService.InvalidateAsync()` after successful cancellation |
| MODIFY | `Server/Program.cs` | Register `ISlotCacheService`, `RedisSlotCacheService`, `SlotConfiguration` binding, new MediatR handlers |

---

## External References

- [StackExchange.Redis — IDatabase.StringGetAsync / StringSetAsync / KeyDeleteAsync](https://stackexchange.github.io/StackExchange.Redis/Basics.html)
- [Upstash Redis — .NET Connection (StackExchange.Redis + TLS rediss://)](https://upstash.com/docs/redis/howto/connectwithsdks/dotnet)
- [ASP.NET Core — IOptions<T> / Configure<T> for app settings POCOs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-9.0)
- [EF Core 9 — Querying — Where with enum filter](https://learn.microsoft.com/en-us/ef/core/querying/)
- [FluentValidation 11 — GreaterThanOrEqualTo validator](https://docs.fluentvalidation.net/en/latest/built-in-validators.html#greaterthan-greaterthanorequalto-validators)
- [MediatR 12 — Query Handler pattern](https://github.com/jbogard/MediatR/wiki)
- [Serilog — Structured logging with property bags](https://github.com/serilog/serilog/wiki/Writing-Log-Events)
- [NFR-018 — Graceful degradation: external service failure must not block core workflows](../.propel/context/docs/design.md#non-functional-requirements)
- [NFR-020 — Real-time slot availability ≤5s staleness](../.propel/context/docs/design.md#non-functional-requirements)
- [OWASP A01 — Broken Access Control (Authorize by role)](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)

---

## Build Commands

```bash
# Restore packages
dotnet restore

# Build
dotnet build

# Run API
dotnet run --project Server/Server.csproj

# Run unit tests
dotnet test

# Apply EF Core migrations (if any schema changes)
dotnet ef database update --project Server/Server.csproj
```

---

## Implementation Validation Strategy

- [ ] `GET /api/appointments/slots?specialtyId={id}&date={date}` returns HTTP 200 with `SlotAvailabilityResponseDto` (slots array, each with `isAvailable`)
- [ ] Response served from Redis on second request within 5s of first (cache hit verified via Serilog debug log or Redis monitor)
- [ ] Redis unavailable simulation: service returns data from PostgreSQL, Serilog `Warning` with `"SlotCache_Miss"` event logged
- [ ] After booking a slot: subsequent `GET /api/appointments/slots` for same date/specialty returns that slot as `isAvailable = false`
- [ ] `GET /api/appointments/slots` with no/past date → HTTP 400 with FluentValidation error
- [ ] `GET /api/appointments/slots` with non-Patient JWT role → HTTP 403
- [ ] `RedisSlotCacheService.SetAsync` failure does NOT propagate exception (graceful degradation per NFR-018)
- [ ] All slots `isAvailable = false` for a fully booked date → `SlotAvailabilityResponseDto.Slots` all have `IsAvailable = false` (no special status code — UI infers fully-booked from the array)
- [ ] Integration tests pass (if applicable)

---

## Implementation Checklist

- [ ] Create `ISlotCacheService` with `GetAsync`, `SetAsync`, `InvalidateAsync` contracts
- [ ] Implement `RedisSlotCacheService`: cache key `slots:{specialtyId}:{date:yyyy-MM-dd}`, TTL 5s; Redis failures return `null` / swallow + `Serilog.Warning("SlotCache_Miss", ...)`
- [ ] Create `SlotConfiguration` POCO and bind from `appsettings.json` (`SlotDurationMinutes`, `BusinessHoursStart`, `BusinessHoursEnd`)
- [ ] Implement `GetAvailableSlotsQueryHandler`: Redis-first → EF Core fallback → generate grid from `SlotConfiguration` → subtract booked appointments → `SetAsync` result → return DTO
- [ ] Create `GetAvailableSlotsValidator`: `SpecialtyId` not empty, `Date` ≥ today (UTC)
- [ ] Wire `GET /api/appointments/slots` in `AppointmentsController` with `[Authorize(Roles = "Patient")]`
- [ ] Call `ISlotCacheService.InvalidateAsync()` in booking and cancellation command handlers after successful `SaveChangesAsync()`
- [ ] Register `ISlotCacheService` → `RedisSlotCacheService` and `SlotConfiguration` binding in `Program.cs`
