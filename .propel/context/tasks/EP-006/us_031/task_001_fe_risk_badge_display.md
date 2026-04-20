# Task - task_001_fe_risk_badge_display

## Requirement Reference

- **User Story:** us_031 — No-Show Risk Score Calculation & Color-Coded Staff Display
- **Story Location:** `.propel/context/tasks/EP-006/us_031/us_031.md`
- **Acceptance Criteria:**
  - AC-2: Staff appointment management list displays a color-coded risk badge per appointment — green for Low, amber for Medium, red for High
  - AC-4: When a recalculation updates the risk score, the badge updates to reflect the new severity label on the next list load
- **Edge Cases:**
  - No `NoShowRisk` record yet (calculation job not yet run): badge shows neutral grey "Pending" state — not a missing badge
  - Missing data default (Medium): badge correctly renders amber for `severity = "Medium"` with score = 0.5

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value |
| ---------------------- | ----- |
| **UI Impact**          | Yes   |
| **Figma URL**          | N/A   |
| **Wireframe Status**   | PENDING |
| **Wireframe Type**     | N/A   |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-RISK-DISPLAY-staff.[html\|png\|jpg]` or provide external URL |
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
| AI/ML              | N/A (badge consumes pre-computed score from API) | N/A |
| Mobile             | N/A                   | N/A     |

> All code and libraries MUST be compatible with versions above.

---

## AI References (AI Tasks Only)

| Reference Type           | Value |
| ------------------------ | ----- |
| **AI Impact**            | No (FE only renders pre-computed risk severity from API response) |
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

Implement the Angular 18 risk-score display layer for the Staff appointment management interface. The core deliverable is a reusable `RiskBadgeComponent` that renders a color-coded badge chip for Low / Medium / High risk, and integration into the existing (or new) `StaffAppointmentListComponent` to show the badge alongside each appointment row.

The component must comply with WCAG 2.2 AA — color is not the sole differentiator; the text label (Low / Medium / High) is always visible, and `aria-label` carries the full risk description for screen readers.

---

## Dependent Tasks

- **EP-006/us_031 task_002_be_noshow_risk_engine** — `GET /api/staff/appointments` must return embedded `noShowRisk` object with `{ score, severity, factors, calculatedAt }` or `null` (not yet calculated)
- **US_011 (EP-001)** — Staff-role JWT required; `StaffGuard` on appointment management route

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `RiskBadgeComponent` (standalone, shareable) | `client/src/app/shared/components/risk-badge/risk-badge.component.ts` |
| CREATE | `StaffAppointmentListComponent` (or extend if exists) | `client/src/app/features/staff/appointments/staff-appointment-list/` |
| CREATE | `StaffAppointmentStore` (NgRx Signals) | `client/src/app/features/staff/appointments/staff-appointment.store.ts` |
| CREATE | `StaffAppointmentService` (HttpClient wrapper) | `client/src/app/core/services/staff-appointment.service.ts` |
| MODIFY | `StaffModule` routing | Add `/staff/appointments` route → `StaffAppointmentListComponent` (lazy-loaded, `StaffGuard`) |

---

## Implementation Plan

1. **`RiskBadgeComponent`** (standalone input-driven):
   - `@Input() severity: 'Low' | 'Medium' | 'High' | null` — `null` renders a neutral "Pending" grey chip
   - `@Input() score: number | null` — displayed in `matTooltip` as percentage e.g. "Risk score: 72%"
   - CSS class binding:
     ```typescript
     get badgeClass(): string {
       return { Low: 'risk-low', Medium: 'risk-medium', High: 'risk-high' }[this.severity ?? ''] ?? 'risk-pending';
     }
     ```
   - Template: `<mat-chip [class]="badgeClass" [matTooltip]="tooltipText" [attr.aria-label]="ariaLabel">{{ severity ?? 'Pending' }}</mat-chip>`
   - `ariaLabel`: `"No-show risk: {severity}, score {score}%"` — full description for screen readers (WCAG 2.2 AA)
   - SCSS: `.risk-low { background: #2E7D32; color: white }` / `.risk-medium { background: #F57C00; color: white }` / `.risk-high { background: #C62828; color: white }` / `.risk-pending { background: #9E9E9E; color: white }`

2. **`StaffAppointmentStore`** (NgRx Signals):
   - `appointments: Signal<StaffAppointmentDto[]>` — each DTO includes `noShowRisk: NoShowRiskDto | null`
   - `loadingState: Signal<'idle' | 'loading' | 'loaded' | 'error'>`
   - `selectedDate: Signal<string>` — ISO date string (today by default)
   - `loadAppointments(date: string)` — calls `StaffAppointmentService.getAppointments(date)`, updates signal

3. **`StaffAppointmentService`**:
   - `getAppointments(date: string): Observable<StaffAppointmentDto[]>` → `GET /api/staff/appointments?date={date}`
   - `StaffAppointmentDto`: `{ id, patientName, specialty, timeSlot, status, noShowRisk: { score, severity, factors, calculatedAt } | null }`

4. **`StaffAppointmentListComponent`**:
   - Renders a `mat-table` with columns: time, patient, specialty, status, risk
   - Risk column: `<app-risk-badge [severity]="row.noShowRisk?.severity" [score]="row.noShowRisk?.score" />`
   - Date picker header (Angular Material `mat-datepicker`) to change `selectedDate` in store
   - Loading skeleton: `mat-progress-bar` while `loadingState = 'loading'`
   - Empty state: "No appointments for this date" message when `appointments().length === 0`

5. **Accessibility** (WCAG 2.2 AA, FR-029):
   - Risk column header: `<th mat-header-cell aria-label="No-show risk level">`
   - `RiskBadgeComponent` `mat-chip`: `role="status"` + full `aria-label` (color is supplementary, not sole indicator)
   - Date picker navigation: standard `mat-datepicker` provides keyboard support out of the box

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

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `client/src/app/shared/components/risk-badge/risk-badge.component.ts` | Standalone risk chip: color class binding, matTooltip with score %, aria-label, "Pending" null state |
| CREATE | `client/src/app/shared/components/risk-badge/risk-badge.component.scss` | Color classes: `.risk-low` (green), `.risk-medium` (amber), `.risk-high` (red), `.risk-pending` (grey) |
| CREATE | `client/src/app/features/staff/appointments/staff-appointment-list/staff-appointment-list.component.ts` | mat-table with risk column; date picker header; loading/empty states |
| CREATE | `client/src/app/features/staff/appointments/staff-appointment.store.ts` | NgRx Signals: appointments, loadingState, selectedDate, loadAppointments() |
| CREATE | `client/src/app/core/services/staff-appointment.service.ts` | HttpClient GET /api/staff/appointments?date= |
| MODIFY | `client/src/app/features/staff/staff.routes.ts` | Add `/staff/appointments` route → `StaffAppointmentListComponent`; guard with `StaffGuard` |

---

## External References

- [Angular Material 18 — MatChip / MatChipsModule](https://material.angular.io/components/chips/overview)
- [Angular Material 18 — MatTable](https://material.angular.io/components/table/overview)
- [Angular Material 18 — MatDatepicker](https://material.angular.io/components/datepicker/overview)
- [NgRx Signals 18 — Signal Store](https://ngrx.io/guide/signals/signal-store)
- [WCAG 2.2 AA — Success Criterion 1.4.1 Use of Color (color not sole differentiator)](https://www.w3.org/TR/WCAG22/#use-of-color)
- [WCAG 2.2 AA — Success Criterion 4.1.2 Name, Role, Value (aria-label on badge)](https://www.w3.org/TR/WCAG22/#name-role-value)
- [Angular HttpClient — typed request with generic response](https://angular.dev/guide/http/making-requests)

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

- [ ] `/staff/appointments` route accessible only with Staff JWT; Patient JWT → redirected to login
- [ ] `GET /api/staff/appointments?date=2026-04-20` response is mapped into `StaffAppointmentStore.appointments` signal
- [ ] `RiskBadgeComponent` renders green chip for `severity = "Low"`, amber for `"Medium"`, red for `"High"`, grey for `null`
- [ ] Badge `aria-label` is set to `"No-show risk: High, score 78%"` (confirmed via DOM attribute in test)
- [ ] `matTooltip` on badge shows score percentage on hover
- [ ] No `noShowRisk` field in response (`null`): badge shows "Pending" in grey — no broken layout
- [ ] Date picker change triggers new `loadAppointments(newDate)` call
- [ ] `mat-progress-bar` visible while `loadingState = 'loading'`

---

## Implementation Checklist

- [ ] Create `RiskBadgeComponent`: `@Input() severity` + `@Input() score`; CSS class binding per severity; `matTooltip` with score %; `aria-label` full description; "Pending" grey state for `null`
- [ ] Create SCSS for risk badge: `.risk-low` (green #2E7D32), `.risk-medium` (amber #F57C00), `.risk-high` (red #C62828), `.risk-pending` (grey #9E9E9E) — all with white text for contrast ratio ≥ 4.5:1 (WCAG AA)
- [ ] Create `StaffAppointmentStore` (NgRx Signals): `appointments`, `loadingState`, `selectedDate` signals; `loadAppointments(date)` action
- [ ] Create `StaffAppointmentService`: `getAppointments(date): Observable<StaffAppointmentDto[]>` → `GET /api/staff/appointments?date={date}`
- [ ] Create `StaffAppointmentListComponent`: `mat-table` with time, patient, specialty, status, risk columns; risk column embeds `<app-risk-badge />`; date picker header; loading and empty states
- [ ] Register `/staff/appointments` lazy route in `staff.routes.ts` with `StaffGuard`
- [ ] Validate WCAG 2.2 AA: text label always visible on badge; `aria-label` set programmatically; color contrast ratio ≥ 4.5:1 for all four badge states
