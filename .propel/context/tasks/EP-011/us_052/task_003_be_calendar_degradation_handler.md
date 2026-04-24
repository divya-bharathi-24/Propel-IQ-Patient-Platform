# Task - task_003_be_calendar_degradation_handler

## Requirement Reference

- **User Story:** us_052 — Platform Availability & Graceful Degradation Handlers
- **Story Location:** `.propel/context/tasks/EP-011/us_052/us_052.md`
- **Acceptance Criteria:**
  - AC-3: When OpenAI is unavailable, circuit breaker activates (per US_050); core booking and document upload continue; staff see "AI processing temporarily unavailable — manual review required" message (NFR-018).
  - AC-4: When Google or Outlook Calendar API is unavailable, `CalendarSync.syncStatus = Failed`; appointment booking confirms without blocking; ICS download fallback is offered to the user (NFR-018).
- **Edge Cases:**
  - Planned maintenance window: system banner communicated; booking and intake remain available; AI and calendar sync temporarily suspended with user-facing notices.

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

| Layer   | Technology                  | Version |
| ------- | --------------------------- | ------- |
| Backend | ASP.NET Core Web API / .NET | 9       |
| ORM     | Entity Framework Core       | 9.x     |
| Logging | Serilog                     | 4.x     |

**Note:** All code and libraries MUST be compatible with versions listed above.

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

Implement two degradation handlers that decouple optional enrichment steps from the core booking workflow:

**1. Calendar Sync Degradation Handler** — `ICalendarSyncService` wraps the Google Calendar and Microsoft Graph API calls. On external API failure, the service creates a `CalendarSync` record with `syncStatus = "Failed"`, returns a `CalendarSyncResult.Failed` (not throws), and the booking confirmation proceeds. Separately, `IcsFileGenerator` produces a standards-compliant `.ics` file for download as a fallback for patients/staff whose calendar sync failed.

**2. AI Degradation Handler (integration guard)** — The `AiExtractionOrchestrator` from US_050 already catches `CircuitBreakerOpenException` and returns `ExtractionResult.ManualFallback(...)`. This task adds the **API response mapping** layer: a `DegradationResponseFactory` that translates degradation result types (`ManualFallback`, `CalendarSyncFailed`) into consistent `DegradationNotice` DTOs returned to the frontend. Controllers call `DegradationResponseFactory.BuildNotice(result)` and include the notice in the response body alongside the booking confirmation — ensuring staff see the correct user-facing message.

This task does NOT re-implement the circuit breaker (US_050 task_001). It adds only the response-layer mapping that surfaces degradation reasons to API consumers.

---

## Dependent Tasks

- `EP-010/us_050/task_001_ai_operational_resilience_pipeline.md` — `CircuitBreakerOpenException` + `ExtractionResult.ManualFallback` must exist.
- `CalendarSync` entity must exist (domain model: DR-017 — `id`, `patientId`, `appointmentId`, `provider`, `externalEventId`, `syncStatus`, `syncedAt`; assumed in DB from earlier US).

---

## Impacted Components

| Component                                 | Module         | Action                                                                                                                                           |
| ----------------------------------------- | -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| `ICalendarSyncService` (new)              | Application    | CREATE — `SyncAsync(appointmentId, provider)` → `CalendarSyncResult`; never throws                                                               |
| `GoogleCalendarSyncService` (new)         | Infrastructure | CREATE — wraps Google Calendar API; on exception → `CalendarSync { syncStatus=Failed }` + return `CalendarSyncResult.Failed`                     |
| `MicrosoftGraphCalendarSyncService` (new) | Infrastructure | CREATE — wraps Microsoft Graph calendar API; same pattern                                                                                        |
| `CalendarSyncResult` (new)                | Application    | CREATE — discriminated result: `Synced(externalEventId)` / `Failed(reason)`                                                                      |
| `IcsFileGenerator` (new)                  | Application    | CREATE — generates RFC 5545 `.ics` file content from `Appointment` entity; used as fallback                                                      |
| `DegradationNotice` DTO (new)             | Application    | CREATE — `{ feature, message, fallbackAvailable, fallbackType }`; included in API responses when a feature is degraded                           |
| `DegradationResponseFactory` (new)        | Application    | CREATE — maps `ExtractionResult.ManualFallback` + `CalendarSyncResult.Failed` to `DegradationNotice`; used by booking and extraction controllers |
| `AppointmentsController` (existing)       | API            | MODIFY — include `DegradationNotice[]` in booking confirmation response when calendar sync fails                                                 |

---

## Implementation Plan

1. **`CalendarSyncResult`**:

   ```csharp
   public abstract record CalendarSyncResult
   {
       public sealed record Synced(string ExternalEventId) : CalendarSyncResult;
       public sealed record Failed(string Reason) : CalendarSyncResult;
   }
   ```

