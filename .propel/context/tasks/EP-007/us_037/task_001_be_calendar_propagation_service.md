# Task - TASK_001

## Requirement Reference

- **User Story**: US_037 — Calendar Event Update & Removal on Reschedule/Cancel
- **Story Location**: `.propel/context/tasks/EP-007/us_037/us_037.md`
- **Acceptance Criteria**:
  - AC-1: Reschedule → PATCH Google (`/calendars/primary/events/{eventId}`) or Outlook (`/me/events/{id}`) with new date/time → `CalendarSync.syncStatus = Synced`.
  - AC-2: Cancel → DELETE Google or Outlook event → `CalendarSync.syncStatus = Revoked`.
  - AC-3: Calendar API failure → `CalendarSync.syncStatus = Failed`, retry queued for 10 minutes, appointment status change proceeds regardless (non-blocking).
  - AC-4: No `CalendarSync` record exists → no API call attempted; change proceeds normally.
- **Edge Cases**:
  - EC-1: OAuth token expired → token refresh attempted via provider refresh endpoint; if refresh fails → `CalendarSync.syncStatus = Failed`, patient prompted to reconnect.
  - EC-2: Batch cancellations → individual calls queued and processed asynchronously; rate limits respected per provider.

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

| Layer               | Technology                   | Version  |
|---------------------|------------------------------|----------|
| Backend             | ASP.NET Core Web API         | .net 10   |
| Calendar - Google   | Google Calendar API          | v3       |
| Calendar - Outlook  | Microsoft Graph API          | v1.0     |
| Auth                | OAuth 2.0 (token refresh)    | —        |
| ORM                 | Entity Framework Core        | 9.x      |
| Database            | PostgreSQL                   | 16+      |
| Messaging           | MediatR                      | 12.x     |
| Logging             | Serilog                      | 4.x      |
| AI/ML               | N/A                          | N/A      |
| Mobile              | N/A                          | N/A      |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type       | Value |
|----------------------|-------|
| **AI Impact**        | No    |
| **AIR Requirements** | N/A   |
| **AI Pattern**       | N/A   |
| **Prompt Template**  | N/A   |
| **Guardrails Config**| N/A   |
| **Model Provider**   | N/A   |

## Mobile References (Mobile Tasks Only)

| Reference Type      | Value |
|---------------------|-------|
| **Mobile Impact**   | No    |
| **Platform Target** | N/A   |
| **Min OS Version**  | N/A   |
| **Mobile Framework**| N/A   |

## Task Overview

Implement `ICalendarPropagationService` and its concrete implementation `CalendarPropagationService` that handles the full lifecycle of calendar event changes triggered by appointment reschedule and cancellation. The service routes to the correct provider adapter (`IGoogleCalendarAdapter` / `IOutlookCalendarAdapter`) based on the `CalendarSync.provider` field, executes PATCH (update) or DELETE operations against the respective external API, handles OAuth token expiry with a silent refresh attempt (EC-1), and updates `CalendarSync.syncStatus` to `Synced`, `Revoked`, or `Failed` based on the outcome. All operations are fire-and-forget (non-blocking to the appointment change flow), and failures set a `retryAt` timestamp for the retry processor in task_002 (AC-3, NFR-018).

## Dependent Tasks

- **task_003_db_calendarsync_retry_migration.md** — `CalendarSync.retryAt` column must exist before this service can set retry timestamps.

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `ICalendarPropagationService` | `PropelIQ.Appointment` | CREATE |
| `CalendarPropagationService` | `PropelIQ.Appointment` | CREATE |
| `IGoogleCalendarAdapter` | `PropelIQ.Appointment` | CREATE |
| `GoogleCalendarAdapter` | `PropelIQ.Appointment` | CREATE |
| `IOutlookCalendarAdapter` | `PropelIQ.Appointment` | CREATE |
| `OutlookCalendarAdapter` | `PropelIQ.Appointment` | CREATE |
| `IOAuthTokenService` | `PropelIQ.Appointment` | CREATE |
| `ICalendarSyncRepository` | `PropelIQ.Appointment` | CREATE |
| `CalendarSyncRepository` | `PropelIQ.Infrastructure` | CREATE |
| `Program.cs` / DI registration | `PropelIQ.Api` | MODIFY |

## Implementation Plan

1. **Define `ICalendarPropagationService`** with two methods: `PropagateUpdateAsync(Guid appointmentId, CancellationToken ct)` and `PropagateDeleteAsync(Guid appointmentId, CancellationToken ct)`. Both return `Task` (fire-and-forget callers do not await the result).
2. **Implement provider routing in `CalendarPropagationService`**:
   - Query `ICalendarSyncRepository.GetActiveByAppointmentIdAsync(appointmentId)` — returns the `CalendarSync` record or null.
   - If null (AC-4): log info and return immediately — no API call.
   - If record exists: branch on `record.Provider` to call `IGoogleCalendarAdapter` or `IOutlookCalendarAdapter`.
