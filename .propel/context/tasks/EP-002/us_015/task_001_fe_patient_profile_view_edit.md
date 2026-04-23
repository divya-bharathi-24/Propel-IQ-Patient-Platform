# Task - task_001_fe_patient_profile_view_edit

## Requirement Reference

- **User Story:** us_015 — Patient Profile View & Structured Demographic Edit
- **Story Location:** `.propel/context/tasks/EP-002/us_015/us_015.md`
- **Acceptance Criteria:**
  - AC-1: Authenticated Patient navigates to `/profile` and sees all demographic fields: legal name, date of birth, biological sex, email, phone, address, insurance details (insurer name, member ID, group number), and emergency contact
  - AC-2: Patient edits a non-locked field (phone, address, emergency contact, or communication preference), saves, sees a success confirmation, and the audit event is written (handled by backend)
  - AC-3: Locked fields (legal name, date of birth) render as read-only with an inline note: "Contact staff to update this field"
  - AC-4: Invalid phone number format triggers an inline error specifying the expected format without blocking edits to other fields
- **Edge Cases:**
  - Session expires mid-edit: unsaved form state is preserved in `sessionStorage` under `patient-profile-draft`; after re-authentication, the edit form is pre-populated from storage and the draft is cleared on successful save
  - Two tabs save conflicting edits simultaneously: backend returns HTTP 409 (optimistic concurrency conflict); UI shows a stale-data warning "Your profile was updated in another tab — please review and resubmit"

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value                                                                                                                         |
| ---------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| **UI Impact**          | Yes                                                                                                                           |
| **Figma URL**          | N/A                                                                                                                           |
| **Wireframe Status**   | PENDING                                                                                                                       |
| **Wireframe Type**     | N/A                                                                                                                           |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-patient-profile.[html\|png\|jpg]` or provide external URL |
| **Screen Spec**        | N/A (figma_spec.md not yet generated)                                                                                         |
| **UXR Requirements**   | N/A (figma_spec.md not yet generated)                                                                                         |
| **Design Tokens**      | N/A (designsystem.md not yet generated)                                                                                       |

> **Wireframe Status:** PENDING — implement layout following Angular Material and WCAG 2.2 AA guidelines until wireframes are available. Run `/analyze-ux` once wireframe is provided.

---

## Applicable Technology Stack

| Layer            | Technology         | Version |
| ---------------- | ------------------ | ------- |
| Frontend         | Angular            | 18.x    |
| Frontend State   | NgRx Signals       | 18.x    |
| Frontend Routing | Angular Router     | 18.x    |
| HTTP Client      | Angular HttpClient | 18.x    |
| UI Components    | Angular Material   | 18.x    |
| Testing — Unit   | Jest               | Latest  |
| Testing — E2E    | Playwright         | 1.x     |
| AI/ML            | N/A                | N/A     |
| Mobile           | N/A                | N/A     |

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

Implement the Patient Profile feature under `/profile` in the Angular 18.x frontend. The feature has two modes:

- **View mode**: displays all demographic and insurance fields read-only. Locked fields (legal name, date of birth, biological sex) are always read-only with a staff-assistance note. Non-locked fields have an "Edit" affordance.
- **Edit mode**: an inline reactive form or slide-out panel exposing only the editable fields (phone, address, emergency contact, communication preferences). Per-field inline validation (phone E.164 format). On save, calls `PATCH /api/patients/me`; on success shows a `MatSnackBar` confirmation; on 409 conflict shows a stale-data warning.

Session-expiry resilience: unsubmitted form state is auto-saved to `sessionStorage` on every `valueChanges` event and restored on component init if a draft exists. Draft is cleared on successful PATCH.

---

## Dependent Tasks

- **task_002_be_patient_profile_api** (EP-002/us_015) — `GET /api/patients/me` and `PATCH /api/patients/me` must be deployed before integration testing
- **US_011** (EP-001) — JWT `AuthGuard` must be active so `/profile` requires authentication

---

## Impacted Components

| Status | Component / Module                | Project                                                                            |
| ------ | --------------------------------- | ---------------------------------------------------------------------------------- |
| CREATE | `PatientProfileComponent`         | Angular Frontend (`app/features/patient/`)                                         |
| CREATE | `PatientProfileEditFormComponent` | Angular Frontend (`app/features/patient/`)                                         |
| CREATE | `PatientService`                  | Angular Frontend (`app/features/patient/services/`)                                |
| CREATE | `PatientProfileDraftService`      | Angular Frontend (`app/features/patient/services/`) — manages sessionStorage draft |
| MODIFY | `AppRoutingModule`                | Add `/profile` route guarded by `AuthGuard` (Patient role)                         |

---

## Implementation Plan

1. **`PatientService`** — two methods:
   - `getProfile()` → `GET /api/patients/me` → returns `PatientProfileDto`
   - `updateProfile(dto: UpdatePatientProfileDto, eTag: string)` → `PATCH /api/patients/me` with `If-Match: {eTag}` header → 200 on success, 409 on concurrency conflict

2. **`PatientProfileDraftService`** — thin wrapper around `sessionStorage`:
   - `saveDraft(formValue: UpdatePatientProfileDto)` → `sessionStorage.setItem('patient-profile-draft', JSON.stringify(formValue))`
   - `loadDraft()` → parse from storage; return `null` if absent or invalid JSON
   - `clearDraft()` → `sessionStorage.removeItem('patient-profile-draft')`

3. **`PatientProfileComponent`** (view mode):
   - `ngOnInit`: call `PatientService.getProfile()` → populate read-only display; store returned `ETag` header value in component state
   - Locked fields (`name`, `dateOfBirth`, `biologicalSex`): rendered as `<mat-form-field>` with `readonly` + `disabled` + a `<mat-hint>` "Contact staff to update this field"
   - Non-locked field group: display values + "Edit profile" `<button mat-stroked-button>` that switches to edit mode (shows `PatientProfileEditFormComponent`)
   - On HTTP error from `getProfile()`: show `MatSnackBar` with generic error message; log error via `console.error` (no PHI in logs)

4. **`PatientProfileEditFormComponent`** (edit mode, inline or sheet):
   - Receives `@Input() initialValue: UpdatePatientProfileDto` and `@Input() eTag: string`
   - Emits `@Output() saved = new EventEmitter<PatientProfileDto>()`; `@Output() cancelled = new EventEmitter<void>()`
   - Reactive form fields (all non-locked):
     - `phone`: optional, custom validator `E164PhoneValidator` — pattern `^\+?[1-9]\d{1,14}$`; inline `<mat-error>` "Phone must be in international format (e.g. +1-202-555-0123)"
     - `address`: grouped `FormGroup` (street, city, state, postalCode, country) — each max 200 chars
     - `emergencyContact`: grouped `FormGroup` (name, phone, relationship) — phone uses same `E164PhoneValidator`
     - `communicationPreferences`: `FormGroup` (emailOptIn: boolean, smsOptIn: boolean, preferredLanguage: string)
   - On `valueChanges` (debounced 500ms): call `PatientProfileDraftService.saveDraft(formValue)`
   - `ngOnInit`: call `PatientProfileDraftService.loadDraft()` → if draft exists, pre-populate form and show banner "You have unsaved changes from a previous session"
   - Submit → `PatientService.updateProfile(formValue, eTag)`:
     - On 200: `PatientProfileDraftService.clearDraft()`; emit `saved` event; show `MatSnackBar` "Profile updated successfully"
     - On 409: show inline warning "Your profile was updated in another tab — please review and resubmit"; refresh profile data via `PatientService.getProfile()` to load latest
     - On 400: map FluentValidation error body to field-level errors
     - On 403: redirect to `/access-denied`

5. **Locked field rendering**: use Angular `[attr.aria-readonly]="true"` and `formControl.disable()` for locked fields. The "Contact staff" note uses `<mat-hint>` with `aria-describedby` wiring for screen-reader accessibility.

6. **Route guard**: `/profile` requires `AuthGuard` (Patient role); non-patient roles redirect to their respective dashboards.

7. **Security (NFR-014)**: sanitize all API error text before template binding; never interpolate raw server error messages.

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no app/ scaffold yet — greenfield Angular project)
```

