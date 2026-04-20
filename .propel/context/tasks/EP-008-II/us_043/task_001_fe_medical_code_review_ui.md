# Task - task_001_fe_medical_code_review_ui

## Requirement Reference

- **User Story:** us_043 — Medical Code Staff Review & Confirmation Interface
- **Story Location:** `.propel/context/tasks/EP-008-II/us_043/us_043.md`
- **Acceptance Criteria:**
  - AC-1: When the medical coding interface opens for a patient encounter, Staff see a side-by-side view with ICD-10 codes on the left and CPT codes on the right, each card showing the code, description, confidence score, and supporting evidence text from the source document. Low-confidence codes (`lowConfidence = true`) are visually flagged.
  - AC-2: Clicking "Confirm" on a code card marks it locally as Accepted with a visual confirmation state; the decision is included in the bulk confirmation payload on submit.
  - AC-3: Clicking "Reject" marks the code as Rejected; a rejection reason field appears and must be populated before the card transitions to the Rejected state.
  - AC-4: The manual code entry form validates the entered code via `POST /api/medical-codes/validate` on blur; valid codes are added to the review panel as manual entries; invalid codes show an inline validation error.
- **Edge Cases:**
  - Partial submission allowed — a progress indicator ("X of N codes reviewed") is shown; Staff can submit with some codes still in `Pending` state.
  - Multiple Staff sessions — on reload, all previously persisted decisions are restored from the API response; new actions overwrite only the current Staff member's pending decisions.

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value                                                                                                                                     |
| ---------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| **UI Impact**          | Yes                                                                                                                                       |
| **Figma URL**          | N/A                                                                                                                                       |
| **Wireframe Status**   | PENDING                                                                                                                                   |
| **Wireframe Type**     | N/A                                                                                                                                       |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-code-review.[html\|png\|jpg]` or provide external URL                 |
| **Screen Spec**        | N/A (figma_spec.md not yet generated)                                                                                                     |
| **UXR Requirements**   | N/A (figma_spec.md not yet generated)                                                                                                     |
| **Design Tokens**      | N/A (designsystem.md not yet generated)                                                                                                   |

> **Wireframe Status: PENDING** — Implement using component-level layout described in Implementation Plan. Align to wireframe when it becomes AVAILABLE.

---

## Applicable Technology Stack

| Layer             | Technology             | Version |
| ----------------- | ---------------------- | ------- |
| Frontend          | Angular                | 18.x    |
| Frontend State    | NgRx Signals           | 18.x    |
| Frontend UI       | Angular Material       | 18.x    |
| Frontend Routing  | Angular Router         | 18.x    |
| HTTP Client       | Angular HttpClient     | 18.x    |
| Testing — Unit    | Jest / Angular Testing Library | — |
| AI/ML             | N/A                    | N/A     |
| Mobile            | N/A                    | N/A     |

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

Implement the Angular Staff-facing medical code review page that displays AI-suggested ICD-10 and CPT codes in a side-by-side layout. Staff can confirm, reject (with reason), or add manual codes for each encounter. The page consumes the `GET /api/patients/{patientId}/medical-codes` response (built in US_042), manages per-code decision state locally via NgRx Signals, shows a real-time progress indicator for pending codes, and submits the final decision set to `POST /api/medical-codes/confirm`. Manual code entries are validated inline via `POST /api/medical-codes/validate` before being added to the panel. No AI calls are made from the frontend — all LLM orchestration is handled by the backend pipeline.

---

## Dependent Tasks

- `task_001_ai_coding_suggestion_pipeline.md` (EP-008-II/us_042) — `GET /api/patients/{patientId}/medical-codes` MUST be available and returning `MedicalCodeSuggestionDto[]`.
- `task_002_be_code_confirmation_api.md` (EP-008-II/us_043) — `POST /api/medical-codes/validate` and `POST /api/medical-codes/confirm` endpoints MUST be deployed for the FE to wire up.
- `task_003_db_medical_code_schema.md` (EP-008-II/us_043) — `MedicalCodes` table MUST exist before the confirmation API can persist decisions.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `MedicalCodeReviewPageComponent` (new) | Clinical Feature Module | CREATE — Routed page component; hosts side-by-side panel layout and submit action |
| `IcdCodesPanelComponent` (new) | Clinical Feature Module | CREATE — Left panel listing ICD-10 `MedicalCodeCardComponent` instances |
| `CptCodesPanelComponent` (new) | Clinical Feature Module | CREATE — Right panel listing CPT `MedicalCodeCardComponent` instances |
| `MedicalCodeCardComponent` (new) | Clinical Feature Module | CREATE — Single code card: code, description, confidence badge, evidence, Confirm/Reject buttons, rejection reason field |
| `ManualCodeEntryComponent` (new) | Clinical Feature Module | CREATE — Form with code input and codeType selector; triggers validate API on blur; appends valid code to active panel |
| `CodeReviewProgressComponent` (new) | Clinical Feature Module | CREATE — Progress indicator: "X of N codes reviewed"; shown while any code is in `Pending` state |
| `MedicalCodeReviewStore` (new) | Clinical State | CREATE — NgRx Signals store slice: `decisions` map (codeId → {status, rejectionReason}), `pendingCount` computed signal |
| `MedicalCodeService` (new) | Clinical Data Access | CREATE — Angular service: `getSuggestions(patientId)`, `validateCode(code, codeType)`, `confirmCodes(patientId, payload)` |
| `clinical.routes.ts` (existing) | App Routing | MODIFY — Add route: `patients/:patientId/medical-codes` → `MedicalCodeReviewPageComponent` |

---

## Implementation Plan

1. **Define the NgRx Signals store slice** — Create `MedicalCodeReviewStore` with:
   - `suggestions` signal: `MedicalCodeSuggestionDto[]` (loaded from API on init)
   - `decisions` signal: `Map<string, { status: 'Pending' | 'Accepted' | 'Rejected', rejectionReason?: string }>`
   - `pendingCount` computed signal: count of decisions with `status = 'Pending'`
   - `icdCodes` computed signal: `suggestions.filter(s => s.codeType === 'ICD10')`
   - `cptCodes` computed signal: `suggestions.filter(s => s.codeType === 'CPT')`

2. **Implement `MedicalCodeService`** — Angular injectable:
   - `getSuggestions(patientId: string): Observable<MedicalCodeSuggestionsResponse>` → `GET /api/patients/{patientId}/medical-codes`
   - `validateCode(code: string, codeType: 'ICD10' | 'CPT'): Observable<CodeValidationResult>` → `POST /api/medical-codes/validate`
   - `confirmCodes(patientId: string, payload: ConfirmCodesPayload): Observable<void>` → `POST /api/medical-codes/confirm`

3. **Implement `MedicalCodeCardComponent`** — Standalone Angular component:
   - `@Input() suggestion: MedicalCodeSuggestionDto`
   - `@Input() decision: { status, rejectionReason? }`
   - `@Output() confirmed = new EventEmitter<void>()`
   - `@Output() rejected = new EventEmitter<string>()` (rejection reason)
   - Confidence badge: green (≥ 0.80), amber (< 0.80) with "Low Confidence — Review Required" tooltip
   - Supporting evidence text displayed in an expandable `mat-expansion-panel`
   - "Reject" click reveals rejection reason `mat-form-field`; disable the Reject button until reason is non-empty

4. **Implement `IcdCodesPanelComponent` and `CptCodesPanelComponent`** — Each panel iterates over `icdCodes` / `cptCodes` signal and renders `MedicalCodeCardComponent` instances. Panel header shows the code type label and count.

5. **Implement `ManualCodeEntryComponent`** — Angular reactive form with `code` (text) and `codeType` (select: ICD10 | CPT) controls. On blur of the code field, call `validateCode`; display inline `mat-error` if invalid; on success, dispatch the validated code to the store as a new Pending manual entry.

6. **Implement `CodeReviewProgressComponent`** — Reads `pendingCount` signal; renders a `mat-progress-bar` and label "X of N codes reviewed". Shown persistently at the top of the page; does not block submission.

7. **Implement `MedicalCodeReviewPageComponent`** — Container:
   - On `ngOnInit`, call `MedicalCodeService.getSuggestions(patientId)` and populate the store.
   - Handle the empty-data case: if `suggestions.length === 0` and `message` is present, display the message in a `mat-card` with an "Upload Documents" action link.
   - Handle HTTP 503: display a `mat-snack-bar` error "Coding service temporarily unavailable — please retry or enter codes manually."
   - "Submit Review" button: calls `confirmCodes` with accepted[], rejected[], manual[] built from `decisions`; navigates to patient record on success.

8. **Wire route and navigation** — Add `{ path: 'patients/:patientId/medical-codes', component: MedicalCodeReviewPageComponent, canActivate: [StaffRoleGuard] }` to `clinical.routes.ts`. Add a "Review Codes" button in the patient record page (existing) linking to this route.

---

## Current Project State

```
app/
  clinical/
    pages/
      patient-360-view/
        patient-360-view.component.ts      ← existing page pattern to follow
    components/
      clinical-field-card/
        clinical-field-card.component.ts   ← existing card component pattern
    services/
      clinical.service.ts                  ← existing Angular service pattern
    store/
      clinical-360.store.ts                ← existing NgRx Signals store pattern
    clinical.routes.ts                     ← existing routing to extend
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `app/clinical/pages/medical-code-review/medical-code-review.page.ts` | Routed page: loads suggestions, hosts panels, handles submit |
| CREATE | `app/clinical/components/icd-codes-panel/icd-codes-panel.component.ts` | Left panel: ICD-10 code card list |
| CREATE | `app/clinical/components/cpt-codes-panel/cpt-codes-panel.component.ts` | Right panel: CPT code card list |
| CREATE | `app/clinical/components/medical-code-card/medical-code-card.component.ts` | Card: code, description, confidence badge, evidence, Confirm/Reject |
| CREATE | `app/clinical/components/manual-code-entry/manual-code-entry.component.ts` | Manual entry form with inline validation |
| CREATE | `app/clinical/components/code-review-progress/code-review-progress.component.ts` | Progress indicator for pending review count |
| CREATE | `app/clinical/store/medical-code-review.store.ts` | NgRx Signals store: suggestions, decisions, pendingCount, icdCodes, cptCodes |
| CREATE | `app/clinical/services/medical-code.service.ts` | Angular service: getSuggestions, validateCode, confirmCodes |
| MODIFY | `app/clinical/clinical.routes.ts` | Add route: `patients/:patientId/medical-codes` |