3. **Implement `GoogleCalendarAdapter.UpdateEventAsync(externalEventId, AppointmentEventPayload)`** — issues `PATCH https://www.googleapis.com/calendar/v3/calendars/primary/events/{eventId}` with updated start/end datetime and title. Uses `Google.Apis.Calendar.v3` SDK.
4. **Implement `GoogleCalendarAdapter.DeleteEventAsync(externalEventId)`** — issues `DELETE https://www.googleapis.com/calendar/v3/calendars/primary/events/{eventId}`.
5. **Implement `OutlookCalendarAdapter.UpdateEventAsync` / `DeleteEventAsync`** — issues `PATCH /v1.0/me/events/{id}` and `DELETE /v1.0/me/events/{id}` via Microsoft Graph SDK (`Microsoft.Graph`).
6. **OAuth token refresh in `IOAuthTokenService`** — before each adapter call, attempt silent token refresh using the stored refresh token. If `HttpStatusCode.Unauthorized` (401) is returned from the API, retry once after refresh. If refresh fails or second attempt is 401, mark `CalendarSync.syncStatus = Failed` and emit a domain event `CalendarSyncAuthFailedEvent` for the patient reconnect notification (EC-1).
7. **Error handling and `retryAt` assignment** — on any non-auth API failure (5xx, timeout), set `syncStatus = Failed` and `retryAt = UtcNow.AddMinutes(10)` (AC-3). Appointment change is not blocked — this runs asynchronously after the appointment command succeeds.
8. **Structured logging** — log provider, appointmentId, externalEventId, outcome, and HTTP status code with Serilog correlation ID (TR-018, NFR-009).

### Pseudocode

```csharp
// CalendarPropagationService.cs
public class CalendarPropagationService(
    ICalendarSyncRepository calendarSyncRepo,
    IGoogleCalendarAdapter googleAdapter,
    IOutlookCalendarAdapter outlookAdapter,
    IOAuthTokenService tokenService,
    ILogger<CalendarPropagationService> logger) : ICalendarPropagationService
{
    public async Task PropagateUpdateAsync(Guid appointmentId, CancellationToken ct)
    {
        var sync = await calendarSyncRepo.GetActiveByAppointmentIdAsync(appointmentId, ct);
        if (sync is null) return; // AC-4 — no sync record, skip

        var payload = await BuildEventPayloadAsync(appointmentId, ct);

        await ExecuteWithRetryOnAuthFailureAsync(sync, async (accessToken) =>
            sync.Provider == CalendarProvider.Google
                ? await googleAdapter.UpdateEventAsync(sync.ExternalEventId, payload, accessToken, ct)
                : await outlookAdapter.UpdateEventAsync(sync.ExternalEventId, payload, accessToken, ct),
            sync, ct);
    }

    public async Task PropagateDeleteAsync(Guid appointmentId, CancellationToken ct)
    {
        var sync = await calendarSyncRepo.GetActiveByAppointmentIdAsync(appointmentId, ct);
        if (sync is null) return; // AC-4

        await ExecuteWithRetryOnAuthFailureAsync(sync, async (accessToken) =>
            sync.Provider == CalendarProvider.Google
                ? await googleAdapter.DeleteEventAsync(sync.ExternalEventId, accessToken, ct)
                : await outlookAdapter.DeleteEventAsync(sync.ExternalEventId, accessToken, ct),
            sync, ct);
    }

    private async Task ExecuteWithRetryOnAuthFailureAsync(
        CalendarSync sync,
        Func<string, Task<CalendarApiResult>> apiCall,
        CalendarSync record,
        CancellationToken ct)
    {
        var token = await tokenService.GetAccessTokenAsync(record.PatientId, record.Provider, ct);
        var result = await apiCall(token);

        if (result.IsUnauthorized)
        {
            // EC-1: attempt token refresh
            token = await tokenService.RefreshTokenAsync(record.PatientId, record.Provider, ct);
            if (token is null)
            {
                // Refresh failed — mark Failed, trigger reconnect prompt
                await UpdateSyncStatusAsync(record, CalendarSyncStatus.Failed, null, ct);
                return;
            }
            result = await apiCall(token);
        }

        if (result.IsSuccess)
        {
            var newStatus = result.WasDelete ? CalendarSyncStatus.Revoked : CalendarSyncStatus.Synced;
            await UpdateSyncStatusAsync(record, newStatus, retryAt: null, ct);
        }
        else
        {
            // AC-3: non-auth failure — queue retry in 10 minutes
            await UpdateSyncStatusAsync(record, CalendarSyncStatus.Failed,
                retryAt: DateTime.UtcNow.AddMinutes(10), ct);
        }
    }
}
```

## Current Project State

```
Server/
├── PropelIQ.Appointment/
│   ├── Services/
│   │   └── (no calendar propagation service yet)
│   └── Adapters/
│       └── (no calendar adapters yet)
├── PropelIQ.Infrastructure/
│   └── Repositories/
│       └── (no CalendarSyncRepository yet)
└── PropelIQ.Api/
    └── Program.cs
```

