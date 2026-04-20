# Task - TASK_002

## Requirement Reference

- **User Story**: US_036 — Microsoft Outlook Calendar OAuth 2.0 Integration
- **Story Location**: `.propel/context/tasks/EP-007/us_036/us_036.md`
- **Acceptance Criteria**:
  - AC-1: When OAuth 2.0 flow completes, a calendar event is created via Microsoft Graph API (`POST /me/events`) with appointment date, start/end time, provider specialty, clinic name, appointment type, and booking reference number.
  - AC-2: CalendarSync record stored with `provider = Outlook`, `externalEventId`, `syncStatus = Synced`; Outlook web event link returned to FE.
  - AC-3: `GET /api/calendar/ics?appointmentId={id}` generates and returns a valid RFC 5545 `.ics` file with all required fields.
  - AC-4: On Graph API failure, `syncStatus = Failed` stored, retry scheduled (10-minute delay via `Channel<T>` background service), ICS download endpoint available as fallback.
- **Edge Cases**:
  - Multiple Outlook calendars: Event posted to default calendar (`/me/events`); no calendar picker in Phase 1.
  - OAuth consent revoked: Graph API returns `401`; `CalendarSync.syncStatus` updated to `Revoked`; `401` response code returned to FE to trigger "Reconnect Outlook" prompt.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | No |
| **Figma URL** | N/A |
| **Wireframe Status** | N/A |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | N/A |
| **Screen Spec** | N/A |
| **UXR Requirements** | N/A |
| **Design Tokens** | N/A |

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Backend | ASP.NET Core Web API | .NET 9 |
| Mediator | MediatR | 12.x |
| Validation | FluentValidation | 11.x |
| ORM | Entity Framework Core | 9.x |
| Database | PostgreSQL | 16+ |
| Library | Microsoft.Graph SDK | 5.x |
| Library | Microsoft.Identity.Client (MSAL) | 4.x |
| AI/ML | N/A | N/A |
| Vector Store | N/A | N/A |
| AI Gateway | N/A | N/A |
| Mobile | N/A | N/A |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement the `OutlookCalendarController` and supporting service layer for Microsoft Outlook Calendar OAuth 2.0 integration via Microsoft Graph API v1.0.

**`POST /api/calendar/outlook/initiate`** — `InitiateOutlookSyncCommand` (MediatR). Resolves `patientId` from JWT. Generates a Microsoft OAuth 2.0 PKCE authorization URL using `IConfidentialClientApplication` (MSAL `Microsoft.Identity.Client`). Encodes `state = Base64(appointmentId + ":" + patientId)` for CSRF protection. Returns `{ authorizationUrl }` for the FE to redirect to.

**`GET /api/calendar/outlook/callback`** — `HandleOutlookCallbackCommand` (MediatR). Called by FE after Microsoft redirects back with `code` + `state`. Handler:
1. Validates `state` — decodes and verifies `patientId` matches JWT claim (OWASP A01).
2. Exchanges `code` for `accessToken` via MSAL `AcquireTokenByAuthorizationCode()`.
3. Calls Microsoft Graph `POST /me/events` via `GraphServiceClient` with full appointment fields (FR-036).
4. INSERTs `CalendarSync { provider = Outlook, externalEventId = event.Id, syncStatus = Synced, syncedAt = UtcNow }`.
5. Writes audit log `OutlookCalendarSynced` (FR-057).
6. Returns `CalendarSyncResultDto { syncStatus = Synced, eventLink = event.WebLink }`.

**Error handling:**
- `401 Unauthorized` from Graph API → set `syncStatus = Revoked`; return `401` to FE (edge case — revoked consent).
- Any other Graph API failure → set `syncStatus = Failed`; enqueue `OutlookRetryRequest` to `Channel<OutlookRetryRequest>` (background service retries once after 10 minutes — AC-4).

**`GET /api/calendar/sync-status`** — `GetCalendarSyncStatusQuery`; loads latest `CalendarSync` record for `(appointmentId, provider, patientId)` ordered by `syncedAt DESC`; `AsNoTracking()`; returns `CalendarSyncStatusResponse { provider, syncStatus, eventLink }`. Used by FE to restore sync state on page load.

