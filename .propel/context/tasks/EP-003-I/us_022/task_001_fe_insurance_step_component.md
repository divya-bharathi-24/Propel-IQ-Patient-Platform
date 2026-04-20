# Task - TASK_001

## Requirement Reference

- **User Story**: US_022 — Insurance Soft Pre-Check & Status Display
- **Story Location**: `.propel/context/tasks/EP-003-I/us_022/us_022.md`
- **Acceptance Criteria**:
  - AC-1: Given I enter my insurer name and member ID in the booking flow, When the insurance pre-check runs, Then the result is one of: "Verified" (match found), "Not Recognized" (no match), or "Incomplete" (missing insurer name or member ID).
  - AC-2: Given the insurance check returns "Not Recognized", When the result is displayed, Then guidance text explains what steps to take and the booking flow proceeds to the confirmation step without blocking.
  - AC-4: Given I choose to skip the insurance step entirely, When I proceed to confirmation, Then the booking completes with an InsuranceValidation record marked as `result = Incomplete` and the status displayed on my dashboard.
- **Edge Cases**:
  - Insurance pre-check service unavailable: Display "Check Pending" badge (blue/info); booking flow proceeds without blocking.
  - Partial insurance information (name only, no member ID): System returns "Incomplete" with a specific prompt identifying the missing field.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | PENDING |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-insurance-check.[html\|png\|jpg]` or provide external URL |
| **Screen Spec** | N/A (figma_spec.md not yet generated) |
| **UXR Requirements** | N/A (figma_spec.md not yet generated) |
| **Design Tokens** | N/A (designsystem.md not yet generated) |

> **Wireframe Status = PENDING:** When wireframe becomes available, implementation MUST:
> - Match layout, spacing, typography, and colors from the wireframe
> - Implement all 5 states: Default (empty form), Loading (API call in-flight), Verified (green badge), Not Recognized / Incomplete (amber badge), Check Pending (blue/info badge)
> - Validate implementation against wireframe at breakpoints: 375px, 768px, 1440px
> - Run `/analyze-ux` after implementation to verify pixel-perfect alignment

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Frontend | Angular | 18.x |
| Frontend State | NgRx Signals | 18.x |
| Backend | ASP.NET Core Web API | .NET 9 |
| Database | PostgreSQL | 16+ |
| Library | Angular Reactive Forms | 18.x |
| Library | Angular Router | 18.x |
| AI/ML | N/A | N/A |
| Vector Store | N/A | N/A |
| AI Gateway | N/A | N/A |
| Mobile | N/A | N/A |

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

Implement the Angular 18 `InsuranceStepComponent` — the dedicated insurance pre-check step (Step 3) within the booking wizard. This standalone component:

- Renders two reactive form fields: **Insurer Name** (text) and **Member ID** (text). Neither field is required at the component level, because an empty submit classifies as "Incomplete" (the `Incomplete` classification is determined server-side by the BE pre-check, not by Angular validators).
- Provides a **"Check Insurance"** button that calls `POST /api/insurance/pre-check` with the current field values. Displays a loading spinner during the API call.
- Renders a **status badge** based on the API response:
  - `Verified` → green badge (`✓ Verified`)
  - `NotRecognized` → amber badge (`⚠ Not Recognized`)
  - `Incomplete` → amber badge (`⚠ Incomplete — <guidance text>`)
  - `CheckPending` → blue/info badge (`ℹ Insurance Check Pending`)
- Renders **guidance text** below the badge for each non-Verified status (FR-039). Guidance text is returned by the BE endpoint and should not be hardcoded in the FE.
- A **"Skip this step"** link allows the patient to proceed without checking insurance. Clicking skip emits an `InsuranceSkipped` event to the parent wizard, which the booking command interprets as `result = Incomplete`.
- A **"Continue to Confirmation"** button is always enabled regardless of insurance status (FR-040 — non-blocking). It emits the current insurance result (`InsuranceCheckResult | null`) to the parent wizard state signal.
- The insurance step result is stored in the parent `BookingWizardStateSignal` (`insuranceResult: InsuranceCheckResult | null`) and forwarded as part of the booking confirmation payload to `POST /api/appointments/book`.
- A reusable `InsuranceStatusBadgeComponent` is extracted for reuse on the patient dashboard (AC-4 — status displayed post-booking).

## Dependent Tasks

- `EP-003-I/us_019/task_001_fe_booking_wizard_component.md` — Booking wizard shell must exist and expose a slot for Step 3 before `InsuranceStepComponent` can be embedded.
- `EP-003-I/us_022/task_002_be_insurance_precheck_endpoint.md` — `POST /api/insurance/pre-check` must be implemented before the "Check Insurance" button is functional.
- `EP-DATA/us_009` — `DummyInsurers` seed data must exist for any status other than `Incomplete` to be returned.

## Impacted Components

| Component | Action | Module / Project |
|-----------|--------|-----------------|
| `InsuranceStepComponent` | CREATE | `app/appointment/booking/steps/insurance-step/` |
| `InsuranceStatusBadgeComponent` | CREATE | `app/shared/components/insurance-status-badge/` |
| `InsuranceService` | CREATE | `app/appointment/booking/services/` |
| `BookingWizardStateSignal` | MODIFY | `app/appointment/booking/state/booking-wizard.state.ts` — add `insuranceResult` signal |
| `BookingWizardComponent` (Step 3 slot) | MODIFY | `app/appointment/booking/` — embed `InsuranceStepComponent` as Step 3 |
| `PatientDashboardComponent` | MODIFY | `app/patient/dashboard/` — embed `InsuranceStatusBadgeComponent` for last insurance result |

## Implementation Plan

1. **`InsuranceService`** — Create an Angular service that wraps `POST /api/insurance/pre-check`. Accepts `{ providerName: string, insuranceId: string }` and returns `Observable<InsurancePreCheckResponse>`. Include error handling: on `HttpErrorResponse` with any status, return `{ status: 'CheckPending', guidance: 'Insurance check is temporarily unavailable. Your booking will proceed.' }` (graceful degradation — NFR-018).

2. **`InsuranceStepComponent` — Form Setup** — Define a reactive `FormGroup` with two `FormControl<string>` fields: `insurerName` and `memberId`. No required validators — empty fields are sent to the BE which classifies them as `Incomplete`. Initialize with empty strings.

3. **"Check Insurance" Button** — On click, set `checking = signal(true)`, call `InsuranceService.check({ providerName, insuranceId })`. On response, set `insuranceResult = signal(response)` and `checking = false`. On error, set `insuranceResult = { status: 'CheckPending', guidance: '...' }` and `checking = false`.

4. **Status Badge Rendering** — Use `@if` / `@switch` on `insuranceResult().status` to render `InsuranceStatusBadgeComponent` with the correct colour variant and guidance text. Badge is hidden until the first check completes or skip is selected.

5. **"Skip" Link** — On click, emit `{ status: 'Incomplete', guidance: 'Insurance information was not provided.' }` to the parent wizard via `Output` event emitter `insuranceChecked`. Mark `skipped = signal(true)` to show a neutral "Skipped — status will be recorded as Incomplete" message in place of the badge.

6. **"Continue to Confirmation" Button** — Always enabled (no `[disabled]` binding). On click, emit current `insuranceResult()` (or `null` if never checked and not skipped) via `insuranceChecked`. Parent wizard advances to Step 4 (Confirmation) regardless of status.

7. **`InsuranceStatusBadgeComponent`** — Standalone component accepting `@Input() result: InsuranceCheckResult`. Renders a colour-coded chip (`Verified` = green, `NotRecognized` / `Incomplete` = amber, `CheckPending` = blue). Used both in the booking wizard step and on the patient dashboard.

8. **`BookingWizardStateSignal` Update** — Add `insuranceResult: WritableSignal<InsuranceCheckResult | null>` to the NgRx Signal store. Set this signal when the wizard receives the `insuranceChecked` event from `InsuranceStepComponent`.

9. **Accessibility** — All form controls have associated `<label>` elements with `for` binding. Status badge uses `role="status"` and `aria-live="polite"` so screen readers announce the result after the API call completes. Guidance text is associated to the badge via `aria-describedby`.

## Current Project State

```
app/
└── appointment/
    └── booking/
        ├── booking-wizard.component.ts   ← US_019 delivered (Step 3 slot available)
        ├── state/
        │   └── booking-wizard.state.ts   ← MODIFY (add insuranceResult signal)
        ├── services/
        │   └── insurance.service.ts      ← NEW
        └── steps/
            └── insurance-step/           ← NEW
                ├── insurance-step.component.ts
                ├── insurance-step.component.html
                └── insurance-step.component.scss
