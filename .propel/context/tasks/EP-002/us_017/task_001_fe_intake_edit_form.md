# Task - TASK_001

## Requirement Reference

- **User Story**: US_017 — Patient Self-Service Intake Edit Without Duplicate Records
- **Story Location**: `.propel/context/tasks/EP-002/us_017/us_017.md`
- **Acceptance Criteria**:
  - AC-1: Given I have a previously submitted intake record, When I navigate to "Edit Intake" from the dashboard, Then all previously saved fields are pre-populated with my last submitted values.
  - AC-2: Given I modify an intake field and save, When the save operation completes, Then the existing IntakeRecord is updated (UPSERT) — no new IntakeRecord row is created — and the `completedAt` timestamp is updated.
  - AC-3: Given I edit intake without completing all required fields, When I attempt to save, Then the system saves the partial update as a draft and displays which fields remain incomplete.
  - AC-4: Given I resume editing an intake after a session timeout, When I return to the edit form, Then my draft values from before the timeout are restored from the saved draft state.
- **Edge Cases**:
  - Concurrent staff/patient edit: Show conflict warning modal with both versions for reconciliation when a 409 Conflict is returned from the API.
  - AI-to-manual mode switch during edit: All data entered in AI mode is pre-populated into manual form fields; no data loss occurs.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | PENDING |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-intake-edit.[html\|png\|jpg]` or provide external URL |
| **Screen Spec** | N/A (figma_spec.md not yet generated) |
| **UXR Requirements** | N/A (figma_spec.md not yet generated) |
| **Design Tokens** | N/A (designsystem.md not yet generated) |

> **Wireframe Status = PENDING:** When wireframe becomes available, implementation MUST:
> - Match layout, spacing, typography, and colors from the wireframe
> - Implement all 5 states: Default (pre-populated), Loading (skeleton), Empty (no prior record), Error (API failure), Validation (missing required fields highlighted)
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

Implement the Angular 18 **Edit Intake** page component that allows an authenticated patient to review and update their previously submitted intake record. The component:

- Fetches the existing `IntakeRecord` via `GET /api/intake/{appointmentId}` and pre-populates all reactive form fields.
- Restores persisted draft values via `GET /api/intake/{appointmentId}/draft` on load if a draft exists (supporting post-timeout resume — AC-4).
- Autosaves the form draft to the backend (`POST /api/intake/{appointmentId}/draft`) on field blur, enabling session-timeout recovery.
- On "Save" action, calls `PUT /api/intake/{appointmentId}` and handles: success (full update), partial save (422 with missing-field list), and concurrent conflict (409 with server version payload for reconciliation modal).
- Displays field-level incomplete indicators when the backend returns validation errors for partial saves (AC-3).
- Shows a conflict reconciliation modal when a 409 Conflict is received, presenting both the local version and the server's conflicting version side-by-side.
- Preserves data when the mode switches from AI-assisted intake to manual edit (AC edge case).

## Dependent Tasks

- `task_003_db_intake_edit_schema.md` — Database schema for `draftData`, `lastModifiedAt`, and `rowVersion` concurrency token on `IntakeRecord` must exist before backend API is functional.
- `task_002_be_intake_edit_api.md` — Backend API endpoints (`GET /api/intake/{appointmentId}`, `PUT /api/intake/{appointmentId}`, `POST /api/intake/{appointmentId}/draft`, `GET /api/intake/{appointmentId}/draft`) must be implemented and reachable.
- `EP-002/us_016` tasks — Patient dashboard must expose the "Edit Intake" navigation entry point (US_016 dependency).

## Impacted Components

| Component | Action | Module / Project |
|-----------|--------|------------------|
| `IntakeEditComponent` | CREATE | `app/patient/intake/` |
| `IntakeEditFormGroup` (Reactive Form model) | CREATE | `app/patient/intake/models/` |
| `IntakeService` | CREATE / MODIFY | `app/patient/intake/services/` |
| `IntakeConflictModalComponent` | CREATE | `app/patient/intake/components/` |
| `PatientRoutingModule` / app routes | MODIFY | `app/patient/` — add route `/intake/edit/:appointmentId` |
| `IntakeStateSignal` (NgRx Signal store) | CREATE | `app/patient/intake/state/` |

## Implementation Plan

1. **Route & Shell Setup** — Register the `/patient/intake/edit/:appointmentId` route in the patient routing configuration, guarded by `AuthGuard` (Patient role only).
2. **Reactive Form Definition** — Define `IntakeEditFormGroup` using Angular Reactive Forms with form controls for all intake sections: demographics (name, DOB, sex, phone, address), medicalHistory (JSONB array), symptoms (JSONB array), medications (JSONB array). Apply `Validators.required` to mandatory fields.
3. **Data Load on Init** — In `ngOnInit`, dispatch load sequence:
   a. Call `GET /api/intake/{appointmentId}/draft` — if draft exists, hydrate the form with draft values (AC-4 restore after timeout).
   b. If no draft, call `GET /api/intake/{appointmentId}` — hydrate form with the persisted `IntakeRecord` values (AC-1 pre-population).
   c. Store the server-side `ETag` / `rowVersion` in component state for optimistic concurrency header on save.
4. **Autosave on Blur** — Subscribe to `valueChanges` (debounced 800 ms) or individual `(blur)` events; call `POST /api/intake/{appointmentId}/draft` with current form value. Display an unobtrusive "Draft saved" inline indicator.
5. **Save Action** — On "Save" button click:
   a. Include `If-Match: <ETag>` header in `PUT /api/intake/{appointmentId}` request body.
   b. **200 OK**: Show "Intake updated" success toast, navigate back to dashboard.
   c. **422 Unprocessable Entity**: Parse `missingFields[]` from response; apply `setErrors({ required: true })` to the specific form controls; display incomplete section summary banner (AC-3).
   d. **409 Conflict**: Open `IntakeConflictModalComponent` passing both local form values and server payload; patient selects or merges the correct version; on confirm, re-submit with updated values and new `ETag`.
6. **Conflict Modal** — `IntakeConflictModalComponent` renders a two-column diff view (My Version vs. Staff Version). Patient can select field values from either version. On "Confirm Resolution", the form is updated with chosen values and the save is retried.
7. **AI-to-Manual Mode Handoff** — Accept an optional `intakeData` input / route state from the AI intake component; if present, pre-populate form controls with the AI-collected data before displaying edit form. No separate API call required for this path.
8. **State Management** — Use NgRx Signal store (`IntakeStateSignal`) to hold: `loadingState`, `savingState`, `draftSavedAt`, `conflictPayload`, `missingFields[]`.
9. **Accessibility** — Ensure all form controls have associated `<label>` elements, `aria-required`, and `aria-describedby` for error messages. Conflict modal must trap focus and have `role="dialog"` with `aria-modal="true"`.

## Current Project State

```
app/
└── patient/
    ├── dashboard/           ← US_016 delivered
    │   ├── patient-dashboard.component.ts
    │   └── patient-dashboard.service.ts
    └── intake/              ← NEW (this task)
        ├── intake-edit.component.ts
        ├── intake-edit.component.html
        ├── intake-edit.component.scss
        ├── models/
        │   └── intake-edit-form.model.ts
        ├── services/
        │   └── intake.service.ts
        ├── state/
        │   └── intake.state.ts
        └── components/
            └── intake-conflict-modal/
                ├── intake-conflict-modal.component.ts
                ├── intake-conflict-modal.component.html
                └── intake-conflict-modal.component.scss
