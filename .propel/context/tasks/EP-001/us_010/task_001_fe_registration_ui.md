# Task - task_001_fe_registration_ui

## Requirement Reference

- **User Story:** us_010 — Patient Self-Registration with Email Verification
- **Story Location:** `.propel/context/tasks/EP-001/us_010/us_010.md`
- **Acceptance Criteria:**
  - AC-1: Registration form accepts email, password with inline per-rule complexity feedback, and demographic fields (name, phone, dateOfBirth); submit triggers account creation and verification email within 60 seconds
  - AC-3: Form surfaces "Email already registered" when API returns 409, without revealing whether the account is active or inactive
  - AC-4: Each violated password rule is displayed inline (e.g., "Must include at least one special character") on validation
  - AC-2: Clicking the verification link in the email redirects to the booking interface; account activation confirmation is displayed
- **Edge Cases:**
  - Expired token (24-hour window): display prompt to request a new verification email from the resend page
  - Second click on verification link: display "Link already used" message with a prompt to navigate to login

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value                                                                                                                  |
| ---------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| **UI Impact**          | Yes                                                                                                                    |
| **Figma URL**          | N/A                                                                                                                    |
| **Wireframe Status**   | PENDING                                                                                                                |
| **Wireframe Type**     | N/A                                                                                                                    |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-registration.[html\|png\|jpg]` or provide external URL |
| **Screen Spec**        | N/A (figma_spec.md not yet generated)                                                                                  |
| **UXR Requirements**   | N/A (figma_spec.md not yet generated)                                                                                  |
| **Design Tokens**      | N/A (designsystem.md not yet generated)                                                                                |

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

Implement the patient self-registration UI in Angular 18.x. The feature encompasses two screens: (1) the registration form where an unregistered user provides email, password (with real-time per-rule inline validation), and basic demographic details; and (2) the email verification flow where clicking the link in the email activates the account and redirects to the booking interface. Also covers the email verification pending confirmation screen, the expired-token and already-used-token error states, and the resend-verification link.

---

## Dependent Tasks

- **US_006** — Patient entity must exist in the data layer (foundational dependency; consumed via backend API only)
- **task_002_be_registration_api** (EP-001/us_010) — Backend registration and email verification API must be deployed before integration testing

---

## Impacted Components

| Status | Component / Module                                  | Project                   |
| ------ | --------------------------------------------------- | ------------------------- |
| CREATE | `RegistrationFormComponent`                         | Angular Frontend (`app/`) |
| CREATE | `EmailVerificationPendingComponent`                 | Angular Frontend (`app/`) |
| CREATE | `EmailVerifyCallbackComponent`                      | Angular Frontend (`app/`) |
| CREATE | `AuthService` (registration + verification methods) | Angular Frontend (`app/`) |
| MODIFY | `AppRoutingModule` / route config                   | Angular Frontend (`app/`) |
| CREATE | `auth/register` route                               | Angular Routing           |
| CREATE | `auth/verify` route (callback from email link)      | Angular Routing           |

---

## Implementation Plan

1. **Create Auth feature module** under `app/features/auth/` with lazy-loaded routes (`/auth/register`, `/auth/verify`).
2. **Build `RegistrationFormComponent`** using Angular reactive forms (`FormBuilder`, `FormGroup`):
   - Fields: `email` (Validators.required, Validators.email), `password` (custom complexity validator), `name` (required), `phone` (optional, E.164 format regex), `dateOfBirth` (required, ISO date).
   - Password complexity custom validator emits per-rule errors: `minLength`, `uppercase`, `digit`, `specialChar` — each renders as a separate inline `<mat-error>` beneath the password field.
3. **Submit handler** calls `AuthService.register(payload)` → `POST /api/auth/register`. On 200/201: navigate to `EmailVerificationPendingComponent`. On 409: set email field error `{ alreadyRegistered: true }` → display "Email already registered". On 400: map FluentValidation error body to form field errors.
4. **Build `EmailVerificationPendingComponent`**: static informational screen ("We sent a verification link to {email}. Check your inbox.") with a "Resend email" button → calls `POST /api/auth/resend-verification`. Implement 60-second cooldown on the button.
5. **Build `EmailVerifyCallbackComponent`** mounted at `/auth/verify`:
   - On `ngOnInit`: extract `token` from `ActivatedRoute.queryParams`, call `AuthService.verifyEmail(token)` → `GET /api/auth/verify?token={token}`.
   - On success: display brief success message, redirect to `/booking` after 2 seconds.
   - On 410 (token expired): display expiry message + link to resend verification from login page.
   - On 409 (link already used): display "Link already used" + link to login page.
6. **`AuthService` methods**: `register(dto)`, `verifyEmail(token)`, `resendVerification(email)` — typed request/response models; handle HTTP errors via `catchError`, surface structured error objects.
7. **Accessibility**: all form fields have associated `<label>`, ARIA `aria-describedby` wiring for error messages, `role="alert"` on dynamically-shown error containers, keyboard-navigable form flow.
8. **NFR-014 client-side defence**: sanitize all user-visible error text before binding; never interpolate raw API error details into templates.

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no app/ scaffold yet — greenfield Angular project)
```