> Update with actual `app/` tree after scaffold is complete.

---

## Expected Changes

| Action | File Path                                                                               | Description                                                             |
| ------ | --------------------------------------------------------------------------------------- | ----------------------------------------------------------------------- |
| CREATE | `app/features/patient/patient.module.ts`                                                | Lazy-loaded Patient feature module                                      |
| CREATE | `app/features/patient/patient-routing.module.ts`                                        | Patient routes: `/profile`                                              |
| CREATE | `app/features/patient/components/profile/patient-profile.component.ts`                  | View-mode profile page: reads and displays all demographic fields       |
| CREATE | `app/features/patient/components/profile/patient-profile.component.html`                | Template: locked + editable field display, edit button                  |
| CREATE | `app/features/patient/components/profile-edit/patient-profile-edit-form.component.ts`   | Edit mode form component with draft persistence                         |
| CREATE | `app/features/patient/components/profile-edit/patient-profile-edit-form.component.html` | Template: phone, address, emergency contact, communication prefs fields |
| CREATE | `app/features/patient/services/patient.service.ts`                                      | `getProfile()`, `updateProfile()` with ETag header                      |
| CREATE | `app/features/patient/services/patient-profile-draft.service.ts`                        | sessionStorage draft save/load/clear                                    |
| CREATE | `app/features/patient/validators/e164-phone.validator.ts`                               | Custom validator for E.164 phone format                                 |
| CREATE | `app/features/patient/models/patient-profile.models.ts`                                 | Interfaces: `PatientProfileDto`, `UpdatePatientProfileDto`              |
| MODIFY | `app/app-routing.module.ts`                                                             | Add lazy-loaded `/profile` route with `AuthGuard`                       |