**`GET /api/calendar/ics?appointmentId={id}`** — `GenerateIcsQuery` (MediatR); loads appointment details; calls `IIcsGeneratorService.GenerateAsync()` which produces a RFC 5545-compliant `.ics` string; returns `File(Encoding.UTF8.GetBytes(ics), "text/calendar", "appointment.ics")` (AC-3). `IIcsGeneratorService` is shared with the US_035 Google Calendar flow.

**`OutlookCalendarRetryService`** — `BackgroundService` consuming `Channel<OutlookRetryRequest>`. Waits `FailedAt + 600s` (10 minutes — AC-4), then retries once by re-executing the Graph API call. On second failure: sets `retryCount = 2` on the `CalendarSync` record. Logs Warning; never throws (AG-6).

**Configuration** (`OutlookCalendarOptions`): `ClientId`, `ClientSecret`, `TenantId`, `RedirectUri` — injected via `IOptions<OutlookCalendarOptions>`; never hardcoded (OWASP A02). Registered in `Program.cs` with `builder.Services.Configure<OutlookCalendarOptions>(configuration.GetSection("OutlookCalendar"))`.

**`patientId` is always resolved from JWT claims** — never from request body or URL (OWASP A01). `state` parameter carries only an opaque token that is verified against the JWT claim.

## Dependent Tasks

- **US_008 (EP-DATA)** — `CalendarSync` entity and `calendar_syncs` table must exist; `CalendarProvider` and `CalendarSyncStatus` enums must be defined.
- **US_035 / TASK_002** — `IIcsGeneratorService` interface and `IcsGeneratorService` implementation must exist (shared with this task). If US_035 is delivered concurrently, coordinate on service interface definition.
- **US_013 / TASK_001** — `IAuditLogRepository` write-only pattern must be in place.

## Impacted Components

| Component | Status | Location |
|-----------|--------|----------|
| `OutlookCalendarController` | NEW | `Server/Controllers/OutlookCalendarController.cs` |
| `IOutlookCalendarService` + `OutlookCalendarService` | NEW | `Server/Services/Calendar/OutlookCalendarService.cs` |
| `InitiateOutlookSyncCommand` + `InitiateOutlookSyncCommandHandler` | NEW | `Server/Features/Calendar/Outlook/InitiateOutlookSync/` |
| `HandleOutlookCallbackCommand` + `HandleOutlookCallbackCommandHandler` | NEW | `Server/Features/Calendar/Outlook/HandleOutlookCallback/` |
| `GetCalendarSyncStatusQuery` + `GetCalendarSyncStatusQueryHandler` | NEW | `Server/Features/Calendar/GetCalendarSyncStatus/` |
| `GenerateIcsQuery` + `GenerateIcsQueryHandler` | NEW or EXTEND | `Server/Features/Calendar/GenerateIcs/` |
| `IIcsGeneratorService` + `IcsGeneratorService` | NEW or EXTEND | `Server/Services/Calendar/IcsGeneratorService.cs` |
| `OutlookCalendarRetryService` | NEW | `Server/BackgroundServices/OutlookCalendarRetryService.cs` |
| `OutlookCalendarOptions` | NEW | `Server/Options/OutlookCalendarOptions.cs` |

## Implementation Plan

1. **`OutlookCalendarOptions`**:

   ```csharp
   public class OutlookCalendarOptions
   {
       public string ClientId { get; set; } = string.Empty;
       public string ClientSecret { get; set; } = string.Empty;
       public string TenantId { get; set; } = string.Empty;
       public string RedirectUri { get; set; } = string.Empty;
       public string[] Scopes { get; set; } = ["Calendars.ReadWrite", "offline_access"];
   }
   ```

   Registered via `builder.Services.Configure<OutlookCalendarOptions>(config.GetSection("OutlookCalendar"))`. Values sourced from Azure Key Vault / environment variables — never from `appsettings.json` in production (OWASP A02).