```

> **Note**: Update this tree after `task_002` (BE) and `task_003` (DB) complete to reflect any path changes.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `app/patient/intake/intake-edit.component.ts` | Standalone Angular component — Edit Intake form page with load/save/draft/conflict logic |
| CREATE | `app/patient/intake/intake-edit.component.html` | Template — reactive form sections (demographics, medical history, symptoms, medications), incomplete-field banner, draft-saved indicator |
| CREATE | `app/patient/intake/intake-edit.component.scss` | Scoped styles — form layout, incomplete field highlight, draft indicator, conflict modal overlay |
| CREATE | `app/patient/intake/models/intake-edit-form.model.ts` | TypeScript interfaces: `IntakeFormValue`, `IntakeDraftResponse`, `IntakeConflictPayload`, `IntakeMissingFieldsError` |
| CREATE | `app/patient/intake/services/intake.service.ts` | Angular service — wraps `GET /api/intake/{appointmentId}`, `PUT /api/intake/{appointmentId}`, `POST` and `GET /api/intake/{appointmentId}/draft` |
| CREATE | `app/patient/intake/state/intake.state.ts` | NgRx Signal store — `loadingState`, `savingState`, `draftSavedAt`, `conflictPayload`, `missingFields` |
| CREATE | `app/patient/intake/components/intake-conflict-modal/intake-conflict-modal.component.ts` | Two-column conflict reconciliation modal component |
| CREATE | `app/patient/intake/components/intake-conflict-modal/intake-conflict-modal.component.html` | Conflict modal template — side-by-side field diff, select/merge controls |
| MODIFY | `app/patient/patient-routing.module.ts` | Add route `/intake/edit/:appointmentId` pointing to `IntakeEditComponent`, guarded by `AuthGuard` with role `Patient` |
| MODIFY | `app/patient/dashboard/patient-dashboard.component.ts` | Ensure "Edit Intake" button navigates to `/patient/intake/edit/:appointmentId` (US_016 integration) |

## External References

- [Angular 18 Reactive Forms — FormGroup, FormControl, Validators](https://angular.dev/guide/forms/reactive-forms)
- [Angular 18 Signals — signal(), computed(), effect()](https://angular.dev/guide/signals)
- [NgRx Signals — SignalStore](https://ngrx.io/guide/signals/signal-store)
- [Angular HttpClient — Headers, error handling](https://angular.dev/guide/http)
- [WCAG 2.2 AA — Dialog (Modal) Pattern](https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/)
- [Angular CDK — FocusTrap for modal accessibility](https://material.angular.io/cdk/a11y/overview#focustrap)

## Build Commands

- Refer to [Angular build commands](.propel/build/angular-build.md)
- `ng build --configuration production` — production build
- `ng test --watch=false --code-coverage` — unit tests

## Implementation Validation Strategy

- [ ] Unit tests pass (all form controls hydrate correctly from API responses)
- [ ] Integration tests pass (mock HTTP interceptors for all 4 API endpoints)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px (once wireframe available)
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment (once wireframe available)
- [ ] Draft autosave fires on field blur and `GET /draft` restores values on reload
- [ ] 409 Conflict response triggers conflict modal with both versions displayed
- [ ] 422 response highlights all `missingFields[]` form controls with error state
- [ ] Route guard blocks access for non-Patient roles
- [ ] Conflict modal traps focus and is announced correctly by screen readers (`aria-modal`, `role="dialog"`)

## Implementation Checklist

- [ ] Register `/patient/intake/edit/:appointmentId` route with `AuthGuard` (Patient role)
- [ ] Define `IntakeEditFormGroup` with all intake field controls and required validators
- [ ] Implement `ngOnInit` load sequence: draft first → fallback to persisted record; store `ETag`
- [ ] Implement field-blur autosave calling `POST /api/intake/{appointmentId}/draft`
- [ ] Implement "Save" button handler with `If-Match` header and response-branch logic (200 / 422 / 409)
- [ ] Display incomplete-field banner on 422 with per-control `setErrors` for missing fields
- [ ] Implement `IntakeConflictModalComponent` two-column diff view and confirm-resolution flow
- [ ] Accept AI-mode hand-off data via route state and pre-populate form controls (mode-switch edge case)
- [ ] **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- [ ] **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