> Placeholder — update with actual paths as dependent tasks complete.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/PropelIQ.Appointment/Services/ICalendarPropagationService.cs` | Service interface |
| CREATE | `Server/PropelIQ.Appointment/Services/CalendarPropagationService.cs` | Routing + error handling orchestrator |
| CREATE | `Server/PropelIQ.Appointment/Adapters/IGoogleCalendarAdapter.cs` | Google adapter interface |
| CREATE | `Server/PropelIQ.Appointment/Adapters/GoogleCalendarAdapter.cs` | Google Calendar API v3 PATCH/DELETE |
| CREATE | `Server/PropelIQ.Appointment/Adapters/IOutlookCalendarAdapter.cs` | Outlook adapter interface |
| CREATE | `Server/PropelIQ.Appointment/Adapters/OutlookCalendarAdapter.cs` | Microsoft Graph API v1.0 PATCH/DELETE |
| CREATE | `Server/PropelIQ.Appointment/Services/IOAuthTokenService.cs` | OAuth token fetch + silent refresh interface |
| CREATE | `Server/PropelIQ.Appointment/Repositories/ICalendarSyncRepository.cs` | CalendarSync query/update interface |
| CREATE | `Server/PropelIQ.Infrastructure/Repositories/CalendarSyncRepository.cs` | EF Core implementation |
| MODIFY | `Server/PropelIQ.Api/Program.cs` | Register adapters, service, and repository in DI |

## External References

- [Google Calendar API v3 — Events: patch](https://developers.google.com/calendar/api/v3/reference/events/patch)
- [Google Calendar API v3 — Events: delete](https://developers.google.com/calendar/api/v3/reference/events/delete)
- [Google .NET API client library — Calendar v3](https://developers.google.com/api-client-library/dotnet/apis/calendar/v3)
- [Microsoft Graph API v1.0 — Update event](https://learn.microsoft.com/en-us/graph/api/event-update?view=graph-rest-1.0)
- [Microsoft Graph API v1.0 — Delete event](https://learn.microsoft.com/en-us/graph/api/event-delete?view=graph-rest-1.0)
- [Microsoft Graph .NET SDK](https://github.com/microsoftgraph/msgraph-sdk-dotnet)
- [OAuth 2.0 token refresh — Google](https://developers.google.com/identity/protocols/oauth2/web-server#offline)
- [OAuth 2.0 token refresh — Microsoft](https://learn.microsoft.com/en-us/azure/active-directory/develop/v2-oauth2-auth-code-flow#refresh-the-access-token)
- [NFR-018 graceful degradation](../docs/design.md)

## Build Commands

```bash
cd Server

# Add NuGet packages
dotnet add PropelIQ.Appointment package Google.Apis.Calendar.v3
dotnet add PropelIQ.Appointment package Microsoft.Graph

# Restore & build
dotnet restore
dotnet build PropelIQ.sln
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] `PropagateUpdateAsync` with valid `CalendarSync(Google)` calls Google PATCH and sets `syncStatus = Synced`
- [ ] `PropagateUpdateAsync` with valid `CalendarSync(Outlook)` calls Outlook PATCH and sets `syncStatus = Synced`
- [ ] `PropagateDeleteAsync` with valid `CalendarSync` calls DELETE and sets `syncStatus = Revoked`
- [ ] No `CalendarSync` record → no adapter call made, method returns without exception (AC-4)
- [ ] 401 from API triggers token refresh; second call succeeds → `syncStatus = Synced`
- [ ] 401 from API + refresh fails → `syncStatus = Failed`, no exception thrown (EC-1)
- [ ] Non-auth API failure → `syncStatus = Failed`, `retryAt = UtcNow + 10 min` (AC-3)
- [ ] Appointment change is non-blocking: service method returns without awaiting API call from calling handler

## Implementation Checklist

- [ ] Create `ICalendarPropagationService` with `PropagateUpdateAsync` and `PropagateDeleteAsync`
- [ ] Implement `CalendarPropagationService` with provider routing (Google vs Outlook) based on `CalendarSync.Provider`
- [ ] Implement `GoogleCalendarAdapter` — PATCH `/calendars/primary/events/{eventId}` and DELETE using Google.Apis.Calendar.v3 SDK
- [ ] Implement `OutlookCalendarAdapter` — PATCH and DELETE `/v1.0/me/events/{id}` using Microsoft.Graph SDK
- [ ] Implement `IOAuthTokenService` — get access token from stored credentials; silent refresh on 401; return null if refresh fails (EC-1)
- [ ] On API failure: set `syncStatus = Failed`, `retryAt = UtcNow + 10 min` (AC-3) — never throw or block the appointment change flow (NFR-018)
- [ ] Create `ICalendarSyncRepository` with `GetActiveByAppointmentIdAsync` and `UpdateStatusAsync` methods
- [ ] Load OAuth client credentials from `IConfiguration` — never hardcode (OWASP A02)