└── shared/
    └── components/
        └── insurance-status-badge/       ← NEW (reusable)
            ├── insurance-status-badge.component.ts
            ├── insurance-status-badge.component.html
            └── insurance-status-badge.component.scss
```

> **Note**: Update tree after `task_002` (BE) completes to confirm API response contract.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `app/appointment/booking/steps/insurance-step/insurance-step.component.ts` | Standalone component — insurance form fields, "Check Insurance" button, "Skip" link, "Continue" button, loading state, signal-based result |
| CREATE | `app/appointment/booking/steps/insurance-step/insurance-step.component.html` | Template — reactive form, status badge slot, guidance text, skip/continue controls |
| CREATE | `app/appointment/booking/steps/insurance-step/insurance-step.component.scss` | Scoped styles — badge colour variants (green/amber/blue), form layout, loading spinner |
| CREATE | `app/appointment/booking/services/insurance.service.ts` | Angular service — wraps `POST /api/insurance/pre-check`; graceful degradation on error |
| CREATE | `app/shared/components/insurance-status-badge/insurance-status-badge.component.ts` | Reusable status badge — `@Input() result: InsuranceCheckResult`; colour-coded chip |
| CREATE | `app/shared/components/insurance-status-badge/insurance-status-badge.component.html` | Badge template — colour-coded chip with icon and `aria-live` |
| CREATE | `app/shared/models/insurance.models.ts` | TypeScript interfaces: `InsurancePreCheckRequest`, `InsurancePreCheckResponse`, `InsuranceCheckResult`, `InsuranceStatus` enum |
| MODIFY | `app/appointment/booking/state/booking-wizard.state.ts` | Add `insuranceResult: WritableSignal<InsuranceCheckResult \| null>` |
| MODIFY | `app/appointment/booking/booking-wizard.component.ts` | Embed `InsuranceStepComponent` as Step 3; handle `insuranceChecked` event; update state signal |
| MODIFY | `app/patient/dashboard/patient-dashboard.component.ts` | Embed `InsuranceStatusBadgeComponent` to display last `InsuranceValidation.result` from dashboard API response |

## External References

- [Angular 18 Reactive Forms — FormGroup, FormControl](https://angular.dev/guide/forms/reactive-forms)
- [Angular 18 Signals — signal(), computed(), effect()](https://angular.dev/guide/signals)
- [Angular 18 — @if / @switch control flow](https://angular.dev/guide/templates/control-flow)
- [Angular HttpClient — error handling with catchError](https://angular.dev/guide/http/making-requests#handling-request-failure)
- [WCAG 2.2 AA — aria-live regions for dynamic status](https://www.w3.org/WAI/ARIA/apg/practices/live-regions/)
- [NgRx Signals — WritableSignal, SignalStore](https://ngrx.io/guide/signals/signal-store)

## Build Commands

- Refer to [Angular build commands](.propel/build/angular-build.md)
- `ng build --configuration production` — production build
- `ng test --watch=false --code-coverage` — unit tests

## Implementation Validation Strategy

- [ ] Unit tests pass — `InsuranceStepComponent` with mocked `InsuranceService` covering Verified, NotRecognized, Incomplete, CheckPending response scenarios
- [ ] "Check Insurance" button shows loading spinner during API call and hides on completion
- [ ] Status badge renders with correct colour variant and guidance text for all 4 statuses (FR-039)
- [ ] "Continue to Confirmation" button always enabled regardless of insurance status (FR-040)
- [ ] "Skip" emits `Incomplete` status and shows neutral skip message in place of badge
- [ ] API error → `CheckPending` badge rendered; booking flow unblocked (NFR-018 graceful degradation)
- [ ] `InsuranceStatusBadgeComponent` renders correctly on patient dashboard post-booking (AC-4)
- [ ] Status badge announces result via `aria-live="polite"` — verified with screen reader test
- [ ] **[UI Tasks]** Visual comparison against wireframe at 375px, 768px, 1440px (once wireframe available)
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment (once wireframe available)

## Implementation Checklist

- [ ] Create `InsuranceService` with `POST /api/insurance/pre-check` call and `catchError` → `CheckPending` fallback
- [ ] Define `InsuranceCheckResult`, `InsuranceStatus` enum, and request/response interfaces in `insurance.models.ts`
- [ ] Implement `InsuranceStepComponent` reactive form with `insurerName` and `memberId` controls (no required validators)
- [ ] Implement "Check Insurance" button with `checking` signal, API dispatch, and result signal update
- [ ] Implement status badge rendering via `@switch` on result status with colour variants and guidance text (FR-039)
- [ ] Implement "Skip" link emitting `Incomplete` status to parent and showing skip confirmation message
- [ ] Implement always-enabled "Continue to Confirmation" button emitting current result (FR-040)
- [ ] Create `InsuranceStatusBadgeComponent` with `@Input() result` and `aria-live="polite"` for reuse on dashboard
- [ ] Add `insuranceResult` signal to `BookingWizardStateSignal` and wire `insuranceChecked` output from Step 3
- [ ] **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- [ ] **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
