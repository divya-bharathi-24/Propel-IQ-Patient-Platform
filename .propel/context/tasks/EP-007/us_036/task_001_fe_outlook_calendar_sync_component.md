# Task - TASK_001

## Requirement Reference

- **User Story**: US_036 — Microsoft Outlook Calendar OAuth 2.0 Integration
- **Story Location**: `.propel/context/tasks/EP-007/us_036/us_036.md`
- **Acceptance Criteria**:
  - AC-1: Given I click "Add to Outlook Calendar", When I complete the Microsoft OAuth 2.0 flow, Then a calendar event is created via Microsoft Graph API with appointment date, start/end time, provider specialty, clinic name, appointment type, and booking reference number.
  - AC-2: Given the Outlook event is successfully created, When the API response confirms creation, Then a CalendarSync record is stored and an Outlook web event link is shown.
  - AC-3: Given I choose "Download ICS" instead, When the download is initiated, Then a valid `.ics` file is generated and downloaded.
  - AC-4: Given the Microsoft Graph API is unavailable, When sync fails, Then `syncStatus = Failed` is stored, a retry is scheduled, and the ICS download option is prominently shown as a fallback.
- **Edge Cases**:
  - Multiple Outlook calendars: Event added to primary calendar; no calendar picker in Phase 1.
  - OAuth consent revoked after sync: Next attempt returns 401; `syncStatus = Revoked` set; user prompted to re-authorize with a "Reconnect Outlook" button.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | PENDING |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-outlook-sync.[html\|png\|jpg]` or provide external URL |
| **Screen Spec** | N/A (figma_spec.md not yet generated) |
| **UXR Requirements** | N/A (figma_spec.md not yet generated) |
| **Design Tokens** | N/A (designsystem.md not yet generated) |

### **CRITICAL: Wireframe Implementation Requirement**

**Wireframe Status = PENDING:** When wireframe becomes available, implementation MUST:

- Match layout of "Add to Outlook Calendar" button and sync status states from the wireframe
- Implement all states: Default (unsynced), Loading (OAuth redirect in progress), Synced (link shown), Failed (ICS fallback prominent), Revoked ("Reconnect Outlook" prompt)
- Validate implementation against wireframe at breakpoints: 375px, 768px, 1440px
- Run `/analyze-ux` after implementation to verify pixel-perfect alignment

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Frontend | Angular | 18.x |
| Frontend State | NgRx Signals | 18.x |
| Backend | ASP.NET Core Web API | .net 10 |
| Database | PostgreSQL | 16+ |
| Library | Angular Router | 18.x |
| Library | Angular `HttpClient` | 18.x |
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

Implement the `OutlookCalendarSyncComponent` — an Angular 18 standalone, `ChangeDetectionStrategy.OnPush` component surfaced on both the booking confirmation page and the patient dashboard. The component renders calendar sync and ICS download controls.

**Microsoft OAuth 2.0 PKCE flow:**
1. Patient clicks "Add to Outlook Calendar".
2. Component calls `CalendarSyncService.initiateOutlookSync(appointmentId)` → `POST /api/calendar/outlook/initiate` which returns `{ authorizationUrl: string }`.
3. Component navigates to the returned URL via `window.location.href = authorizationUrl` (external Microsoft OAuth consent screen).
4. After consent, Microsoft redirects to `/calendar/outlook/callback?code=...&state=...` (handled by `OutlookCallbackComponent`).
5. `OutlookCallbackComponent` calls `CalendarSyncService.exchangeOutlookCode(code, state)` → `GET /api/calendar/outlook/callback?code=&state=` which creates the Graph event and inserts the `CalendarSync` record.
6. On success, the FE polls `CalendarSyncService.getSyncStatus(appointmentId, 'Outlook')` once to retrieve the `externalEventLink` and renders it.

**ICS download (AC-3):** Separate "Download ICS" button triggers `CalendarSyncService.downloadIcs(appointmentId)` → `GET /api/calendar/ics?appointmentId={id}`. The service receives a binary `Blob` response; creates a transient `<a>` anchor with `URL.createObjectURL(blob)` and `download="appointment.ics"` and programmatically clicks it. No page navigation.

**Sync status rendering (AC-2, AC-4):**
- `syncStatus = 'Synced'`: Show green "Synced to Outlook ✓" chip with `<a [href]="eventLink" target="_blank">Open in Outlook</a>`.
- `syncStatus = 'Failed'`: Show amber "Sync failed" chip; render ICS download button prominently with `aria-live="polite"` announcement "Sync failed. Download ICS as fallback."
- `syncStatus = 'Revoked'`: Show "Reconnect Outlook" button — re-initiates the OAuth flow.

**State model** (`signal<OutlookSyncState>`):

```typescript
type OutlookSyncStatus = 'Unknown' | 'Initiating' | 'Synced' | 'Failed' | 'Revoked';
interface OutlookSyncState {
  status: OutlookSyncStatus;
  eventLink: string | null;
  errorMessage: string | null;
}
```

**`OutlookCallbackComponent`** (`/calendar/outlook/callback`): Reads `code` and `state` from `ActivatedRoute.queryParamMap`; calls `exchangeOutlookCode()`; on success navigates to `/patient/dashboard`; on failure navigates to `/patient/dashboard?calendarError=outlook`.

## Dependent Tasks

- **US_036 / TASK_002** — `POST /api/calendar/outlook/initiate`, `GET /api/calendar/outlook/callback`, `GET /api/calendar/ics`, `GET /api/calendar/outlook/sync-status` backend endpoints must be implemented.
- **US_035 / TASK_001** — `OutlookCalendarSyncComponent` may share the `CalendarSyncService` and `downloadIcs()` helper with the Google Calendar component (same service, different provider routes). Coordinate to avoid duplication.
- **US_011 / TASK_001** — `AuthInterceptor` must attach Bearer token to all `HttpClient` calls.
- **US_008 (EP-DATA)** — `CalendarSync` entity and `calendar_syncs` table must exist.

## Impacted Components

| Component | Status | Location |
|-----------|--------|----------|
| `OutlookCalendarSyncComponent` | NEW | `app/features/calendar/outlook-sync/outlook-calendar-sync.component.ts` |
| `OutlookCallbackComponent` | NEW | `app/features/calendar/outlook-callback/outlook-callback.component.ts` |
| `CalendarSyncService` | NEW or EXTEND | `app/features/calendar/calendar-sync.service.ts` |
| `CalendarSyncModels` | NEW or EXTEND | `app/features/calendar/calendar-sync.models.ts` |
| `BookingConfirmationComponent` | MODIFY | Add `<app-outlook-calendar-sync [appointmentId]="...">` alongside Google sync button |
| `PatientDashboardComponent` | MODIFY | Add `<app-outlook-calendar-sync [appointmentId]="...">` per appointment card |
| `AppRoutingModule` | MODIFY | Add `/calendar/outlook/callback` route to `OutlookCallbackComponent` |

## Implementation Plan

1. **TypeScript models** (`calendar-sync.models.ts`):

   ```typescript
   export type CalendarProvider = 'Google' | 'Outlook';
   export type CalendarSyncStatus = 'Unknown' | 'Initiating' | 'Synced' | 'Failed' | 'Revoked';

   export interface InitiateCalendarSyncResponse {
     authorizationUrl: string;
   }

   export interface CalendarSyncStatusResponse {
     provider: CalendarProvider;
     syncStatus: CalendarSyncStatus;
     eventLink: string | null;
   }
   ```

2. **`CalendarSyncService`** — extended or created to include Outlook methods:

   ```typescript
   @Injectable({ providedIn: 'root' })
   export class CalendarSyncService {
     private readonly http = inject(HttpClient);

     initiateOutlookSync(appointmentId: string): Observable<InitiateCalendarSyncResponse> {
       return this.http.post<InitiateCalendarSyncResponse>(
         '/api/calendar/outlook/initiate', { appointmentId }
       );
     }

     exchangeOutlookCode(code: string, state: string): Observable<void> {
       return this.http.get<void>('/api/calendar/outlook/callback', {
         params: { code, state }
       });
     }

     getSyncStatus(appointmentId: string, provider: CalendarProvider): Observable<CalendarSyncStatusResponse> {
       return this.http.get<CalendarSyncStatusResponse>('/api/calendar/sync-status', {
         params: { appointmentId, provider }
       });
     }

     downloadIcs(appointmentId: string): Observable<Blob> {
       return this.http.get('/api/calendar/ics', {
         params: { appointmentId },
         responseType: 'blob'
       });
     }
   }
   ```

3. **`OutlookCalendarSyncComponent`** — signals + OAuth redirect:

   ```typescript
   @Component({
     standalone: true,
     selector: 'app-outlook-calendar-sync',
     changeDetection: ChangeDetectionStrategy.OnPush,
     template: `...`
   })
   export class OutlookCalendarSyncComponent {
     @Input({ required: true }) appointmentId!: string;
     private readonly svc = inject(CalendarSyncService);
     private readonly destroyRef = inject(DestroyRef);

     syncState = signal<OutlookSyncState>({ status: 'Unknown', eventLink: null, errorMessage: null });

     ngOnInit(): void {
       // Check existing sync status on load
       this.svc.getSyncStatus(this.appointmentId, 'Outlook').pipe(
         takeUntilDestroyed(this.destroyRef),
         catchError(() => of(null))
       ).subscribe(res => {
         if (res) this.syncState.set({ status: res.syncStatus as OutlookSyncStatus, eventLink: res.eventLink, errorMessage: null });
       });
     }

     initiateSync(): void {
       this.syncState.update(s => ({ ...s, status: 'Initiating' }));
       this.svc.initiateOutlookSync(this.appointmentId).pipe(
         takeUntilDestroyed(this.destroyRef)
       ).subscribe({
         next: res => { window.location.href = res.authorizationUrl; },
         error: () => this.syncState.set({ status: 'Failed', eventLink: null, errorMessage: 'Could not initiate Outlook sync. Please try again.' })
       });
     }

     downloadIcs(): void {
       this.svc.downloadIcs(this.appointmentId).pipe(
         takeUntilDestroyed(this.destroyRef)
       ).subscribe(blob => {
         const url = URL.createObjectURL(blob);
         const anchor = document.createElement('a');
         anchor.href = url;
         anchor.download = 'appointment.ics';
         anchor.click();
         URL.revokeObjectURL(url);
       });
     }
   }
   ```

4. **Template** — all sync states:

   ```html
   @switch (syncState().status) {
     @case ('Unknown') {
       <button (click)="initiateSync()" aria-label="Add appointment to Outlook Calendar">
         Add to Outlook Calendar
       </button>
       <button (click)="downloadIcs()" aria-label="Download ICS file for manual import">
         Download ICS
       </button>
     }
     @case ('Initiating') {
       <span role="status" aria-live="polite">Redirecting to Microsoft...</span>
     }
     @case ('Synced') {
       <span class="chip synced">Synced to Outlook ✓</span>
       <a [href]="syncState().eventLink" target="_blank" rel="noopener noreferrer">
         Open in Outlook
       </a>
     }
     @case ('Failed') {
       <span class="chip failed" role="alert">Sync failed</span>
       <p aria-live="polite">Sync failed. Download ICS as a fallback.</p>
       <button (click)="downloadIcs()" class="fallback-prominent">Download ICS</button>
       <button (click)="initiateSync()">Retry Outlook Sync</button>
     }
     @case ('Revoked') {
       <span class="chip revoked">Outlook disconnected</span>
       <button (click)="initiateSync()">Reconnect Outlook</button>
     }
   }
   ```

5. **`OutlookCallbackComponent`** (`/calendar/outlook/callback`):

   ```typescript
   constructor() {
     const code = this.route.snapshot.queryParamMap.get('code')!;
     const state = this.route.snapshot.queryParamMap.get('state')!;

     this.svc.exchangeOutlookCode(code, state).pipe(
       takeUntilDestroyed(this.destroyRef)
     ).subscribe({
       next: () => this.router.navigate(['/patient/dashboard']),
       error: () => this.router.navigate(['/patient/dashboard'], {
         queryParams: { calendarError: 'outlook' }
       })
     });
   }
   ```

6. **Route registration**:

   ```typescript
   { path: 'calendar/outlook/callback', component: OutlookCallbackComponent }
   // No auth guard — callback URL is publicly accessible (Microsoft redirects here)
   ```

## Current Project State

```
app/
├── features/
│   ├── auth/             (US_011 — completed)
│   ├── booking/          (US_019 — completed)
│   ├── patient/          (US_016 — completed)
│   └── calendar/         ← NEW (this task + US_035)
│       ├── calendar-sync.service.ts
│       ├── calendar-sync.models.ts
│       ├── google-sync/
│       │   └── google-calendar-sync.component.ts    (US_035)
│       ├── outlook-sync/
│       │   └── outlook-calendar-sync.component.ts   ← NEW
│       └── outlook-callback/
│           └── outlook-callback.component.ts         ← NEW
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `app/features/calendar/calendar-sync.models.ts` | TypeScript models: `CalendarProvider`, `CalendarSyncStatus`, `InitiateCalendarSyncResponse`, `CalendarSyncStatusResponse`, `OutlookSyncState` |
| CREATE | `app/features/calendar/calendar-sync.service.ts` | `CalendarSyncService`: `initiateOutlookSync()`, `exchangeOutlookCode()`, `getSyncStatus()`, `downloadIcs()` (shared with US_035 Google flow) |
| CREATE | `app/features/calendar/outlook-sync/outlook-calendar-sync.component.ts` | `OutlookCalendarSyncComponent`: signal-based state, OAuth redirect, ICS download, all 5 status states in `@switch` template |
| CREATE | `app/features/calendar/outlook-callback/outlook-callback.component.ts` | `OutlookCallbackComponent`: reads `code`/`state` from query params, calls `exchangeOutlookCode()`, navigates to dashboard |
| MODIFY | `app/features/booking/booking-confirmation/booking-confirmation.component.ts` | Add `<app-outlook-calendar-sync [appointmentId]="...">` alongside Google sync button |
| MODIFY | `app/features/patient/dashboard/patient-dashboard.component.ts` | Add `<app-outlook-calendar-sync [appointmentId]="...">` per appointment card |
| MODIFY | `app/app.routes.ts` | Add `/calendar/outlook/callback` route (no auth guard — OAuth redirect target) |

