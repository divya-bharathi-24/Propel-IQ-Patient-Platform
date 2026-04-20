# Task - task_002_be_google_calendar_oauth_sync

## Requirement Reference

- **User Story:** us_035 — Google Calendar OAuth 2.0 Appointment Sync
- **Story Location:** `.propel/context/tasks/EP-007/us_035/us_035.md`
- **Acceptance Criteria:**
  - AC-1: `GET /api/calendar/google/auth?appointmentId={id}` generates a Google OAuth 2.0 authorization URL (PKCE + state anti-CSRF) and redirects the patient to Google's consent screen
  - AC-2: `GET /api/calendar/google/callback` exchanges the authorization code for tokens, stores them encrypted, creates a Google Calendar event with full appointment details, stores `CalendarSync(provider=Google, externalEventId, syncStatus=Synced)`, redirects FE to `?calendarResult=success`
  - AC-3: Patient denies OAuth → Google returns `error=access_denied` → callback redirects FE to `?calendarResult=declined` — no CalendarSync record created
  - AC-4: Google Calendar API call fails → CalendarSync stored with `syncStatus=Failed`, `retryScheduledAt = UtcNow + 10 min`; `CalendarSyncRetryBackgroundService` retries after 10 minutes; `GET /api/appointments/{id}/ics` generates ICS download fallback
- **Edge Cases:**
  - Expired access token: `GoogleCalendarService` catches HTTP 401 from Google API → calls token refresh via refresh token → retries the event call; if refresh also fails → throws `GoogleTokenExpiredException` → CalendarSync `syncStatus=Revoked`, redirects FE to `?calendarResult=expired`
  - Duplicate sync: `CalendarSync` record already exists for `(patientId, appointmentId, provider=Google)` with `syncStatus=Synced` → `UpdateEvent` (PATCH) instead of `InsertEvent` (POST)

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
| Backend            | ASP.NET Core Web API  | .net 10  |
| Backend Messaging  | MediatR               | 12.x    |
| ORM                | Entity Framework Core | 9.x     |
| Logging            | Serilog               | 4.x     |
| Token Security     | ASP.NET Core Data Protection API | .net 10 |
| Background Jobs    | .NET BackgroundService (IHostedService) | .net 10 |
| Google Calendar    | Google.Apis.Calendar.v3 NuGet | latest stable |
| ICS Generation     | Ical.Net              | 4.x     |
| Testing — Unit     | xUnit + Moq           | 2.x     |
| Database           | PostgreSQL            | 16+     |
| AI/ML              | N/A                   | N/A     |
| Mobile             | N/A                   | N/A     |

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

Implement the ASP.NET Core .net 10 Google Calendar OAuth 2.0 sync backend within the Calendar module (`Server/Modules/Calendar/`). Four capabilities are required:

1. **OAuth flow initiation** — `GET /api/calendar/google/auth` builds the Google authorization URL with PKCE verifier and anti-CSRF `state` parameter, stores `state` in Redis (10-min TTL), and redirects to Google
2. **OAuth callback handler** — `GET /api/calendar/google/callback` validates `state`, exchanges code for tokens, encrypts and stores tokens via `ASP.NET Core Data Protection API`, creates/updates the Google Calendar event, upserts `CalendarSync`, redirects FE
3. **`GoogleCalendarService`** — wraps `Google.Apis.Calendar.v3`, handles event create/update/delete, auto-refresh on 401, throws `GoogleTokenExpiredException` on failed refresh
4. **`CalendarSyncRetryBackgroundService`** — `BackgroundService` polling every 5 minutes for `CalendarSync WHERE syncStatus = 'Failed' AND retryScheduledAt <= UtcNow`, re-executing sync for each
5. **ICS fallback** — `GET /api/appointments/{id}/ics` generates RFC 5545 ICS file using `Ical.Net` with all required event fields (FR-036)

All endpoints decorated `[Authorize(Roles = "Patient")]`; `patientId` always from `ClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier)` (OWASP A01). `GOOGLE_CLIENT_SECRET` from environment variable only (OWASP A02).

---

## Dependent Tasks

