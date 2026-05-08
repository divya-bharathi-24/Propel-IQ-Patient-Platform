# Bug Fix Task - login_error_alert

## Bug Report Reference

- **Bug ID**: bug_login_error_alert
- **Source**: Playwright test run — `test-automation/tests/login.spec.ts`
- **Branch**: `features/propeliq_QA` (commit `519416d`)
- **Mode**: New file creation

---

## Bug Summary

### Issue Classification

- **Priority**: High
- **Severity**: Core login feature tests produce false negatives in standalone/CI environments
- **Affected Version**: commit `519416d` (Booking Automation)
- **Environment**: Windows, Chrome (Playwright standalone project), backend at `https://localhost:5001` not running

### Steps to Reproduce

1. Run `npx playwright test tests/login.spec.ts --project=standalone --headed`
2. Observe tests 1, 2, 3 fail (only test 4 `renders login form correctly` passes)

**Expected**: All 4 login tests pass when the Angular app is running at `localhost:4200`
**Actual**: 3 tests fail — error alert not visible, strict mode locator violation, and dashboard redirect timeout

**Error Output**:

```text
1) @login shows error on invalid credentials
   Error: expect(locator).toBeVisible() failed
   Locator: locator('.server-error[role="alert"]')
   Expected: visible
   Timeout: 15000ms
   Error: element(s) not found

2) @login shows validation error on empty submit
   Error: strict mode violation: getByText(/required/i) resolved to 3 elements

3) @login successful login redirects to dashboard
   Test timeout of 30000ms exceeded.
   Expected pattern: /dashboard/
   Received string:  "http://localhost:4200/auth/login"
```

---

## Root Cause Analysis

- **File**: `test-automation/tests/login.spec.ts` (lines 25–42)
- **Component**: Playwright standalone test suite — `@login Login Page`
- **Function**: Tests `@login shows error on invalid credentials` and `@login successful login redirects to dashboard`
- **Cause**:

### (1) Immediate Trigger

`locator('.server-error[role="alert"]')` times out because the Angular component's `serverError` signal is never set — meaning the `error` callback on `authService.login().subscribe()` never fires within the 15s window.

### (2) Underlying Cause

`login.spec.ts` makes **real HTTP calls** to `POST /api/auth/login`, which proxies to the .NET backend at `https://localhost:5001`. In standalone/CI mode the backend is not running. When a Node.js proxy cannot connect to an HTTPS endpoint, it can exhibit a slow TCP/TLS timeout (up to 30 s) rather than an immediate ECONNREFUSED, so the observable never emits an error within the test timeout.

Compare: `registration_login.spec.ts` (in the same `standalone` project) correctly uses `page.route()` to mock all API calls — making those tests backend-independent. `login.spec.ts` has no such mocks.

### (3) Why Wasn't It Caught Earlier

The tests were written and validated against a live backend environment. The `standalone` project was configured to exclude auth-setup dependencies, but the tests themselves were not made self-contained. No API mocks were added before the commit.

### Secondary Cause — Strict Mode Violation (Test 2)

`getByText(/required/i)` matches **3 elements** in the DOM simultaneously:
1. `<p class="sr-only">All fields are required.</p>`
2. `<mat-error>Email address is required.</mat-error>`
3. `<mat-error>Password is required.</mat-error>`

Playwright strict mode throws because the locator is ambiguous.

---

## Impact Assessment

- **Affected Features**: Login page E2E test coverage
- **User Impact**: False negative test results — CI pipeline reports login tests as broken even when application logic is correct
- **Data Integrity Risk**: No
- **Security Implications**: No — mocking is only in test scope, never in production

---

## Fix Overview

Three targeted fixes:

1. **Add `page.route()` mock for `POST /api/auth/login`** returning HTTP 401 in the invalid-credentials test, so the component's error handler fires and `serverError` is set — independent of backend availability.
2. **Add `page.route()` mock for `POST /api/auth/login`** returning a valid `TokenResponse` in the successful-login test, so the Angular app stores tokens and navigates to `/dashboard`.
3. **Use `.first()` on `getByText(/required/i)`** to resolve the strict-mode violation in the empty-submit test (already applied in `login.spec.ts`).

> The `errorAlert` locator change from `getByRole('alert')` to `.server-error[role="alert"]` (already applied in `login.page.ts`) is correct and should be retained.

---

## Fix Dependencies

- Angular app (`localhost:4200`) must be running — no backend dependency after fix
- `TokenResponse` shape: `{ accessToken, refreshToken, expiresIn, userId, role, deviceId }`

