# Task - TASK_001

## Requirement Reference

- **User Story**: US_011 — User Login, JWT Tokens & Session Auto-Timeout
- **Story Location**: `.propel/context/tasks/EP-001/us_011/us_011.md`
- **Acceptance Criteria**:
  - AC-1: Given valid credentials, When submitted, Then receive JWT access token (15-min expiry) + rotating refresh token; redirect authenticated user to dashboard
  - AC-2: Given 15-minute session inactivity, When the idle timer fires, Then user is redirected to the login page with a "session expired" message
  - AC-3: Given an expired access token but active session, When any API request is made, Then the HTTP interceptor silently calls refresh, swaps the token, and retries the original request without user re-login
  - AC-4: Given the user clicks "Logout", When the action executes, Then tokens are cleared client-side and the user is redirected to the login page
- **Edge Cases**:
  - Stolen refresh token: interceptor receives 401 on refresh call → full logout, display "session invalidated for security" message
  - Multi-device: each Angular tab/session manages its own in-memory token independently; no cross-tab session sync required for Phase 1

## Design References (Frontend Tasks Only)

| Reference Type         | Value                                                                                                               |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------- |
| **UI Impact**          | Yes                                                                                                                 |
| **Figma URL**          | N/A                                                                                                                 |
| **Wireframe Status**   | PENDING                                                                                                             |
| **Wireframe Type**     | N/A                                                                                                                 |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-011-login.[html\|png\|jpg]` or provide external URL |
| **Screen Spec**        | N/A (figma_spec.md not yet generated)                                                                               |
| **UXR Requirements**   | N/A (figma_spec.md not yet generated)                                                                               |
| **Design Tokens**      | N/A (designsystem.md not yet generated)                                                                             |

## Applicable Technology Stack

| Layer          | Technology           | Version    |
| -------------- | -------------------- | ---------- |
| Frontend       | Angular              | 18.x       |
| Frontend State | NgRx Signals         | 18.x       |
| Backend        | ASP.NET Core Web API | .net 10    |
| Database       | PostgreSQL           | 16+        |
| Cache          | Upstash Redis        | Serverless |
| AI/ML          | N/A                  | N/A        |
| Vector Store   | N/A                  | N/A        |
| AI Gateway     | N/A                  | N/A        |
| Mobile         | N/A                  | N/A        |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type           | Value |
| ------------------------ | ----- |
| **AI Impact**            | No    |
| **AIR Requirements**     | N/A   |
| **AI Pattern**           | N/A   |
| **Prompt Template Path** | N/A   |
| **Guardrails Config**    | N/A   |
| **Model Provider**       | N/A   |

## Mobile References (Mobile Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **Mobile Impact**    | No    |
| **Platform Target**  | N/A   |
| **Min OS Version**   | N/A   |
| **Mobile Framework** | N/A   |

## Task Overview

Implement the Angular 18 login user interface and client-side session management for US_011. This task covers the `LoginComponent` (reactive form, validation, submission), the `AuthService` (token lifecycle), an `HttpInterceptor` for silent JWT refresh and 401 handling, a `SessionTimerService` for 15-minute inactivity enforcement, an `AuthGuard` for protected routes, and the logout flow. All token storage uses session-scoped memory (never `localStorage`) to meet OWASP A02 (Cryptographic Failures) and HIPAA requirements.

## Dependent Tasks

- **TASK_003** — `refresh_tokens` DB migration must be applied (schema exists in PostgreSQL) before the backend endpoints can be exercised end-to-end from the frontend.
- **TASK_002** — Backend auth endpoints (`POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/logout`) must be deployed/running before full integration testing of this task.

## Impacted Components

| Component                     | Status | Location                    |
| ----------------------------- | ------ | --------------------------- |
| `LoginComponent`              | NEW    | `app/features/auth/login/`  |
| `AuthService`                 | NEW    | `app/core/auth/`            |
| `AuthInterceptor`             | NEW    | `app/core/interceptors/`    |
| `SessionTimerService`         | NEW    | `app/core/auth/`            |
| `AuthGuard`                   | NEW    | `app/core/guards/`          |
| `AppRoutingModule`            | MODIFY | `app/app-routing.module.ts` |
| `AppModule` / `provideRouter` | MODIFY | `app/app.config.ts`         |

## Implementation Plan

1. **Token Storage Strategy**: Store `accessToken` and `refreshToken` in a private in-memory `Signal<AuthState>` inside `AuthService` (NgRx Signals `signal()`). Never persist to `localStorage` or `sessionStorage` to prevent XSS token theft (OWASP A03).

2. **LoginComponent**: Create a reactive form (`FormBuilder`) with `email` (Validators.required, Validators.email) and `password` (Validators.required, minLength 8). On valid submit, call `AuthService.login()`. Display server-side error messages (invalid credentials) and loading state on the submit button.

3. **AuthService.login()**: Call `POST /api/auth/login`, receive `{ accessToken, refreshToken, expiresIn }`, store tokens in-memory via Signals, start `SessionTimerService`, and return a navigation observable to the dashboard.

4. **AuthInterceptor**: Implement `HttpInterceptor`. Attach `Authorization: Bearer <accessToken>` to every outbound request (skip `/api/auth/login` and `/api/auth/refresh`). On receiving HTTP 401, attempt one silent refresh by calling `POST /api/auth/refresh` with the current `refreshToken`. On success, swap the stored access token and retry the original request. On refresh failure (401/403), call `AuthService.logout()` and navigate to `/login?reason=session_expired`.

5. **SessionTimerService**: Use `fromEvent` (RxJS) to observe `mousemove`, `keydown`, `click`, and `touchstart` window events. Debounce resets with `switchMap`. After 15 minutes (900 000 ms) of no activity, call `AuthService.logout()` and navigate to `/login?reason=idle_timeout`.

6. **AuthGuard (CanActivateFn)**: Check `AuthService.isAuthenticated()` (derived Signal). If false, redirect to `/login`. If the access token is within the last 60 seconds of expiry, proactively trigger a background refresh before allowing navigation.

7. **LogoutAction**: `AuthService.logout()` clears the in-memory token Signals, calls `POST /api/auth/logout` (fire-and-forget with error suppression), stops `SessionTimerService`, and navigates to `/login`.

8. **Session Expired Message**: Read the `reason` query param on `LoginComponent` init; display a non-dismissable informational banner ("Your session expired due to inactivity. Please log in again.") when `reason=idle_timeout` or `reason=session_expired`.

## Current Project State

```
app/                          ← Angular 18 application root (to be scaffolded)
└── (no source files yet)
```

> This is a greenfield project. No existing Angular source files. All components are new.

## Expected Changes

| Action | File Path                                      | Description                                                                 |
| ------ | ---------------------------------------------- | --------------------------------------------------------------------------- |
| CREATE | `app/features/auth/login/login.component.ts`   | Reactive login form with validation and submission                          |
| CREATE | `app/features/auth/login/login.component.html` | Login form template (email, password, submit button, error banner)          |
| CREATE | `app/features/auth/login/login.component.scss` | Login page styles                                                           |
| CREATE | `app/core/auth/auth.service.ts`                | Token lifecycle: login, logout, refresh, isAuthenticated signal             |
| CREATE | `app/core/auth/auth-state.model.ts`            | `AuthState` interface: `{ accessToken, refreshToken, userId, role }`        |
| CREATE | `app/core/auth/session-timer.service.ts`       | 15-minute inactivity timer with activity event listeners                    |
| CREATE | `app/core/interceptors/auth.interceptor.ts`    | HTTP interceptor: bearer token injection + silent refresh + 401 handling    |
| CREATE | `app/core/guards/auth.guard.ts`                | `CanActivateFn` protecting authenticated routes                             |
| MODIFY | `app/app.config.ts`                            | Register `AuthInterceptor` via `provideHttpClient(withInterceptors([...]))` |
| MODIFY | `app/app-routing.module.ts`                    | Add `/login` route and apply `authGuard` to all protected routes            |

## External References

- [Angular 18 Reactive Forms](https://angular.dev/guide/forms/reactive-forms) — FormBuilder, Validators, form submission patterns
- [Angular 18 HttpInterceptor (functional)](https://angular.dev/guide/http/interceptors) — `HttpInterceptorFn` functional interceptor API (Angular 15+ style)
- [Angular 18 Signals (NgRx Signals)](https://ngrx.io/guide/signals) — `signal()`, `computed()`, `effect()` for reactive token state
- [OWASP: JWT Security Best Practices](https://cheatsheetseries.owasp.org/cheatsheets/JSON_Web_Token_for_Java_Cheat_Sheet.html) — Token storage, validation, and rotation guidance
- [OWASP: Session Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html) — Inactivity timeout and session expiry patterns
- [RxJS fromEvent + switchMap](https://rxjs.dev/api/index/function/fromEvent) — User activity event stream for idle timer
- [Angular Router query params](https://angular.dev/api/router/ActivatedRoute#queryParamMap) — Reading `reason` param for session-expired banner

## Build Commands

```bash
# Install Angular CLI (if not present)
npm install -g @angular/cli@18