- **EP-007/us_035 task_003_db_calendar_sync_schema** — `PatientOAuthToken` table (encrypted tokens) and `calendar_syncs.event_link` + `retry_scheduled_at` columns must be migrated before this handler can persist OAuth state
- **US_008 (Foundational)** — `CalendarSync` entity must exist
- **US_011 (EP-001)** — JWT middleware must be active; Patient-role claims required
- **US_019 (EP-003)** — `Appointment` entity with specialty, date, timeSlot fields must exist

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `GoogleCalendarController` | `Server/Modules/Calendar/GoogleCalendarController.cs` |
| CREATE | `InitiateGoogleSyncCommand` + `InitiateGoogleSyncCommandHandler` | Calendar Module — Commands |
| CREATE | `HandleGoogleCallbackCommand` + `HandleGoogleCallbackCommandHandler` | Calendar Module — Commands |
| CREATE | `GetCalendarSyncStatusQuery` + `GetCalendarSyncStatusQueryHandler` | Calendar Module — Queries |
| CREATE | `GoogleCalendarService` (wraps `Google.Apis.Calendar.v3`) | `Server/Modules/Calendar/Services/GoogleCalendarService.cs` |
| CREATE | `IGoogleCalendarService` interface | `Server/Modules/Calendar/Interfaces/IGoogleCalendarService.cs` |
| CREATE | `IcsGenerationService` (wraps `Ical.Net`) | `Server/Modules/Calendar/Services/IcsGenerationService.cs` |
| CREATE | `CalendarSyncRetryBackgroundService` | `Server/Modules/Calendar/BackgroundServices/CalendarSyncRetryBackgroundService.cs` |
| CREATE | `GoogleTokenExpiredException` + `CalendarSyncFailedException` | `Server/Common/Exceptions/` |
| CREATE | `GoogleCalendarSettings` options class | `Server/Modules/Calendar/Options/GoogleCalendarSettings.cs` |
| MODIFY | `Program.cs` | Register services, MediatR handlers, `CalendarSyncRetryBackgroundService`, Data Protection key ring |

---

## Implementation Plan

1. **`GoogleCalendarSettings`** (bound from `appsettings.json` section `"GoogleCalendar"`):
   ```csharp
   public class GoogleCalendarSettings
   {
       public string ClientId { get; set; } = "";          // from appsettings, non-secret
       public string RedirectUri { get; set; } = "";       // e.g. "https://api.example.com/api/calendar/google/callback"
       public string FrontendConfirmationUrl { get; set; } = ""; // FE redirect base
   }
   // GOOGLE_CLIENT_SECRET loaded from Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET") only
   ```

2. **`InitiateGoogleSyncCommandHandler`**:
   - Validate `appointmentId` belongs to requesting `patientId` (OWASP A01)
   - Generate PKCE: `code_verifier = RandomBase64Url(32)`, `code_challenge = Base64UrlEncode(SHA256(code_verifier))`
   - Generate `state = $"{Guid.NewGuid()}:{appointmentId}"` — composite anti-CSRF + context
   - Store `state → (code_verifier, patientId, appointmentId)` in Redis with 10-min TTL
   - Build Google OAuth URL:
     ```
     https://accounts.google.com/o/oauth2/v2/auth
       ?client_id={ClientId}
       &redirect_uri={RedirectUri}
       &response_type=code
       &scope=https://www.googleapis.com/auth/calendar.events
       &access_type=offline&prompt=consent
       &state={state}
       &code_challenge={code_challenge}
       &code_challenge_method=S256
     ```
   - Return `302 Location: {authUrl}` from controller

3. **`HandleGoogleCallbackCommandHandler`**:
   - Input: `code`, `state`, `error` (nullable — populated if user declined)
   - If `error = "access_denied"` → redirect FE to `?calendarResult=declined` (AC-3)
   - Validate `state` exists in Redis; extract `(code_verifier, patientId, appointmentId)`; delete from Redis after read (one-time use — OWASP A07)
   - Exchange code for tokens: POST to `https://oauth2.googleapis.com/token` with `code_verifier`
   - Encrypt `access_token` + `refresh_token` via `IDataProtector.Protect()` (ASP.NET Core Data Protection — AES-256, NFR-004)
   - Upsert `PatientOAuthToken(patientId, provider=Google, encryptedAccessToken, encryptedRefreshToken, expiresAt)`
   - Load appointment details; call `_googleCalendarService.CreateOrUpdateEventAsync()`
   - On success: upsert `CalendarSync(syncStatus=Synced, externalEventId, eventLink, syncedAt=UtcNow)` → redirect FE to `?calendarResult=success&appointmentId={id}`
   - On `HttpRequestException` (Google API unavailable): upsert `CalendarSync(syncStatus=Failed, retryScheduledAt=UtcNow+10min)` → redirect FE to `?calendarResult=failed&appointmentId={id}` (AC-4)
   - On `GoogleTokenExpiredException`: upsert `CalendarSync(syncStatus=Revoked)` → redirect FE to `?calendarResult=expired&appointmentId={id}`
   - AuditLog: `action = "GoogleCalendarSynced"` or `"GoogleCalendarSyncFailed"` with `patientId`, `appointmentId`