---

## Impacted Components

### Test Layer

| File | Change |
|------|--------|
| `test-automation/tests/login.spec.ts` | Add `page.route()` mocks for login endpoint in 2 tests; `.first()` already applied |
| `test-automation/pages/login.page.ts` | `errorAlert` locator already fixed to `.server-error[role="alert"]` |

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | `test-automation/tests/login.spec.ts` | Add `page.route('**/api/auth/login**', ...)` in `shows error on invalid credentials` (return 401) and `successful login redirects to dashboard` (return TokenResponse) |
| RETAIN | `test-automation/pages/login.page.ts` | Keep `errorAlert` = `.server-error[role="alert"]` (already fixed) |
| RETAIN | `test-automation/tests/login.spec.ts` | Keep `.first()` on `getByText(/required/i)` (already fixed) |

---

## Implementation Plan

### Step 1 — Fix `@login shows error on invalid credentials`

In `login.spec.ts`, before calling `login.login(...)`, add:

```typescript
await page.route('**/api/auth/login**', route =>
  route.fulfill({ status: 401, json: { message: 'Invalid email or password.' } }),
);
```

### Step 2 — Fix `@login successful login redirects to dashboard`

In `login.spec.ts`, before calling `login.login(...)`, add a route that returns a valid `TokenResponse`:

```typescript
await page.route('**/api/auth/login**', route =>
  route.fulfill({
    status: 200,
    json: {
      accessToken: 'mock-access-token',
      refreshToken: 'mock-refresh-token',
      expiresIn: 900,
      userId: 'test-patient-001',
      role: 'Patient',
      deviceId: 'test-device-001',
    },
  }),
);
```

After mocking, the Angular app will:
- Call `_storeTokens(res)` — sets the in-memory `AuthState`
- Navigate to `/dashboard` (role = `'Patient'` triggers the default route)

### Step 3 — (Already done) Strict mode fix

`getByText(/required/i).first()` — already applied in `login.spec.ts` line 35.

### Step 4 — (Already done) Error alert locator

`locator('.server-error[role="alert"]')` in `login.page.ts` — already applied.

---

## Regression Prevention Strategy

- [ ] After fix, run `npx playwright test tests/login.spec.ts --project=standalone` with NO backend running — all 4 tests must pass
- [ ] Run `npx playwright test tests/login.spec.ts --project=standalone` with backend running — all 4 tests must still pass (mocks take precedence)
- [ ] Verify `registration_login.spec.ts` still passes (no regression from locator changes)

---

## Rollback Procedure

1. Revert `page.route()` additions in `login.spec.ts` — tests revert to requiring a live backend
2. No data or infrastructure changes required

---

## External References

- Playwright `page.route()` API: https://playwright.dev/docs/api/class-page#page-route
- Angular Material `mat-error` accessible role: https://material.angular.io/components/form-field/overview
- Login component HTML: `app/src/app/features/auth/components/login/login.component.html` (line 19–26)
- `TokenResponse` model: `app/src/app/core/auth/auth-state.model.ts` (line 13–20)
- `AuthService._storeTokens`: `app/src/app/features/auth/services/auth.service.ts` (line 138–160)

---

## Build Commands

```powershell
# Run the specific spec standalone (no backend needed after fix)
cd test-automation
npx playwright test tests/login.spec.ts --project=standalone --headed

# Run all standalone tests
npx playwright test --project=standalone
```

---

## Implementation Validation Strategy

- [ ] `@login shows error on invalid credentials` — PASS (`.server-error[role="alert"]` visible within 15s)
- [ ] `@login shows validation error on empty submit` — PASS (no strict-mode violation)
- [ ] `@login successful login redirects to dashboard` — PASS (URL matches `/dashboard`)
- [ ] `@login renders login form correctly` — PASS (unchanged)
- [ ] All 4 tests pass with exit code 0

## Implementation Checklist

- [ ] Add `page.route('**/api/auth/login**', ...)` returning 401 in invalid-credentials test
- [ ] Add `page.route('**/api/auth/login**', ...)` returning TokenResponse in successful-login test
- [ ] Confirm `.first()` applied to `getByText(/required/i)` in empty-submit test
- [ ] Confirm `errorAlert` locator = `.server-error[role="alert"]` in `login.page.ts`
- [ ] Run `npx playwright test tests/login.spec.ts --project=standalone` — 4/4 pass
- [ ] Commit fix with message: `fix(e2e): mock login API in standalone tests to remove backend dependency`