2. **`InitiateOutlookSyncCommandHandler.Handle()`**:

   ```csharp
   var patientId = GetPatientIdFromJwt();   // OWASP A01
   var appointmentId = request.AppointmentId;

   // Encode state for CSRF protection — opaque, verified on callback
   var state = Convert.ToBase64String(
       Encoding.UTF8.GetBytes($"{appointmentId}:{patientId}")
   );

   var app = ConfidentialClientApplicationBuilder
       .Create(_options.ClientId)
       .WithClientSecret(_options.ClientSecret)
       .WithAuthority(AzureCloudInstance.AzurePublic, _options.TenantId)
       .WithRedirectUri(_options.RedirectUri)
       .Build();

   var authUrl = await app.GetAuthorizationRequestUrl(_options.Scopes)
       .WithState(state)
       .ExecuteAsync();

   return new InitiateOutlookSyncResult(authUrl.ToString());
   ```

3. **`HandleOutlookCallbackCommandHandler.Handle()`**:

   ```csharp
   var patientId = GetPatientIdFromJwt();

   // Validate state — verify patientId matches JWT (OWASP A01 — CSRF guard)
   var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(request.State));
   var parts = decoded.Split(':');
   if (parts.Length != 2 || !Guid.TryParse(parts[1], out var statePatientId) || statePatientId != patientId)
       throw new SecurityException("Invalid OAuth state parameter.");

   var appointmentId = Guid.Parse(parts[0]);

   // Exchange code → access token (MSAL)
   var app = BuildConfidentialClient();
   var result = await app.AcquireTokenByAuthorizationCode(_options.Scopes, request.Code)
       .ExecuteAsync(cancellationToken);

   // Build GraphServiceClient with the acquired token
   var graphClient = new GraphServiceClient(
       new BaseBearerTokenAuthenticationProvider(
           new TokenCredential(result.AccessToken)));

   // Load appointment details for event fields (FR-036)
   var appointment = await _dbContext.Appointments
       .AsNoTracking()
       .Include(a => a.Specialty)
       .FirstOrDefaultAsync(a => a.Id == appointmentId && a.PatientId == patientId, cancellationToken)
       ?? throw new NotFoundException(nameof(Appointment), appointmentId);

   // Create Graph event (POST /me/events)
   var graphEvent = new Event
   {
       Subject = $"Appointment — {appointment.Specialty.Name}",
       Body = new ItemBody { Content = $"Reference: {appointment.Id}", ContentType = BodyType.Text },
       Start = new DateTimeTimeZone
       {
           DateTime = appointment.Date.ToDateTime(appointment.TimeSlotStart).ToString("o"),
           TimeZone = "UTC"
       },
       End = new DateTimeTimeZone
       {
           DateTime = appointment.Date.ToDateTime(appointment.TimeSlotEnd).ToString("o"),
           TimeZone = "UTC"
       },
       Location = new Location { DisplayName = "Propel IQ Clinic" }
   };

   Event createdEvent;
   try
   {
       createdEvent = await graphClient.Me.Events.PostAsync(graphEvent, cancellationToken: cancellationToken);
   }
   catch (ODataError ex) when (ex.ResponseStatusCode == 401)
   {
       // Revoked consent — update existing CalendarSync if present
       await UpsertCalendarSync(appointmentId, patientId, null, CalendarSyncStatus.Revoked, cancellationToken);
       throw new CalendarAuthRevokedException("Outlook OAuth consent has been revoked.");
   }
   catch (Exception)
   {
       await UpsertCalendarSync(appointmentId, patientId, null, CalendarSyncStatus.Failed, cancellationToken);
       // Schedule retry via Channel<T> (AC-4 — 10-minute retry)
       await _retryChannel.Writer.WriteAsync(
           new OutlookRetryRequest(appointmentId, patientId, result.AccessToken, DateTime.UtcNow), cancellationToken);
       throw;   // Let global error handler return 502
   }

   // Success — UPSERT CalendarSync
   await UpsertCalendarSync(appointmentId, patientId, createdEvent.Id, CalendarSyncStatus.Synced, cancellationToken);

   await _auditLogRepository.WriteAsync(new AuditLogEntry
   {
       UserId = patientId,
       Action = "OutlookCalendarSynced",
       EntityType = "CalendarSync",
       EntityId = appointmentId,
       IpAddress = _httpContextAccessor.HttpContext!.Connection.RemoteIpAddress?.ToString()
   });

   return new CalendarSyncResultDto(CalendarSyncStatus.Synced, createdEvent.WebLink);
   ```