---

## External References

- [Angular 18 Signals & NgRx Signals Store](https://ngrx.io/guide/signals/signal-store) — `signalStore`, `withState`, `withComputed`, `withMethods` API
- [Angular 18 Standalone Components](https://angular.dev/guide/components/importing) — `imports` array in `@Component` decorator
- [Angular Material 18 — Card, Progress Bar, Expansion Panel, Snackbar](https://material.angular.io/components/categories) — `MatCardModule`, `MatProgressBarModule`, `MatExpansionModule`, `MatSnackBarModule`
- [Angular Reactive Forms (18)](https://angular.dev/guide/forms/reactive-forms) — `FormBuilder`, `FormGroup`, `FormControl` for manual code entry
- [Angular HttpClient (18)](https://angular.dev/guide/http) — `HttpClient` with typed response bodies
- [FR-052 (spec.md)](../.propel/context/docs/spec.md) — Side-by-side review interface requirement
- [UC-009 Sequence Diagram (models.md)](../.propel/context/docs/models.md) — Full code review flow
- [NFR-012 (design.md)](../.propel/context/docs/design.md) — Frontend LCP < 2.5s, FID < 100ms, CLS < 0.1

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for Angular build and serve commands.

---

## Implementation Validation Strategy

- [ ] Unit tests pass (Jest / Angular Testing Library)
- [ ] Integration tests pass (page component → service mock → API contract)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px (execute when wireframe becomes AVAILABLE)
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment (execute when wireframe becomes AVAILABLE)
- [ ] ICD-10 panel renders on the left and CPT panel on the right with correct card data
- [ ] Low-confidence cards display amber badge and "Low Confidence — Review Required" label
- [ ] Confirm action sets card to Accepted visual state; Reject requires non-empty reason before confirming Rejected state
- [ ] Manual code entry form calls validate API on blur; valid code appends to panel; invalid code shows inline error
- [ ] Progress indicator updates reactively as decisions are made
- [ ] Empty suggestions case: message card displayed without panels
- [ ] HTTP 503 case: snackbar error shown with manual entry option still active
- [ ] Submit button calls `POST /api/medical-codes/confirm` with correct accepted/rejected/manual payloads
- [ ] Route is protected by `StaffRoleGuard`; Patient-role users are redirected

---

## Implementation Checklist

- [ ] Create `MedicalCodeReviewStore` (NgRx Signals): `suggestions`, `decisions`, `pendingCount`, `icdCodes`, `cptCodes`
- [ ] Create `MedicalCodeService`: `getSuggestions`, `validateCode`, `confirmCodes` HTTP methods
- [ ] Create `MedicalCodeCardComponent`: confidence badge, evidence expansion, Confirm/Reject with reason gate
- [ ] Create `IcdCodesPanelComponent` and `CptCodesPanelComponent` iterating over store signals
- [ ] Create `ManualCodeEntryComponent`: reactive form with blur-triggered validation API call
- [ ] Create `CodeReviewProgressComponent`: reads `pendingCount` signal, renders progress bar + label
- [ ] Create `MedicalCodeReviewPageComponent`: compose all sub-components, handle empty/503 states, wire submit
- [ ] Extend `clinical.routes.ts`: add `patients/:patientId/medical-codes` route with `StaffRoleGuard`