## External References

- [Microsoft Graph API v1.0 — Create event (`POST /me/events`)](https://learn.microsoft.com/en-us/graph/api/user-post-events)
- [Microsoft Graph API v1.0 — Event resource type](https://learn.microsoft.com/en-us/graph/api/resources/event)
- [Microsoft Identity Platform — OAuth 2.0 Authorization Code Flow with PKCE](https://learn.microsoft.com/en-us/azure/active-directory/develop/v2-oauth2-auth-code-flow)
- [Angular `URL.createObjectURL()` for Blob downloads](https://developer.mozilla.org/en-US/docs/Web/API/URL/createObjectURL_static)
- [WCAG 2.2 — 4.1.3 Status Messages (`role="status"`, `aria-live`)](https://www.w3.org/TR/WCAG22/#status-messages)
- [FR-035 — Outlook sync via free API standards (spec.md#FR-035)](spec.md#FR-035)
- [FR-036 — Full appointment fields in calendar event (spec.md#FR-036)](spec.md#FR-036)
- [TR-013 — Microsoft Graph API OAuth 2.0 (design.md#TR-013)](design.md#TR-013)

## Build Commands

- Refer to: `.propel/build/frontend-build.md`

## Implementation Validation Strategy

- [ ] Unit tests pass: `OutlookCalendarSyncComponent` renders "Add to Outlook Calendar" button when `status = 'Unknown'`
- [ ] Unit tests pass: `initiateSync()` sets status to `'Initiating'` then calls `window.location.href` with the authorization URL
- [ ] Unit tests pass: `downloadIcs()` creates a Blob object URL and triggers an anchor click
- [ ] Unit tests pass: `status = 'Failed'` renders ICS download button with prominent styling and `aria-live` announcement
- [ ] Unit tests pass: `status = 'Revoked'` renders "Reconnect Outlook" button that re-initiates the OAuth flow
- [ ] `OutlookCallbackComponent` navigates to `/patient/dashboard?calendarError=outlook` on API error
- [ ] `/calendar/outlook/callback` route accessible without authentication guard (OAuth redirect target)
- [ ] **[UI Tasks]** Visual comparison against wireframe at 375px, 768px, 1440px (when wireframe is available)
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment (when wireframe is available)

## Implementation Checklist

- [ ] Create `OutlookCalendarSyncComponent` (standalone, `OnPush`): `@Input appointmentId`; `ngOnInit` calls `getSyncStatus()` to restore existing sync state; `@switch` template for 5 states (`Unknown`, `Initiating`, `Synced`, `Failed`, `Revoked`); `takeUntilDestroyed()` on all subscriptions (AC-1, AC-2, AC-4)
- [ ] `initiateSync()` calls `initiateOutlookSync(appointmentId)` then redirects via `window.location.href`; sets `status = 'Initiating'` optimistically; sets `status = 'Failed'` on error
- [ ] `downloadIcs()` calls `GET /api/calendar/ics` with `responseType: 'blob'`; creates transient anchor with `URL.createObjectURL(blob)`, triggers programmatic click, then calls `URL.revokeObjectURL()` to prevent memory leak (AC-3)
- [ ] `Failed` state renders ICS download button prominently with `aria-live="polite"` fallback announcement; `Revoked` state renders "Reconnect Outlook" button re-invoking `initiateSync()` (AC-4, edge case consent revoked)
- [ ] Create `OutlookCallbackComponent`: reads `code` + `state` from `queryParamMap`; calls `exchangeOutlookCode()`; on success → `router.navigate(['/patient/dashboard'])`; on error → `router.navigate` with `calendarError=outlook` query param; no auth guard on route (OAuth redirect)
- [ ] `CalendarSyncService.downloadIcs()` uses `HttpClient` with `responseType: 'blob'`; `initiateOutlookSync()` sends `appointmentId` in POST body; `getSyncStatus()` uses `GET` with `provider` query param — all calls carry Bearer token via `AuthInterceptor` (US_011)