4. **`IIcsGeneratorService`** (shared with US_035):

   ```csharp
   public interface IIcsGeneratorService
   {
       string Generate(Appointment appointment);
   }

   public class IcsGeneratorService : IIcsGeneratorService
   {
       public string Generate(Appointment appointment)
       {
           // RFC 5545 compliant VCALENDAR string
           var uid = $"{appointment.Id}@propeliq.health";
           var dtStart = appointment.Date.ToDateTime(appointment.TimeSlotStart)
               .ToString("yyyyMMddTHHmmssZ");
           var dtEnd = appointment.Date.ToDateTime(appointment.TimeSlotEnd)
               .ToString("yyyyMMddTHHmmssZ");

           return $"""
               BEGIN:VCALENDAR
               VERSION:2.0
               PRODID:-//PropelIQ//EN
               BEGIN:VEVENT
               UID:{uid}
               DTSTAMP:{DateTime.UtcNow:yyyyMMddTHHmmssZ}
               DTSTART:{dtStart}
               DTEND:{dtEnd}
               SUMMARY:Appointment — {appointment.Specialty?.Name}
               DESCRIPTION:Booking Reference: {appointment.Id}\\nClinic: Propel IQ
               LOCATION:Propel IQ Clinic
               STATUS:CONFIRMED
               END:VEVENT
               END:VCALENDAR
               """;
       }
   }
   ```

5. **`OutlookCalendarRetryService`** (`BackgroundService` — AC-4):

   ```csharp
   protected override async Task ExecuteAsync(CancellationToken stoppingToken)
   {
       await foreach (var request in _retryChannel.Reader.ReadAllAsync(stoppingToken))
       {
           try
           {
               // Wait 10 minutes from failure time
               var delay = request.FailedAt.AddSeconds(600) - DateTime.UtcNow;
               if (delay > TimeSpan.Zero) await Task.Delay(delay, stoppingToken);

               // Re-attempt Graph API event creation
               await _outlookService.CreateEventAsync(request, stoppingToken);
           }
           catch (Exception ex)
           {
               _logger.LogWarning(ex, "Outlook calendar sync retry failed for appointment {Id}. Giving up.", request.AppointmentId);
               // Mark retryCount = 2 on CalendarSync — no further retry
               await _outlookService.MarkRetryExhaustedAsync(request.AppointmentId, request.PatientId, stoppingToken);
               // AG-6: Never rethrow from BackgroundService
           }
       }
   }
   ```

6. **`OutlookCalendarController`**:

   ```csharp
   [ApiController]
   [Route("api/calendar")]
   public class OutlookCalendarController : ControllerBase
   {
       [HttpPost("outlook/initiate")]
       [Authorize(Roles = "Patient")]
       public async Task<IActionResult> Initiate([FromBody] InitiateOutlookSyncCommand cmd, ISender mediator)
           => Ok(await mediator.Send(cmd));

       [HttpGet("outlook/callback")]
       // No [Authorize] — OAuth redirect; patientId validated from state parameter
       public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string state, ISender mediator)
       {
           try { return Ok(await mediator.Send(new HandleOutlookCallbackCommand(code, state))); }
           catch (CalendarAuthRevokedException) { return Unauthorized(); }
       }

       [HttpGet("sync-status")]
       [Authorize(Roles = "Patient")]
       public async Task<IActionResult> GetSyncStatus([FromQuery] Guid appointmentId, [FromQuery] string provider, ISender mediator)
           => Ok(await mediator.Send(new GetCalendarSyncStatusQuery(appointmentId, provider)));

       [HttpGet("ics")]
       [Authorize(Roles = "Patient")]
       public async Task<IActionResult> DownloadIcs([FromQuery] Guid appointmentId, ISender mediator)
       {
           var icsContent = await mediator.Send(new GenerateIcsQuery(appointmentId));
           return File(Encoding.UTF8.GetBytes(icsContent), "text/calendar", "appointment.ics");
       }
   }
   ```

   > **Security note**: `GET /api/calendar/outlook/callback` has no `[Authorize]` attribute because Microsoft OAuth redirects here before the user has a session. The `state` parameter is the sole CSRF guard — it encodes `appointmentId:patientId` and is verified against the JWT in `HandleOutlookCallbackCommandHandler` (OWASP A01).

