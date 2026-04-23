# Task - task_001_fe_google_calendar_sync_ui

## Requirement Reference

- **User Story:** us_035 — Google Calendar OAuth 2.0 Appointment Sync
- **Story Location:** `.propel/context/tasks/EP-007/us_035/us_035.md`
- **Acceptance Criteria:**
  - AC-1: "Add to Google Calendar" button on booking confirmation and patient dashboard initiates the OAuth 2.0 authorization flow; navigates the patient to Google's consent screen
  - AC-2: After successful OAuth + event creation, a `CalendarSyncStatusComponent` shows `syncStatus = Synced` with a clickable link to the Google Calendar event
  - AC-3: When the patient declines OAuth authorization, a guidance message is shown explaining how to grant permission in the future; booking confirmation remains visible and unaffected
  - AC-4: When Google Calendar API is unavailable (`syncStatus = Failed`), an ICS download button is offered as a fallback; a "Retry" option is visible
- **Edge Cases:**
  - Token expiry during re-auth: patient is shown "Your Google connection has expired — please reconnect" prompt with a "Reconnect Google" button that re-initiates the OAuth flow
  - Duplicate sync attempt: FE detects `syncStatus = Synced` already present → "Update Calendar Event" button replaces "Add to Google Calendar" button (no duplicate creation prompt needed; BE handles the upsert)

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value                                                                                                                                |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| **UI Impact**          | Yes                                                                                                                                  |
| **Figma URL**          | N/A                                                                                                                                  |
| **Wireframe Status**   | PENDING                                                                                                                              |
| **Wireframe Type**     | N/A                                                                                                                                  |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-CALENDAR-SYNC-confirmation.[html\|png\|jpg]` or provide external URL |
| **Screen Spec**        | N/A (figma_spec.md not yet generated)                                                                                                |
| **UXR Requirements**   | N/A (figma_spec.md not yet generated)                                                                                                |
| **Design Tokens**      | N/A (designsystem.md not yet generated)                                                                                              |

---

## Applicable Technology Stack

| Layer            | Technology                     | Version |
| ---------------- | ------------------------------ | ------- |
| Frontend         | Angular                        | 18.x    |
| Frontend State   | NgRx Signals                   | 18.x    |
| Frontend UI      | Angular Material               | 18.x    |
| Frontend Routing | Angular Router                 | 18.x    |
| HTTP Client      | Angular HttpClient             | 18.x    |
| Testing — Unit   | Jest / Angular Testing Library | —       |
| AI/ML            | N/A                            | N/A     |
| Mobile           | N/A                            | N/A     |

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

Implement the Angular 18 Google Calendar sync UI layer. The FE does not participate directly in the OAuth token exchange (server-side flow) — it initiates the flow by navigating to the BE-generated authorization URL and reacts to the result after the BE callback redirects back.

**Key components:**

- `CalendarSyncButtonComponent` — context-aware button: "Add to Google Calendar" (no sync), "Update Calendar Event" (already synced), "Reconnect Google" (token expired); disabled during `pending` state
- `CalendarSyncStatusComponent` — renders sync status badge + event link (Synced) or ICS fallback + Retry button (Failed) or guidance message (Declined)
- `CalendarSyncService` — HTTP client: `initiateGoogleSync(appointmentId)`, `getSyncStatus(appointmentId)`, `downloadIcs(appointmentId)`, `retrySyncRelink(appointmentId)`
- `CalendarSyncStore` (NgRx Signals) — `syncStatus`, `eventLink`, `isSyncing` signals per appointment
- OAuth result handling: BE redirects back to `/appointments/confirmation?calendarResult=success|failed|declined` — Angular Router reads query params and updates the store

---

## Dependent Tasks

- **EP-007/us_035 task_002_be_google_calendar_oauth_sync** — `GET /api/calendar/google/auth`, `GET /api/calendar/google/status/{appointmentId}`, `GET /api/appointments/{id}/ics` must be available
- **US_019 (EP-003)** — Booking confirmation page must exist to host the "Add to Google Calendar" button
- **US_011 (EP-001)** — Patient JWT required for all calendar API calls

---

## Impacted Components

| Status | Component / Module                           | Project                                                                                             |
| ------ | -------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| CREATE | `CalendarSyncButtonComponent` (standalone)   | `client/src/app/shared/components/calendar-sync-button/`                                            |
| CREATE | `CalendarSyncStatusComponent` (standalone)   | `client/src/app/shared/components/calendar-sync-status/`                                            |
| CREATE | `CalendarSyncStore` (NgRx Signals)           | `client/src/app/features/patient/calendar/calendar-sync.store.ts`                                   |
| CREATE | `CalendarSyncService`                        | `client/src/app/core/services/calendar-sync.service.ts`                                             |
| MODIFY | Booking confirmation page component          | Add `<app-calendar-sync-button>` and `<app-calendar-sync-status>` after appointment details section |
| MODIFY | Patient dashboard appointment card component | Add `<app-calendar-sync-button>` to upcoming appointment card actions                               |

---

## Implementation Plan

1. **`CalendarSyncStore`** (NgRx Signals):
   - `syncStatus: Signal<'none' | 'pending' | 'synced' | 'failed' | 'declined' | 'expired'>`
   - `eventLink: Signal<string | null>` — Google Calendar event URL (available when `syncStatus = 'synced'`)
   - `isSyncing: Signal<boolean>`
   - `loadSyncStatus(appointmentId: string)` — calls `CalendarSyncService.getSyncStatus()` on component init
   - `handleOAuthResult(result: 'success' | 'failed' | 'declined')` — called from confirmation page on query param read

2. **`CalendarSyncService`** (HttpClient):
   - `initiateGoogleSync(appointmentId: string): void` — constructs `GET /api/calendar/google/auth?appointmentId={id}` URL and performs `window.location.href` redirect (full page redirect to trigger OAuth flow) — NOT an Angular `router.navigate()` call (OAuth must redirect the full browser window)
   - `getSyncStatus(appointmentId: string): Observable<CalendarSyncStatusDto>` → `GET /api/calendar/google/status/{appointmentId}`
   - `downloadIcs(appointmentId: string): void` — triggers `GET /api/appointments/{id}/ics` download via `<a download>` element (no Angular HttpClient call — direct browser download)
   - `retrySyncRelink(appointmentId: string): void` — calls `initiateGoogleSync` to re-run OAuth + retry

3. **`CalendarSyncButtonComponent`**:
   - `@Input() appointmentId: string`
   - Reads `store.syncStatus()` to determine button label and variant:
     - `none` → "Add to Google Calendar" (primary `mat-raised-button`)
     - `synced` → "Update Calendar Event" (secondary `mat-stroked-button`)
     - `expired` → "Reconnect Google" (warn `mat-flat-button`)
     - `pending` or `isSyncing` → disabled spinner button
   - On click: calls `service.initiateGoogleSync(appointmentId)` (full window redirect)
   - `aria-label="Sync appointment to Google Calendar"` (WCAG 2.2 AA)

4. **`CalendarSyncStatusComponent`**:
   - `@Input() appointmentId: string`
   - Renders conditionally on `store.syncStatus()`:
     - `synced`: green `mat-chip` + `<a [href]="store.eventLink()" target="_blank" rel="noopener noreferrer">View in Google Calendar</a>`
     - `failed`: red `mat-chip` "Sync failed" + "Download ICS" button + "Retry" button
     - `declined`: amber `mat-chip` "Not connected" + guidance text: "To add to Google Calendar, click 'Add to Google Calendar' and approve the calendar permission request."
     - `none`: no badge (button alone is sufficient)
   - Link to external Google Calendar event: `rel="noopener noreferrer"` prevents tab-napping (OWASP A05)

5. **OAuth result detection** (booking confirmation page):
   - On `ngOnInit`: read `ActivatedRoute.queryParamMap.get('calendarResult')` and `queryParamMap.get('appointmentId')`
   - If `calendarResult = 'success'` → `store.handleOAuthResult('success')` then `store.loadSyncStatus(appointmentId)`
   - If `calendarResult = 'failed'` → `store.handleOAuthResult('failed')`
   - If `calendarResult = 'declined'` → `store.handleOAuthResult('declined')`
   - Clean up query params from URL after reading: `router.navigate([], { queryParams: {}, replaceUrl: true })`

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no client/ scaffold yet — greenfield Angular 18 project)
```