2. **`ICalendarSyncService`** + **`GoogleCalendarSyncService`** (Microsoft Graph follows the same pattern):

   ```csharp
   public interface ICalendarSyncService
   {
       Task<CalendarSyncResult> SyncAsync(Guid appointmentId, string provider,
           CancellationToken ct = default);
   }

   public sealed class GoogleCalendarSyncService : ICalendarSyncService
   {
       public async Task<CalendarSyncResult> SyncAsync(Guid appointmentId,
           string provider, CancellationToken ct = default)
       {
           // Create CalendarSync record as "Pending" first
           var calSync = new CalendarSync
           {
               Id = Guid.NewGuid(),
               AppointmentId = appointmentId,
               Provider = provider,
               SyncStatus = "Pending",
               SyncedAt = null
           };
           _context.CalendarSyncs.Add(calSync);
           await _context.SaveChangesAsync(ct);

           try
           {
               string externalEventId = await _googleCalendarClient
                   .CreateEventAsync(appointmentId, ct);

               calSync.SyncStatus = "Synced";
               calSync.ExternalEventId = externalEventId;
               calSync.SyncedAt = DateTimeOffset.UtcNow;
               await _context.SaveChangesAsync(ct);

               return new CalendarSyncResult.Synced(externalEventId);
           }
           catch (Exception ex)
           {
               calSync.SyncStatus = "Failed";
               await _context.SaveChangesAsync(ct);

               Log.Warning(ex,
                   "Google Calendar sync failed for appointment {AppointmentId} — ICS fallback available",
                   appointmentId);

               return new CalendarSyncResult.Failed(
                   "Google Calendar temporarily unavailable. ICS download available.");
           }
       }
   }
   ```

3. **`IcsFileGenerator`** — RFC 5545 `.ics` content builder:

   ```csharp
   public sealed class IcsFileGenerator
   {
       public string Generate(Appointment appointment, string patientName, string specialty)
       {
           // RFC 5545 compliant iCalendar format
           return $"""
               BEGIN:VCALENDAR
               VERSION:2.0
               PRODID:-//PropelIQ//Patient Platform//EN
               BEGIN:VEVENT
               UID:{appointment.Id}@propeliq.health
               DTSTAMP:{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}
               DTSTART:{appointment.TimeSlotStart:yyyyMMddTHHmmssZ}
               DTEND:{appointment.TimeSlotEnd:yyyyMMddTHHmmssZ}
               SUMMARY:Appointment - {specialty}
               DESCRIPTION:Patient: {patientName}
               STATUS:CONFIRMED
               END:VEVENT
               END:VCALENDAR
               """;
       }
   }
   ```

   Controllers call `IcsFileGenerator.Generate(...)` and return it as `File(Encoding.UTF8.GetBytes(ics), "text/calendar", "appointment.ics")` when `CalendarSyncResult.Failed`.

4. **`DegradationNotice`** DTO + **`DegradationResponseFactory`**:

   ```csharp
   public sealed record DegradationNotice(
       string Feature,           // "CalendarSync" | "AiExtraction"
       string Message,           // User-facing message
       bool FallbackAvailable,   // true if ICS download or manual review is available
       string? FallbackType      // "IcsDownload" | "ManualReview" | null
   );

   public static class DegradationResponseFactory
   {
       public static DegradationNotice? FromCalendarSyncResult(CalendarSyncResult result)
           => result is CalendarSyncResult.Failed f
               ? new DegradationNotice("CalendarSync", f.Reason,
                   FallbackAvailable: true, FallbackType: "IcsDownload")
               : null;

       public static DegradationNotice? FromExtractionResult(ExtractionResult result)
           => result.NeedsManualReview && result.FallbackReason is not null
               ? new DegradationNotice("AiExtraction",
                   "AI processing temporarily unavailable — manual review required",
                   FallbackAvailable: true, FallbackType: "ManualReview")
               : null;
   }
   ```

5. **`AppointmentsController` booking endpoint** — include degradation notices in response:

   ```csharp
   // After successful appointment booking:
   var calSyncResult = await _calendarSyncService.SyncAsync(appointment.Id, patient.PreferredCalendar, ct);

   var notices = new List<DegradationNotice>();
   var calNotice = DegradationResponseFactory.FromCalendarSyncResult(calSyncResult);
   if (calNotice is not null) notices.Add(calNotice);

   return Ok(new BookAppointmentResponse
   {
       AppointmentId = appointment.Id,
       Status = "Confirmed",
       IcsDownloadAvailable = calSyncResult is CalendarSyncResult.Failed,
       DegradationNotices = notices   // Empty list when all external services healthy
   });
   ```

---

## Current Project State

```
Server/
  Application/
    Calendar/                           ← create new folder
    Degradation/                        ← create new folder
  Infrastructure/
    Calendar/                           ← create new folder
  API/
    Controllers/
      AppointmentsController.cs         ← EXISTS — MODIFY
```

