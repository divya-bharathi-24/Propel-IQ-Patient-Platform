# Task - task_001_fe_adhoc_reminder_ui

## Requirement Reference

- **User Story:** us_034 — Manual Ad-Hoc Reminder Trigger & Delivery Logging
- **Story Location:** `.propel/context/tasks/EP-006/us_034/us_034.md`
- **Acceptance Criteria:**
  - AC-1: "Send Reminder Now" button dispatches an immediate email and SMS reminder when clicked from the appointment detail view
  - AC-3: After the action completes, the appointment detail view shows "Last reminder sent: [timestamp] by [staff name]" confirmation
  - AC-4: On delivery failure, the UI surfaces the failure reason and provides a Retry action
- **Edge Cases:**
  - Cancelled appointment: backend returns 422 — UI shows "Cannot send reminders for cancelled appointments" error toast
  - Debounce cooldown: backend returns 429 with seconds remaining — UI shows "Reminder recently sent — retry available in X minutes" inline warning

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value |
| ---------------------- | ----- |
| **UI Impact**          | Yes   |
| **Figma URL**          | N/A   |
| **Wireframe Status**   | PENDING |
| **Wireframe Type**     | N/A   |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-ADHOC-REMINDER-detail.[html\|png\|jpg]` or provide external URL |
| **Screen Spec**        | N/A (figma_spec.md not yet generated) |
| **UXR Requirements**   | N/A (figma_spec.md not yet generated) |
| **Design Tokens**      | N/A (designsystem.md not yet generated) |

---

## Applicable Technology Stack

| Layer              | Technology            | Version |
| ------------------ | --------------------- | ------- |
| Frontend           | Angular               | 18.x    |
| Frontend State     | NgRx Signals          | 18.x    |
| Frontend UI        | Angular Material      | 18.x    |
| Frontend Routing   | Angular Router        | 18.x    |
| HTTP Client        | Angular HttpClient    | 18.x    |
| Testing — Unit     | Jest / Angular Testing Library | — |
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

Implement the Angular 18 UI layer for the manual ad-hoc reminder trigger feature on the Staff appointment detail view. The deliverable is a `SendReminderNowButtonComponent` integrated into the existing (or new) `AppointmentDetailComponent`, a `StaffReminderService` for API communication, and clear confirmation/error/cooldown feedback driven by Angular Signals.

The UI must handle four distinct states:
1. **Idle** — "Send Reminder Now" button active
2. **Loading** — button disabled with spinner during API call
3. **Success** — inline confirmation "Last reminder sent: [timestamp] by [staff name]"
4. **Error** — inline error panel with failure reason and Retry button

Debounce enforcement happens server-side; the frontend simply interprets the 429 response to render the cooldown message. All interactive states comply with WCAG 2.2 AA (color is never the sole indicator; loading state announces to screen readers via `aria-live`).

---

## Dependent Tasks

- **EP-006/us_034/task_002_be_adhoc_reminder_api** — `POST /api/staff/appointments/{appointmentId}/reminders/trigger` and extended `GET /api/staff/appointments/{id}` (with `lastManualReminder`) must be in place before UI integration
- **EP-006/us_033** — SendGrid/Twilio infrastructure and Notification entity required (backend dependency)
- **EP-001/us_011** — Staff-role JWT and `StaffGuard` on appointment routes

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `SendReminderNowButtonComponent` (standalone) | `client/src/app/features/staff/appointments/appointment-detail/send-reminder-now-button/send-reminder-now-button.component.ts` |
| CREATE | `StaffReminderService` | `client/src/app/core/services/staff-reminder.service.ts` |
| MODIFY | `AppointmentDetailComponent` | Add reminder button, confirmation panel, and error panel to template and component logic |
| MODIFY | `AppointmentDetailStore` (NgRx Signals) | Add `reminderState: Signal<'idle' \| 'loading' \| 'success' \| 'error' \| 'cooldown'>`, `lastManualReminder: Signal<LastManualReminderDto \| null>`, `cooldownSecondsRemaining: Signal<number>`, `reminderErrorReason: Signal<string \| null>` |

---

## Implementation Plan

1. **`StaffReminderService`** — Angular `HttpClient` wrapper:
   ```typescript
   triggerManualReminder(appointmentId: string): Observable<TriggerReminderResponseDto> {
     return this.http.post<TriggerReminderResponseDto>(
       `/api/staff/appointments/${appointmentId}/reminders/trigger`, {}
     );
   }
   ```
   - Typed DTOs:
     ```typescript
     interface TriggerReminderResponseDto {
       sentAt: string;          // ISO UTC
       triggeredByStaffName: string;
     }
     interface ReminderCooldownErrorDto {
       retryAfterSeconds: number;
     }
     ```
   - Error interception: map HTTP 422 → `{ type: 'CANCELLED_APPOINTMENT' }`, 429 → `{ type: 'COOLDOWN', retryAfterSeconds }`, 5xx → generic failure

2. **`SendReminderNowButtonComponent`** (standalone input/output-driven):
   - Inputs: `@Input() disabled: boolean` (true when cooldown or loading)
   - Outputs: `@Output() sendClicked = new EventEmitter<void>()`
   - Template: `<button mat-raised-button color="primary" [disabled]="disabled" [attr.aria-busy]="isLoading" (click)="sendClicked.emit()">{{ isLoading ? '' : 'Send Reminder Now' }}<mat-spinner *ngIf="isLoading" diameter="18" /></button>`
   - The `aria-live="polite"` region on the parent component announces state transitions to screen readers

3. **`AppointmentDetailStore` signal extensions** (NgRx Signals):
   - `reminderState: Signal<'idle' | 'loading' | 'success' | 'error' | 'cooldown'>` — drives UI branch rendering
   - `lastManualReminder: Signal<LastManualReminderDto | null>` — populated from appointment detail response (`GET /api/staff/appointments/{id}`) and updated on success
   - `cooldownSecondsRemaining: Signal<number>` — set from 429 `retryAfterSeconds`; drives "retry available in X minutes" display
   - `reminderErrorReason: Signal<string | null>` — set on non-cooldown failure; displayed in error panel
   - `triggerReminder(appointmentId: string)` method: sets state to 'loading', calls service, updates state on response/error

4. **`AppointmentDetailComponent` template additions**:
   - Add `<app-send-reminder-now-button>` bound to store's `reminderState` and `triggerReminder()`
   - Success panel: `<mat-card *ngIf="reminderState() === 'success'" class="reminder-confirmation">Last reminder sent: {{ lastManualReminder()?.sentAt | date:'medium' }} by {{ lastManualReminder()?.triggeredByStaffName }}</mat-card>`
   - Error panel: `<mat-card *ngIf="reminderState() === 'error'" class="reminder-error">{{ reminderErrorReason() }} <button mat-button (click)="triggerReminder(appointmentId)">Retry</button></mat-card>`
   - Cooldown panel: `<mat-chip *ngIf="reminderState() === 'cooldown'" class="cooldown-warning">Reminder recently sent — retry available in {{ cooldownSecondsRemaining() | cooldownMinutes }} minutes</mat-chip>`
   - `aria-live="polite"` wrapper div enclosing all reminder feedback panels

5. **Retrieve `lastManualReminder` on page load**:
   - The `GET /api/staff/appointments/{id}` response DTO is extended in task_002 to include `lastManualReminder: { sentAt: string, triggeredByStaffName: string } | null`
   - On `AppointmentDetailStore.loadAppointment()`, map this field into `lastManualReminder` signal; if present, render the confirmation panel in 'success' state

---

## Current Project State

```
client/
├── src/
│   └── app/
│       ├── core/
│       │   └── services/                     # StaffReminderService to be added here
│       └── features/
│           └── staff/
│               └── appointments/
│                   ├── appointment-detail/   # Extend with reminder UI
│                   │   └── ...
│                   └── staff-appointment.store.ts
```

> Placeholder — update actual tree once dependent task_002 is complete and route is confirmed.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `client/src/app/core/services/staff-reminder.service.ts` | Angular service wrapping `POST /api/staff/appointments/{id}/reminders/trigger` with typed error mapping |
| CREATE | `client/src/app/features/staff/appointments/appointment-detail/send-reminder-now-button/send-reminder-now-button.component.ts` | Standalone button component with loading/disabled states and ARIA support |
| MODIFY | `client/src/app/features/staff/appointments/staff-appointment.store.ts` | Add `reminderState`, `lastManualReminder`, `cooldownSecondsRemaining`, `reminderErrorReason` signals and `triggerReminder()` action |
| MODIFY | `client/src/app/features/staff/appointments/appointment-detail/appointment-detail.component.ts` | Integrate reminder button, confirmation panel, error panel, cooldown panel, and `aria-live` region |
| MODIFY | `client/src/app/features/staff/appointments/appointment-detail/appointment-detail.component.html` | Template additions for all four reminder UI states |

---

## External References

- [Angular 18 Signals documentation](https://angular.dev/guide/signals)
- [NgRx Signals documentation](https://ngrx.io/guide/signals)
- [Angular Material Button](https://material.angular.io/components/button/overview)
- [Angular Material Chips](https://material.angular.io/components/chips/overview)
- [Angular Material Progress Spinner](https://material.angular.io/components/progress-spinner/overview)
- [WCAG 2.2 AA — aria-live regions](https://www.w3.org/WAI/WCAG22/Understanding/status-messages.html)
- [Angular HttpClient error handling](https://angular.dev/guide/http/making-requests#handling-request-failure)

---

## Build Commands

- Frontend build: `ng build --configuration production` (from `client/` folder)
- Frontend serve: `ng serve`
- Frontend tests: `ng test`

---

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] "Send Reminder Now" button renders in idle state on appointment detail view
- [ ] Button shows spinner and is disabled during API call (loading state)
- [ ] Success panel displays correct timestamp and staff name after API 200 response
- [ ] Cooldown panel displays correct minutes remaining after API 429 response
- [ ] Error panel displays failure reason and Retry button after API 5xx response
- [ ] 422 error message "Cannot send reminders for cancelled appointments" is surfaced correctly
- [ ] `aria-live` region announces state changes (verified with screen reader or Lighthouse accessibility audit)

---

## Implementation Checklist

- [ ] Create `StaffReminderService` with `triggerManualReminder()` returning `Observable<TriggerReminderResponseDto>` and typed error mapping for 422/429/5xx
- [ ] Create standalone `SendReminderNowButtonComponent` with loading/disabled state, ARIA attributes, and `sendClicked` output
- [ ] Extend `AppointmentDetailStore` with `reminderState`, `lastManualReminder`, `cooldownSecondsRemaining`, `reminderErrorReason` signals and `triggerReminder()` action
- [ ] Integrate reminder button into `AppointmentDetailComponent` bound to store action
- [ ] Add success confirmation panel rendering `lastManualReminder` (sentAt formatted + staff name)
- [ ] Add cooldown warning panel with minutes-remaining display (from `cooldownSecondsRemaining`)
- [ ] Add error panel with `reminderErrorReason` text and Retry button
- [ ] Wrap all feedback panels in `aria-live="polite"` region; validate WCAG 2.2 AA compliance