## Current Project State

```
Server/
├── Controllers/
│   └── OutlookCalendarController.cs                       ← NEW
├── Services/
│   └── Calendar/
│       ├── IIcsGeneratorService.cs                        ← NEW (shared with US_035)
│       ├── IcsGeneratorService.cs                         ← NEW
│       └── OutlookCalendarService.cs                      ← NEW
├── BackgroundServices/
│   └── OutlookCalendarRetryService.cs                     ← NEW
├── Options/
│   └── OutlookCalendarOptions.cs                          ← NEW
└── Features/
    └── Calendar/
        ├── Outlook/
        │   ├── InitiateOutlookSync/
        │   │   ├── InitiateOutlookSyncCommand.cs
        │   │   └── InitiateOutlookSyncCommandHandler.cs
        │   └── HandleOutlookCallback/
        │       ├── HandleOutlookCallbackCommand.cs
        │       └── HandleOutlookCallbackCommandHandler.cs
        ├── GetCalendarSyncStatus/
        │   ├── GetCalendarSyncStatusQuery.cs
        │   └── GetCalendarSyncStatusQueryHandler.cs
        └── GenerateIcs/
            ├── GenerateIcsQuery.cs
            └── GenerateIcsQueryHandler.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Options/OutlookCalendarOptions.cs` | Strongly-typed config: `ClientId`, `ClientSecret`, `TenantId`, `RedirectUri`, `Scopes`; sourced from Key Vault (OWASP A02) |
| CREATE | `Server/Services/Calendar/IIcsGeneratorService.cs` | Interface: `string Generate(Appointment appointment)` (shared with US_035) |
| CREATE | `Server/Services/Calendar/IcsGeneratorService.cs` | RFC 5545 VCALENDAR generation: UID, DTSTART, DTEND, SUMMARY, DESCRIPTION (ref), LOCATION, STATUS |
| CREATE | `Server/Features/Calendar/Outlook/InitiateOutlookSync/InitiateOutlookSyncCommandHandler.cs` | MSAL `GetAuthorizationRequestUrl()` with PKCE; CSRF `state = Base64(appointmentId:patientId)`; returns auth URL |
| CREATE | `Server/Features/Calendar/Outlook/HandleOutlookCallback/HandleOutlookCallbackCommandHandler.cs` | Validates `state` vs JWT `patientId`; MSAL `AcquireTokenByAuthorizationCode()`; `GraphServiceClient.Me.Events.PostAsync()`; UPSERT `CalendarSync`; retry channel on failure; audit log `OutlookCalendarSynced` |
| CREATE | `Server/Features/Calendar/GetCalendarSyncStatus/GetCalendarSyncStatusQueryHandler.cs` | `AsNoTracking()` load latest `CalendarSync` by `(appointmentId, provider, patientId)`; returns `CalendarSyncStatusResponse` |
| CREATE | `Server/Features/Calendar/GenerateIcs/GenerateIcsQueryHandler.cs` | Load appointment; call `IIcsGeneratorService.Generate()`; return ICS string |
| CREATE | `Server/BackgroundServices/OutlookCalendarRetryService.cs` | `BackgroundService`: `Channel<OutlookRetryRequest>` consumer; 600s delay; one retry; `LogWarning` on failure; AG-6 never throws |
| CREATE | `Server/Controllers/OutlookCalendarController.cs` | 4 endpoints: `POST /initiate`, `GET /callback` (no auth), `GET /sync-status`, `GET /ics` |

## External References