# Scaffold new Angular 18 application (greenfield)
ng new propel-iq-patient-platform --routing --style scss --standalone false

# Generate auth feature module and components
ng generate module features/auth --route auth --module app.module
ng generate component features/auth/login --module features/auth/auth.module
ng generate service core/auth/auth
ng generate service core/auth/session-timer
ng generate interceptor core/interceptors/auth
ng generate guard core/guards/auth --implements CanActivate

# Serve locally
ng serve --port 4200

# Build for production
ng build --configuration production
```

## Implementation Validation Strategy

- [ ] Unit tests pass (to be planned separately via `plan-unit-test` workflow)
- [ ] Integration tests pass: login flow returns 200 + tokens; 401 triggers interceptor refresh
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px (when wireframe is available)
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment (when wireframe is available)
- [ ] Login form validation rejects invalid email format and passwords shorter than 8 characters
- [ ] Inactivity timer correctly fires after 900 000 ms; verified via manual test with short timeout (e.g., 5 s) in dev
- [ ] HTTP interceptor correctly retries original request after successful silent refresh
- [ ] HTTP interceptor correctly navigates to `/login?reason=session_expired` when refresh fails (HTTP 401)
- [ ] `AuthGuard` blocks unauthenticated access to protected routes and redirects to `/login`
- [ ] `localStorage` and `sessionStorage` contain no token data after login (OWASP A02 compliance)

## Implementation Checklist

- [x] Create `AuthState` model interface (`accessToken`, `refreshToken`, `userId`, `role`, `expiresAt`)
- [x] Implement `AuthService` with `login()`, `logout()`, `refresh()`, and `isAuthenticated` computed Signal
- [x] Implement `LoginComponent` reactive form with email/password validation and session-expired banner
- [x] Implement `AuthInterceptor` with bearer token injection, 401 detection, silent refresh, and retry logic
- [x] Implement `SessionTimerService` with 15-minute inactivity timer using RxJS `fromEvent` + `switchMap`
- [x] Implement `AuthGuard` (`CanActivateFn`) protecting authenticated routes
- [x] Wire `AuthInterceptor` into `provideHttpClient` in `app.config.ts`
- [x] Implement logout action: clear Signals, fire-and-forget `POST /api/auth/logout`, navigate to `/login`