4. **`GoogleCalendarService.CreateOrUpdateEventAsync()`**:
   - Decrypt tokens via `IDataProtector.Unprotect()`
   - Build `Google.Apis.Calendar.v3.Data.Event` with (FR-036):
     - `Summary`: `"Appointment: {appointmentType} — {specialtyName}"`
     - `Start`/`End`: `EventDateTime` with `DateTime` and `TimeZone`
     - `Location`: clinic name
     - `Description`: `"Provider: {specialty}\nBooking Ref: {appointmentId}\nClinic: {clinicName}"`
   - Check for existing `CalendarSync.externalEventId` → if exists: `Events.Patch()` (update); if not: `Events.Insert("primary")`
   - On `Google.GoogleApiException` with `HttpStatusCode.Unauthorized`: call `RefreshTokenAsync()` → retry once → if still 401 throw `GoogleTokenExpiredException`

5. **`IcsGenerationService`** (`Ical.Net`):
   - `GenerateIcs(Appointment appointment): byte[]`
   - Creates `Calendar` with `VEvent`: `DtStart`, `DtEnd`, `Summary`, `Location`, `Description` (matching FR-036 fields)
   - `Content-Type: text/calendar; charset=utf-8`, `Content-Disposition: attachment; filename="appointment-{id}.ics"`

6. **`CalendarSyncRetryBackgroundService`** (`BackgroundService`):
   - `PeriodicTimer` every 5 minutes
   - Query: `calendarSyncs WHERE syncStatus = 'Failed' AND retryScheduledAt <= UtcNow` (using `IServiceScopeFactory`)
   - For each: dispatch `HandleGoogleCallbackCommand` in retry mode (re-use stored tokens, skip OAuth exchange step)
   - Max retries: 3 (stored as `retryCount` on `CalendarSync`); after 3 → `syncStatus = 'PermanentFailed'`, log `Serilog.Error`

7. **`GET /api/calendar/google/status/{appointmentId}`** (`GetCalendarSyncStatusQueryHandler`):
   - Returns `{ SyncStatus, EventLink, Provider, SyncedAt }` or `null` if no record
   - `[Authorize(Roles = "Patient")]` — validates `CalendarSync.patientId == requestingPatientId` (OWASP A01)

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no Server/ scaffold yet — greenfield ASP.NET Core project)
```

> Update with actual `Server/Modules/Calendar/` tree after scaffold is complete.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Modules/Calendar/GoogleCalendarController.cs` | Auth initiation (redirect), callback, status query, ICS download — all `[Authorize(Roles="Patient")]` |
| CREATE | `Server/Modules/Calendar/Commands/InitiateGoogleSyncCommand.cs` | PKCE + state generation, Redis storage, OAuth URL build, 302 redirect |
| CREATE | `Server/Modules/Calendar/Commands/HandleGoogleCallbackCommand.cs` | Code exchange, token encryption, event create/update, CalendarSync UPSERT, FE redirect |
| CREATE | `Server/Modules/Calendar/Queries/GetCalendarSyncStatusQuery.cs` | Returns `CalendarSyncStatusDto` for given `appointmentId` |
| CREATE | `Server/Modules/Calendar/Services/GoogleCalendarService.cs` | `Google.Apis.Calendar.v3` wrapper; auto-refresh; `CreateOrUpdateEventAsync`, `DeleteEventAsync` |
| CREATE | `Server/Modules/Calendar/Services/IcsGenerationService.cs` | `Ical.Net` ICS generation; returns `byte[]`; FR-036 fields populated |
| CREATE | `Server/Modules/Calendar/BackgroundServices/CalendarSyncRetryBackgroundService.cs` | `BackgroundService`; 5-min `PeriodicTimer`; retries Failed CalendarSync records; max 3 retries |
| CREATE | `Server/Modules/Calendar/Options/GoogleCalendarSettings.cs` | `ClientId`, `RedirectUri`, `FrontendConfirmationUrl` (non-secrets only) |
| CREATE | `Server/Common/Exceptions/GoogleTokenExpiredException.cs` | Thrown when refresh token is also invalid; maps to `CalendarSync.syncStatus = Revoked` |
| MODIFY | `appsettings.json` | Add `"GoogleCalendar"` section (non-secret values only: `ClientId`, `RedirectUri`, `FrontendConfirmationUrl`) |
| MODIFY | `Server/Program.cs` | Register `IGoogleCalendarService`, `IcsGenerationService`, `CalendarSyncRetryBackgroundService`, MediatR handlers, Data Protection key ring (Redis persistence in production) |

---

## External References