- [Microsoft Graph SDK for .NET v5 — Create event](https://learn.microsoft.com/en-us/graph/api/user-post-events?tabs=csharp)
- [Microsoft.Identity.Client (MSAL) — `AcquireTokenByAuthorizationCode`](https://learn.microsoft.com/en-us/azure/active-directory/develop/msal-net-acquire-token-auth-code)
- [Microsoft Graph — `Event` resource type (v1.0)](https://learn.microsoft.com/en-us/graph/api/resources/event)
- [RFC 5545 — iCalendar specification](https://datatracker.ietf.org/doc/html/rfc5545)
- [ASP.NET Core `BackgroundService` + `Channel<T>`](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)
- [OWASP A01:2021 — patientId from JWT; state CSRF validation](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)
- [OWASP A02:2021 — Client secrets via Key Vault, not appsettings](https://owasp.org/Top10/A02_2021-Cryptographic_Failures/)
- [TR-013 — Microsoft Graph API OAuth 2.0 (design.md#TR-013)](design.md#TR-013)
- [FR-036 — Full calendar event fields (spec.md#FR-036)](spec.md#FR-036)

## Build Commands

- Refer to: `.propel/build/backend-build.md`
- NuGet packages: `dotnet add Server package Microsoft.Graph --version 5.*`; `dotnet add Server package Microsoft.Identity.Client --version 4.*`

## Implementation Validation Strategy

- [ ] Unit tests pass: `InitiateOutlookSyncCommandHandler` returns a valid `https://login.microsoftonline.com/...` authorization URL with PKCE parameters
- [ ] Unit tests pass: `HandleOutlookCallbackCommandHandler` rejects callback with mismatched `patientId` in `state` (CSRF guard)
- [ ] Unit tests pass: `HandleOutlookCallbackCommandHandler` UPSERTs `CalendarSync { syncStatus = Synced }` on successful Graph event creation
- [ ] Unit tests pass: `IcsGeneratorService.Generate()` produces RFC 5545-compliant string with UID, DTSTART, DTEND, SUMMARY, DESCRIPTION containing booking reference
- [ ] `GET /api/calendar/ics` returns `Content-Type: text/calendar` with filename `appointment.ics`
- [ ] Graph API `401` response sets `syncStatus = Revoked` and controller returns HTTP `401`
- [ ] Graph API non-401 failure enqueues `OutlookRetryRequest` to retry channel; `syncStatus = Failed` stored
- [ ] `OutlookCalendarOptions.ClientSecret` never appears in logs or API responses (OWASP A02)

## Implementation Checklist

- [ ] `OutlookCalendarOptions` registered via `IOptions<T>`; `ClientSecret` sourced from Key Vault / env vars; never hardcoded (OWASP A02); scopes include `Calendars.ReadWrite` and `offline_access`
- [ ] `InitiateOutlookSyncCommandHandler`: `patientId` from JWT (OWASP A01); `state = Base64(appointmentId:patientId)` for CSRF; MSAL `GetAuthorizationRequestUrl()` with PKCE; returns `{ authorizationUrl }` (AC-1 OAuth flow initiation)
- [ ] `HandleOutlookCallbackCommandHandler`: validate `state` against JWT `patientId` (OWASP A01 CSRF guard); MSAL `AcquireTokenByAuthorizationCode()`; `GraphServiceClient.Me.Events.PostAsync()` with all FR-036 fields (date, time, specialty, clinic, reference); UPSERT `CalendarSync`; catch `ODataError 401` → `Revoked`; catch other exceptions → `Failed` + enqueue to retry `Channel<T>`; audit log `OutlookCalendarSynced` (AC-2, AC-4, edge case revoked)
- [ ] `IcsGeneratorService.Generate()`: RFC 5545 compliant — `BEGIN/END:VCALENDAR`, `VEVENT` with `UID`, `DTSTAMP`, `DTSTART`, `DTEND`, `SUMMARY`, `DESCRIPTION` (booking reference), `LOCATION`, `STATUS:CONFIRMED` (AC-3, FR-036)
- [ ] `OutlookCalendarRetryService` (`BackgroundService`): `Channel<OutlookRetryRequest>` consumer; `Task.Delay(600s)` before retry; one retry attempt; on second failure `LogWarning` + `MarkRetryExhaustedAsync()`; wrapped in try/catch — NEVER throws from `ExecuteAsync` (AG-6, AC-4 retry)
- [ ] `GET /api/calendar/outlook/callback` has no `[Authorize]` attribute (OAuth redirect target); security enforced via `state` CSRF validation in handler; all other endpoints `[Authorize(Roles="Patient")]`; `patientId` never from URL or body in any handler
