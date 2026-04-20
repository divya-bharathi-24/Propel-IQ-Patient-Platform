# Task - task_001_fe_admin_user_management

## Requirement Reference

- **User Story:** us_012 — Admin-Managed Staff & Admin Account Creation
- **Story Location:** `.propel/context/tasks/EP-001/us_012/us_012.md`
- **Acceptance Criteria:**
  - AC-1: Authenticated Admin can submit a create-user form (name, email, role) and receive confirmation that the account was created and a credential setup email was sent
  - AC-2: New user opens the credential setup email and is directed to a one-time password setup page where they set a compliant password (8+ chars, mixed case, digit, special char) with per-rule inline validation
  - AC-3: Staff member in walk-in booking flow can open a create-patient sub-form and submit name, contact number, and email to create a basic Patient record
  - AC-4: Non-Admin users attempting to access admin account creation routes are denied (redirected to access-denied page — HTTP 403 is surfaced by the backend; frontend guard prevents navigation)
- **Edge Cases:**
  - Credential setup email bounces: Admin sees delivery failure notification in user management list (flag icon or status indicator)
  - Staff creates Patient with duplicate email: UI surfaces inline error "Email already registered" with option to link to existing account

---

## Design References (Frontend Tasks Only)

| Reference Type       | Value                                                                                                                                                 |
| -------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| **UI Impact**        | Yes                                                                                                                                                   |
| **Figma URL**        | N/A                                                                                                                                                   |
| **Wireframe Status** | PENDING                                                                                                                                               |
| **Wireframe Type**   | N/A                                                                                                                                                   |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-admin-user-create.[html\|png\|jpg]` or provide external URL |
| **Screen Spec**      | N/A (figma_spec.md not yet generated)                                                                                                                 |
| **UXR Requirements** | N/A (figma_spec.md not yet generated)                                                                                                                 |
| **Design Tokens**    | N/A (designsystem.md not yet generated)                                                                                                               |

> **Wireframe Status:** PENDING — implement layout following Angular Material and WCAG 2.2 AA guidelines until wireframes are available. Run `/analyze-ux` once wireframe is provided.

---

## Applicable Technology Stack

| Layer            | Technology       | Version |
| ---------------- | ---------------- | ------- |
| Frontend         | Angular          | 18.x    |
| Frontend State   | NgRx Signals     | 18.x    |
| Frontend Routing | Angular Router   | 18.x    |
| HTTP Client      | Angular HttpClient | 18.x  |
| UI Components    | Angular Material | 18.x    |
| Testing — Unit   | Jest             | Latest  |
| Testing — E2E    | Playwright       | 1.x     |
| AI/ML            | N/A              | N/A     |
| Mobile           | N/A              | N/A     |

> All code and libraries MUST be compatible with versions above.

---

## AI References (AI Tasks Only)

| Reference Type        | Value |
| --------------------- | ----- |
| **AI Impact**         | No    |
| **AIR Requirements**  | N/A   |
| **AI Pattern**        | N/A   |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A   |
| **Model Provider**    | N/A   |

---

## Mobile References (Mobile Tasks Only)

| Reference Type      | Value |
| ------------------- | ----- |
| **Mobile Impact**   | No    |
| **Platform Target** | N/A   |
| **Min OS Version**  | N/A   |
| **Mobile Framework**| N/A   |

---

## Task Overview

Implement two frontend surface areas for US_012:

1. **Admin User Management UI** — An admin-only section under `/admin/users` that lists existing Staff/Admin accounts and provides a "Create User" dialog/form accepting name, email, and role (Staff or Admin). On submit, calls the backend API, shows confirmation, and reflects the new account in the list with credential email status.

2. **Credential Setup Page** — A public-facing, token-gated page at `/auth/setup-credentials` where a newly created Staff/Admin user sets their initial compliant password via a link in the setup email. Covers success, expired-token, and already-used-token states.

3. **Walk-In Patient Create Sub-Form** — Within the Staff walk-in booking flow, provides a collapsible inline form to create a basic Patient record (name, phone, email). Duplicate-email error surfaces an inline warning with a link-to-existing-account option.

---

## Dependent Tasks

- **US_011** (EP-001) — JWT authentication layer and `AuthGuard` must be active before admin route guards can be enforced
- **task_002_be_account_management_api** (EP-001/us_012) — Backend endpoints must be deployed before integration testing
- **task_003_db_user_credential_schema** (EP-001/us_012) — Database schema must exist for backend to respond correctly

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `AdminUserListComponent` | Angular Frontend (`app/features/admin/`) |
| CREATE | `CreateUserDialogComponent` (Angular Material dialog) | Angular Frontend (`app/features/admin/`) |
| CREATE | `CredentialSetupComponent` | Angular Frontend (`app/features/auth/`) |
| CREATE | `WalkInPatientCreateFormComponent` (inline sub-form) | Angular Frontend (`app/features/staff/`) |
| CREATE | `AdminService` | Angular Frontend (`app/features/admin/services/`) |
| MODIFY | `AuthService` | Angular Frontend — add `setupCredentials()` method |
| CREATE | `AdminGuard` (route guard) | Angular Frontend (`app/core/guards/`) |
| MODIFY | `AppRoutingModule` / route config | Add `/admin/users` and `/auth/setup-credentials` routes |

---

## Implementation Plan

1. **`AdminGuard`** (`CanActivateFn`): reads the JWT role claim from `AuthService.currentUser()`; if role is not `Admin`, redirects to `/access-denied` (do NOT call the backend — guard is client-side only for UX; backend enforces 403).

2. **`AdminUserListComponent`** at `/admin/users`:
   - On `ngOnInit`: call `AdminService.listUsers()` → `GET /api/admin/users`; render result in `<mat-table>` with columns: name, email, role, status, lastLoginAt, credentialEmailStatus
   - "Create User" button opens `CreateUserDialogComponent` via `MatDialog.open()`
   - On dialog close with `result`: refresh user list; show `MatSnackBar` "Account created. Setup email sent."

3. **`CreateUserDialogComponent`** (reactive form inside `MatDialogRef`):
   - Fields: `name` (required, max 200), `email` (required, email validator), `role` (`MatSelect`: Staff | Admin)
   - Submit → `AdminService.createUser(dto)` → `POST /api/admin/users`
   - On 201: close dialog with result
   - On 409: set email field error `{ alreadyExists: true }` → display "Email already registered"
   - On 403: close dialog, show snackbar "Insufficient permissions"

4. **`CredentialSetupComponent`** at `/auth/setup-credentials?token={token}`:
   - `ngOnInit`: extract `token` from `ActivatedRoute.queryParams`
   - Render reactive form: `password` (custom complexity validator — same `PasswordComplexityValidator` from task_001 of US_010), `confirmPassword` (must match validator)
   - Submit → `AuthService.setupCredentials({ token, password })` → `POST /api/auth/setup-credentials`
   - On 200: display "Password set. Please log in." + link to `/auth/login`
   - On 410 (token expired): display expiry message + "Contact your Admin for a new invite"
   - On 409 (already used): display "Setup link already used. Please log in."
   - On 400: map per-rule FluentValidation errors to inline `<mat-error>` blocks

5. **`WalkInPatientCreateFormComponent`** (inline collapsible, embedded in walk-in booking flow):
   - Fields: `name` (required), `phone` (optional, E.164 regex), `email` (required, email validator)
   - Submit → `StaffService.createWalkInPatient(dto)` → `POST /api/patients/create`
   - On 201: emit `patientCreated` event with returned `patientId` to parent walk-in component
   - On 409: display inline "Email already registered — [Link to existing account]" (AC edge case); link calls `StaffService.searchPatient(email)` and selects the returned patient
   - On 403: display "Insufficient permissions" inline

6. **Accessibility**: all form fields have `<label>`, `aria-describedby` on error messages, `role="alert"` on dynamically-shown errors, keyboard-navigable dialogs (focus trapped in `MatDialog`).

7. **Security (NFR-014)**: sanitize all API error text before template binding; never interpolate raw error detail strings.

8. **Route configuration**: lazy-load Admin feature module; apply `AdminGuard` on `/admin/**`; `/auth/setup-credentials` must NOT require authentication (public route).

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no app/ scaffold yet — greenfield Angular project)
```

> Update this section with actual `app/` tree after the project scaffold is completed.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `app/features/admin/admin.module.ts` | Lazy-loaded Admin feature module |
| CREATE | `app/features/admin/admin-routing.module.ts` | Admin routes: `/admin/users` |
| CREATE | `app/features/admin/components/user-list/admin-user-list.component.ts` | User list table with create button |
| CREATE | `app/features/admin/components/user-list/admin-user-list.component.html` | Template: mat-table with user columns |
| CREATE | `app/features/admin/components/create-user-dialog/create-user-dialog.component.ts` | MatDialog form: name, email, role |
| CREATE | `app/features/admin/components/create-user-dialog/create-user-dialog.component.html` | Dialog template |
| CREATE | `app/features/admin/services/admin.service.ts` | Service: `listUsers()`, `createUser()` |
| CREATE | `app/features/auth/components/credential-setup/credential-setup.component.ts` | One-time credential setup page |
| CREATE | `app/features/auth/components/credential-setup/credential-setup.component.html` | Password + confirm password form |
| CREATE | `app/features/staff/components/walk-in-patient-create/walk-in-patient-create-form.component.ts` | Inline walk-in patient creation sub-form |
| CREATE | `app/features/staff/components/walk-in-patient-create/walk-in-patient-create-form.component.html` | Template: name, phone, email fields |
| CREATE | `app/core/guards/admin.guard.ts` | Route guard: allow Admin role only |
| MODIFY | `app/features/auth/services/auth.service.ts` | Add `setupCredentials(token, password)` method |
| MODIFY | `app/app-routing.module.ts` | Add lazy-loaded admin + credential-setup routes |

---

## External References

- [Angular 18 Reactive Forms — Custom Validators](https://angular.dev/guide/forms/form-validation#custom-validators)
- [Angular Material Dialog](https://material.angular.io/components/dialog/overview)
- [Angular Material Table (mat-table)](https://material.angular.io/components/table/overview)
- [Angular Router — CanActivate Guards](https://angular.dev/guide/routing/common-router-tasks#preventing-unauthorized-access)
- [Angular 18 Lazy Loading Feature Modules](https://angular.dev/guide/ngmodules/lazy-loading)
- [WCAG 2.2 AA — Dialog Accessibility](https://www.w3.org/WAI/WCAG22/quickref/#keyboard)
- [OWASP A01 — Broken Access Control (client-side guard + backend enforcement)](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)

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

- [ ] Unit tests pass for `CreateUserDialogComponent` (submit, 409 duplicate, 403 states)
- [ ] Unit tests pass for `CredentialSetupComponent` (success, expired, already-used, validation states)
- [ ] Unit tests pass for `WalkInPatientCreateFormComponent` (submit, 409 duplicate, patientCreated event)
- [ ] Unit tests pass for `AdminGuard` (Admin role passes, Staff/Patient roles redirect to `/access-denied`)
- [ ] `AdminService.createUser()` is NOT called when form is invalid
- [ ] `/admin/users` route is inaccessible without Admin JWT role claim
- [ ] `/auth/setup-credentials` route is accessible without authentication
- [ ] Duplicate email on patient create shows inline warning with link-to-existing action
- [ ] All form fields have associated labels and ARIA attributes (accessibility audit passes)
- [ ] No raw API error text interpolated into templates (NFR-014)
- [ ] **[UI Tasks]** Visual comparison against wireframe at 375px, 768px, 1440px (when wireframe becomes AVAILABLE)
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment (when wireframe becomes AVAILABLE)

---

## Implementation Checklist

- [ ] Create `AdminGuard` (`CanActivateFn`): allow Admin role only; redirect non-Admin to `/access-denied`
- [ ] Build `AdminUserListComponent` with `mat-table`, user data columns, and "Create User" button
- [ ] Build `CreateUserDialogComponent` reactive form (name, email, role selector); handle 201, 409, 403 responses
- [ ] Build `CredentialSetupComponent` at `/auth/setup-credentials?token=…`; handle success, expired (410), already-used (409), validation (400) states
- [ ] Reuse `PasswordComplexityValidator` from Auth module (US_010 task_001); do NOT duplicate
- [ ] Build `WalkInPatientCreateFormComponent` with `patientCreated` output event; handle 201, 409 (link offer), 403
- [ ] Add `setupCredentials()` method to `AuthService`
- [ ] Apply `AdminGuard` to `/admin/**` routes in lazy-loaded `AdminModule`
- [ ] Register `/auth/setup-credentials` as a public route (no auth guard) in `AppRoutingModule`
- [ ] **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation (when AVAILABLE)
- [ ] **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete (when AVAILABLE)