- [Google Calendar API v3 — Events: insert](https://developers.google.com/calendar/api/v3/reference/events/insert)
- [Google Calendar API v3 — Events: patch](https://developers.google.com/calendar/api/v3/reference/events/patch)
- [Google OAuth 2.0 — PKCE for Web Server Applications](https://developers.google.com/identity/protocols/oauth2/web-server#creatingclient)
- [Google.Apis.Calendar.v3 NuGet package](https://www.nuget.org/packages/Google.Apis.Calendar.v3)
- [ASP.NET Core Data Protection API — IDataProtector.Protect / Unprotect](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/using-data-protection)
- [Ical.Net 4.x — Calendar / VEvent generation](https://github.com/rianjs/ical.net)
- [.net 10 BackgroundService + PeriodicTimer](https://learn.microsoft.com/en-us/dotnet/core/extensions/timer-service)
- [OWASP A01 — Broken Access Control: `patientId` from JWT; ownership check on `CalendarSync`](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)
- [OWASP A02 — Cryptographic Failures: `GOOGLE_CLIENT_SECRET` from env var; tokens encrypted at rest](https://owasp.org/Top10/A02_2021-Cryptographic_Failures/)
- [OWASP A07 — OAuth state parameter as CSRF guard; one-time Redis consumption](https://owasp.org/Top10/A07_2021-Identification_and_Authentication_Failures/)

---

## Build Commands

```bash
# Restore packages
dotnet restore

# Build
dotnet build

# Run API (ensure GOOGLE_CLIENT_SECRET env var is set)
GOOGLE_CLIENT_SECRET=... dotnet run --project Server/Server.csproj

# Run unit tests
dotnet test
```

---

## Implementation Validation Strategy

- [ ] `GET /api/calendar/google/auth?appointmentId={id}` with valid Patient JWT → 302 redirect to `https://accounts.google.com/o/oauth2/v2/auth` with `code_challenge`, `state`, `scope=calendar.events`
- [ ] `GET /api/calendar/google/auth` with another patient's `appointmentId` → HTTP 403
- [ ] `GET /api/calendar/google/callback?error=access_denied&state={state}` → FE redirect to `?calendarResult=declined` (no CalendarSync created)
- [ ] `GET /api/calendar/google/callback` with valid code + state → tokens encrypted in `PatientOAuthToken`; `CalendarSync(syncStatus=Synced)` created; FE redirect to `?calendarResult=success`
- [ ] Duplicate sync: second `HandleGoogleCallbackCommandHandler` execution → Google `Events.Patch()` called (not `Events.Insert`); no duplicate `CalendarSync` row
- [ ] Google API HTTP 503 → `CalendarSync(syncStatus=Failed, retryScheduledAt=UtcNow+10min)`; FE redirect to `?calendarResult=failed`
- [ ] `CalendarSyncRetryBackgroundService` re-processes `Failed` records at `retryScheduledAt` (mocked `PeriodicTimer` in unit test)
- [ ] `GET /api/appointments/{id}/ics` → response `Content-Type: text/calendar`; ICS body contains `DTSTART`, `DTEND`, `SUMMARY`, `LOCATION`, `DESCRIPTION` with booking ref
- [ ] `GOOGLE_CLIENT_SECRET` absent from all committed config files (OWASP A02 gate)
- [ ] OAuth `state` Redis key deleted after one successful callback consumption (one-time use)

---

## Implementation Checklist

- [ ] Create `InitiateGoogleSyncCommandHandler`: validate appointment ownership; generate PKCE (`code_verifier`, `code_challenge`); composite `state = "{guid}:{appointmentId}"`; store in Redis (10-min TTL); build Google OAuth URL; return 302 redirect
- [ ] Create `HandleGoogleCallbackCommandHandler`: validate + consume `state` from Redis (one-time use); exchange code for tokens; encrypt via `IDataProtector`; upsert `PatientOAuthToken`; call `IGoogleCalendarService.CreateOrUpdateEventAsync()`; upsert `CalendarSync`; redirect FE with `calendarResult` param; AuditLog
- [ ] Create `GoogleCalendarService`: `Google.Apis.Calendar.v3` events insert/patch/delete; auto-refresh on 401; throw `GoogleTokenExpiredException` on double-401; decrypt tokens from `IDataProtector` before each API call
- [ ] Create `IcsGenerationService` (`Ical.Net`): build `VEvent` with all FR-036 fields (date, start/end time, specialty, clinic, type, booking ref); return as `byte[]` with correct MIME type
- [ ] Create `CalendarSyncRetryBackgroundService`: `PeriodicTimer` 5-min; `IServiceScopeFactory` for EF Core; query `Failed` + `retryScheduledAt <= UtcNow`; re-invoke sync logic; increment `retryCount`; after 3 failures → `PermanentFailed` + Serilog.Error
- [ ] Create `GetCalendarSyncStatusQueryHandler`: validate `CalendarSync.patientId == requestingPatientId`; return `CalendarSyncStatusDto { SyncStatus, EventLink, Provider, SyncedAt }`
- [ ] Register `GOOGLE_CLIENT_SECRET` from environment variable only; bind `GoogleCalendarSettings` from `appsettings.json` (non-secrets only); register Data Protection API with Redis key persistence for production
- [ ] Verify `GOOGLE_CLIENT_SECRET` not committed in any appsettings file (OWASP A02)