> Update with actual `client/src/` tree after scaffold is complete.

---

## Expected Changes

| Action | File Path                                                                                                       | Description                                                                                                                               |
| ------ | --------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| CREATE | `client/src/app/shared/components/calendar-sync-button/calendar-sync-button.component.ts`                       | Context-aware OAuth initiation button; full-window redirect; disabled state during pending                                                |
| CREATE | `client/src/app/shared/components/calendar-sync-status/calendar-sync-status.component.ts`                       | Sync status badge; event link (Synced); ICS download + Retry (Failed); guidance (Declined)                                                |
| CREATE | `client/src/app/features/patient/calendar/calendar-sync.store.ts`                                               | NgRx Signals: syncStatus, eventLink, isSyncing signals; loadSyncStatus(), handleOAuthResult()                                             |
| CREATE | `client/src/app/core/services/calendar-sync.service.ts`                                                         | initiateGoogleSync (window.location.href redirect), getSyncStatus, downloadIcs (anchor download)                                          |
| MODIFY | `client/src/app/features/patient/appointments/booking-confirmation/booking-confirmation.component.ts`           | Read `calendarResult` query param on init; dispatch to CalendarSyncStore; embed CalendarSyncButtonComponent + CalendarSyncStatusComponent |
| MODIFY | `client/src/app/features/patient/appointments/upcoming-appointment-card/upcoming-appointment-card.component.ts` | Add `<app-calendar-sync-button>` to card actions                                                                                          |