---

## External References

- [Angular 18 Reactive Forms — FormGroup Nesting](https://angular.dev/guide/forms/reactive-forms#grouping-form-controls)
- [Angular 18 — valueChanges + debounceTime](https://angular.dev/guide/forms/reactive-forms#listening-to-changes)
- [Angular Material Form Field (mat-form-field, mat-hint, mat-error)](https://material.angular.io/components/form-field/overview)
- [Angular Material Snackbar](https://material.angular.io/components/snack-bar/overview)
- [sessionStorage — MDN](https://developer.mozilla.org/en-US/docs/Web/API/Window/sessionStorage)
- [Angular HttpClient — Custom Request Headers (If-Match ETag)](https://angular.dev/guide/http/making-requests#adding-headers)
- [WCAG 2.2 AA — Identifying Input Purpose (autocomplete)](https://www.w3.org/WAI/WCAG22/quickref/#identify-input-purpose)
- [OWASP A01 — Broken Access Control (patient sees own data only)](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)

---

## Build Commands

```bash
# Install dependencies
npm install

# Serve development build
ng serve

# Build production
ng build --configuration production

# Run unit tests
ng test

# Run E2E tests (Playwright)
npx playwright test
```

---

## Implementation Validation Strategy

- [ ] Unit tests pass for `PatientProfileComponent` (data display, locked fields disabled, edit button shows edit form)
- [ ] Unit tests pass for `PatientProfileEditFormComponent` (submit success, 409 conflict warning, 400 field errors, draft save/restore)
- [ ] Unit tests pass for `PatientProfileDraftService` (save/load/clear round-trip, invalid JSON returns null)
- [ ] Unit tests pass for `E164PhoneValidator` (valid formats pass, invalid formats fail with correct error key)
- [ ] `name`, `dateOfBirth`, `biologicalSex` form controls are disabled and show "Contact staff" hint
- [ ] Phone field error shows expected format hint without blocking save of other valid fields
- [ ] On 409 response: stale-data banner shown and profile data refreshed
- [ ] After successful save: `sessionStorage` draft is cleared
- [ ] After session-expiry re-auth: draft pre-populates form and "unsaved changes" banner is displayed
- [ ] `/profile` route inaccessible without Patient JWT (redirects to login)
- [ ] No raw API error messages interpolated into templates (NFR-014)
- [ ] **[UI Tasks]** Visual comparison against wireframe at 375px, 768px, 1440px (when wireframe becomes AVAILABLE)
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment (when wireframe becomes AVAILABLE)

---

## Implementation Checklist

- [x] Create `PatientService` with `getProfile()` (stores ETag) and `updateProfile()` (sends `If-Match` header)
- [x] Create `PatientProfileDraftService` with `saveDraft()`, `loadDraft()`, `clearDraft()` backed by `sessionStorage`
- [x] Build `PatientProfileComponent`: display all fields, locked fields disabled with "Contact staff" hint, "Edit profile" button
- [x] Build `PatientProfileEditFormComponent` with nested `FormGroup`s for address, emergency contact, communication prefs
- [x] Implement `E164PhoneValidator` and apply to both `phone` and `emergencyContact.phone` fields
- [x] Wire `valueChanges` (debounced 500ms) to `PatientProfileDraftService.saveDraft()` in edit form
- [x] On component init: load draft from `PatientProfileDraftService`; pre-populate form and show "unsaved changes" banner if draft found
- [x] Handle 409 response: show stale-data warning; refresh profile via `getProfile()`; clear draft
- [x] Apply `AuthGuard` to `/profile` route in lazy-loaded `PatientModule`
- [ ] **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation (when AVAILABLE)
- [ ] **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete (when AVAILABLE)