> Update this section with actual `app/` tree after project scaffold is completed (dependent on initial project setup task).

---

## Expected Changes

| Action | File Path                                                                                           | Description                                                                                 |
| ------ | --------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| CREATE | `app/features/auth/auth.module.ts`                                                                  | Lazy-loaded Auth feature module with routing                                                |
| CREATE | `app/features/auth/auth-routing.module.ts`                                                          | Auth routes: `/auth/register`, `/auth/verify`                                               |
| CREATE | `app/features/auth/components/registration-form/registration-form.component.ts`                     | Reactive form component for patient self-registration                                       |
| CREATE | `app/features/auth/components/registration-form/registration-form.component.html`                   | Template: email, password (per-rule errors), name, phone, dateOfBirth fields                |
| CREATE | `app/features/auth/components/registration-form/registration-form.component.scss`                   | Component styles                                                                            |
| CREATE | `app/features/auth/components/email-verification-pending/email-verification-pending.component.ts`   | Post-registration "check your email" screen with resend button                              |
| CREATE | `app/features/auth/components/email-verification-pending/email-verification-pending.component.html` | Template for email pending screen                                                           |
| CREATE | `app/features/auth/components/email-verify-callback/email-verify-callback.component.ts`             | Handles `/auth/verify?token=…` redirect; success/error states                               |
| CREATE | `app/features/auth/components/email-verify-callback/email-verify-callback.component.html`           | Template for verification callback states                                                   |
| CREATE | `app/features/auth/services/auth.service.ts`                                                        | Service: `register()`, `verifyEmail()`, `resendVerification()` methods                      |
| CREATE | `app/features/auth/validators/password-complexity.validator.ts`                                     | Custom validator returning per-rule error map                                               |
| CREATE | `app/features/auth/models/registration.models.ts`                                                   | TypeScript interfaces: `RegistrationRequest`, `RegistrationResponse`, `VerifyEmailResponse` |
| MODIFY | `app/app-routing.module.ts`                                                                         | Add lazy-loaded `auth` route pointing to `AuthModule`                                       |

---

## External References

- [Angular 18 Reactive Forms — Official Guide](https://angular.dev/guide/forms/reactive-forms)
- [Angular 18 Custom Validators](https://angular.dev/guide/forms/form-validation#custom-validators)
- [Angular Material Form Fields](https://material.angular.io/components/form-field/overview)
- [NgRx Signals — State Management](https://ngrx.io/guide/signals)
- [OWASP A03 — Injection Prevention (Angular Output Encoding)](https://owasp.org/Top10/A03_2021-Injection/)
- [WCAG 2.2 AA — Form Accessibility](https://www.w3.org/WAI/WCAG22/quickref/?versions=2.2#input-assistance)
- [Angular HttpClient Error Handling](https://angular.dev/guide/http/making-requests#handling-request-failure)

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

- [ ] Unit tests pass for `RegistrationFormComponent` (validation states, submit handler, error mapping)
- [ ] Unit tests pass for `EmailVerifyCallbackComponent` (success, expired, already-used states)
- [ ] Unit tests pass for `AuthService` (register, verifyEmail, resendVerification methods)
- [x] Custom password validator emits correct per-rule error keys for each combination of missing rules
- [x] Form submit is disabled when form is invalid (no API call fired)
- [x] 409 response correctly sets `alreadyRegistered` error on email control without revealing active/inactive status
- [x] Token expired state renders correct message and resend link
- [x] Token already-used state renders "Link already used" and login link
- [x] All form fields have associated labels and ARIA attributes (accessibility audit passes)
- [x] No raw API error details interpolated into templates (XSS/injection check)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px (when wireframe becomes AVAILABLE)
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment (when wireframe becomes AVAILABLE)

---

## Implementation Checklist

- [x] Create `app/features/auth/` feature with lazy-loaded routing
- [x] Build `RegistrationFormComponent` reactive form (email, password, name, phone, dateOfBirth)
- [x] Implement `PasswordComplexityValidator` with per-rule error keys (`minLength`, `uppercase`, `digit`, `specialChar`)
- [x] Wire per-rule `<mat-error>` display for each password constraint beneath the password field
- [x] Implement duplicate-email error display from 409 API response ("Email already registered")
- [x] Build `EmailVerificationPendingComponent` with 60-second resend cooldown
- [x] Build `EmailVerifyCallbackComponent` handling success, expired, and already-used states
- [x] Implement `AuthService` with typed `register()`, `verifyEmail()`, `resendVerification()` methods
- [x] Add ARIA attributes and `role="alert"` for dynamically rendered error messages
- [x] Sanitize all user-visible API error text before template binding (NFR-014)
- [x] Register lazy-loaded auth routes in `app.routes.ts`
- [ ] **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation (when AVAILABLE)
- [ ] **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete (when AVAILABLE)