---

## External References

- [Google Calendar API v3 — OAuth 2.0 scope: `https://www.googleapis.com/auth/calendar.events`](https://developers.google.com/calendar/api/auth)
- [Angular 18 — ActivatedRoute queryParamMap](https://angular.dev/guide/routing/common-router-tasks#query-parameters-and-fragments)
- [Angular Material 18 — MatChip](https://material.angular.io/components/chips/overview)
- [Angular Material 18 — MatButton variants](https://material.angular.io/components/button/overview)
- [WCAG 2.2 AA — Success Criterion 1.4.1 Use of Color](https://www.w3.org/TR/WCAG22/#use-of-color)
- [OWASP A05 — Security Misconfiguration: `rel="noopener noreferrer"` on external links](https://owasp.org/Top10/A05_2021-Security_Misconfiguration/)
- [MDN — Anchor download attribute for ICS files](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/a#download)

---

## Build Commands

```bash
# Install dependencies
npm install

# Serve Angular development server
ng serve

# Build for production
ng build --configuration production

# Run unit tests
ng test
```

---

## Implementation Validation Strategy

- [x] "Add to Google Calendar" button on booking confirmation page: clicking performs `window.location.href` to `GET /api/calendar/google/auth?appointmentId={id}` (NOT Angular router navigation)
- [x] After OAuth success: `calendarResult=success` query param → `CalendarSyncStore.syncStatus = 'synced'`; Google Calendar event link shown in `CalendarSyncStatusComponent`
- [x] After OAuth decline: `calendarResult=declined` → amber "Not connected" badge + guidance text shown; "Add to Google Calendar" button still present
- [x] `syncStatus = 'failed'`: ICS download button triggers browser download (Content-Disposition: attachment); Retry button re-initiates OAuth flow
- [x] External Google Calendar event link has `rel="noopener noreferrer"` and `target="_blank"` (security)
- [x] Duplicate sync: second visit shows "Update Calendar Event" button when `syncStatus = 'synced'`
- [x] Query params cleaned from URL after reading `calendarResult` (`replaceUrl: true`)

---

## Implementation Checklist

- [x] Create `CalendarSyncStore` (NgRx Signals): `syncStatus`, `eventLink`, `isSyncing` signals; `loadSyncStatus(appointmentId)` calls `GET /api/calendar/google/status/{id}`; `handleOAuthResult(result)` updates local signal
- [x] Create `CalendarSyncService`: `initiateGoogleSync()` → `window.location.href` redirect to `GET /api/calendar/google/auth?appointmentId=`; `getSyncStatus()` → `GET /api/calendar/google/status/{id}`; `downloadIcs()` → anchor download
- [x] Create `CalendarSyncButtonComponent`: context-aware label (Add / Update / Reconnect / disabled-spinner); `window.location.href` on click; `aria-label` on button
- [x] Create `CalendarSyncStatusComponent`: Synced → green chip + external link (`rel="noopener noreferrer"`); Failed → red chip + ICS download + Retry; Declined → amber chip + guidance text
- [x] Modify booking confirmation component: read `calendarResult` + `appointmentId` from `queryParamMap` on init; dispatch to store; clean params with `replaceUrl: true`; embed `<app-calendar-sync-button>` and `<app-calendar-sync-status>`
- [x] Modify upcoming appointment card: embed `<app-calendar-sync-button [appointmentId]="appointment.id" />`
- [x] Validate WCAG 2.2 AA: status badges include text label alongside color; external link aria-label includes destination description