---

## Expected Changes

| Action | File Path                                                             | Description                                                                                                   |
| ------ | --------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| CREATE | `Server/Application/Calendar/ICalendarSyncService.cs`                 | Interface: `SyncAsync()` → `CalendarSyncResult`                                                               |
| CREATE | `Server/Application/Calendar/CalendarSyncResult.cs`                   | Abstract record: `Synced(externalEventId)` + `Failed(reason)`                                                 |
| CREATE | `Server/Application/Calendar/IcsFileGenerator.cs`                     | RFC 5545 `.ics` content generator from `Appointment` entity                                                   |
| CREATE | `Server/Application/Degradation/DegradationNotice.cs`                 | DTO record: `Feature`, `Message`, `FallbackAvailable`, `FallbackType`                                         |
| CREATE | `Server/Application/Degradation/DegradationResponseFactory.cs`        | Static factory: maps `CalendarSyncResult.Failed` and `ExtractionResult.ManualFallback` to `DegradationNotice` |
| CREATE | `Server/Infrastructure/Calendar/GoogleCalendarSyncService.cs`         | `ICalendarSyncService`: wraps Google Calendar API; persists `CalendarSync`; `Failed` on exception             |
| CREATE | `Server/Infrastructure/Calendar/MicrosoftGraphCalendarSyncService.cs` | `ICalendarSyncService`: wraps Microsoft Graph; same pattern as Google                                         |
| MODIFY | `Server/API/Controllers/AppointmentsController.cs`                    | Include `DegradationNotices` in booking response; `IcsDownloadAvailable` flag                                 |

---

## External References

- [RFC 5545 — iCalendar Format](https://datatracker.ietf.org/doc/html/rfc5545) — `VCALENDAR` / `VEVENT` structure for `.ics` file generation
- [DR-017 (design.md)](../../../docs/design.md) — `CalendarSync` entity: `provider`, `externalEventId`, `syncStatus (Synced/Failed/Revoked)`, `syncedAt`
- [NFR-018 (design.md)](../../../docs/design.md) — Calendar API unavailability must not block appointment confirmation
- [AG-6 (design.md)](../../../docs/design.md) — External service failures degrade functionality without blocking core booking workflows
- [US_050 task_001](../../../EP-010/us_050/task_001_ai_operational_resilience_pipeline.md) — `CircuitBreakerOpenException` + `ExtractionResult.ManualFallback` pattern

---

## Build Commands

- Refer to [`.propel/build/`](../../../../build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [ ] When Google Calendar API throws, `CalendarSync.syncStatus = "Failed"` persisted in DB; booking confirmation response returns HTTP 200 (not 500)
- [ ] Booking confirmation response includes `"icsDownloadAvailable": true` when calendar sync fails
- [ ] `DegradationNotices` array in response is empty when all external services are healthy
- [ ] `IcsFileGenerator.Generate(...)` produces valid RFC 5545 content (contains `BEGIN:VCALENDAR`, `BEGIN:VEVENT`, `DTSTART`, `DTEND`, `UID`)
- [ ] `DegradationResponseFactory.FromExtractionResult(ExtractionResult.ManualFallback(...))` returns non-null `DegradationNotice` with `FallbackType = "ManualReview"`

---

## Implementation Checklist

- [x] Create `CalendarSyncResult` abstract record: `Synced(externalEventId)` and `Failed(reason)` subtypes
- [x] Create `ICalendarSyncService` interface; create `GoogleCalendarSyncService` + `MicrosoftGraphCalendarSyncService`: persist `CalendarSync` as `Pending` first; update to `Synced`/`Failed` on outcome; catch → `Failed` + Serilog Warning
- [x] Create `IcsFileGenerator`: RFC 5545 compliant `VCALENDAR` + `VEVENT`; `DTSTART`/`DTEND` from `Appointment.TimeSlotStart`/`TimeSlotEnd`; `UID` = `{appointment.Id}@propeliq.health` — leverages existing `IcsGenerationService` (RFC 5545 via Ical.Net); ICS download endpoint `GET /api/appointments/{id}/ics` already exists
- [x] Create `DegradationNotice` DTO + `DegradationResponseFactory` static class mapping `CalendarSyncResult.Failed` → `DegradationNotice` and `ExtractionResult.ManualFallback` → `DegradationNotice`
- [x] Modify `BookingController` booking endpoint: call `ICalendarSyncService.SyncAsync` after booking; build `DegradationNotices` list; include `IcsDownloadAvailable` flag in `BookAppointmentResponse`
- [x] Register `GoogleCalendarSyncService` and `MicrosoftGraphCalendarSyncService` as keyed scoped `ICalendarSyncService` in `Program.cs` (keyed: "Google" / "Outlook")
