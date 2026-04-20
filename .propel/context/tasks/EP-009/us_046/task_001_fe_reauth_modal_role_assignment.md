# Task - task_001_fe_reauth_modal_role_assignment

## Requirement Reference

- **User Story:** us_046 — Role Assignment with Re-Authentication Gate
- **Story Location:** `.propel/context/tasks/EP-009/us_046/us_046.md`
- **Acceptance Criteria:**
  - AC-1: When a role change is submitted for any managed user, the UI calls `PATCH /api/admin/users/{id}/role` and reflects the new role in the user row immediately (optimistic update); a snackbar confirms the change will take effect on the user's next session.
  - AC-2: When the Admin selects "Elevate to Admin" for a user, a re-authentication modal opens prompting for the current password before the role change API call is made.
  - AC-3: When the password entered in the re-auth modal is rejected by the API (HTTP 401), the modal shows an inline error "Incorrect password — please try again" and remains open; the role change is not submitted.
  - AC-4: When the role change succeeds after re-auth, the user row shows the updated role badge; a snackbar confirms "Role updated — change takes effect on next login".
- **Edge Cases:**
  - Re-auth modal timeout: if the modal is open for 5 minutes without submission, it closes automatically, the action is cancelled, and no API call is made. A `MatSnackBar` informs the Admin "Action timed out — please try again".
  - User currently logged in: the success snackbar text indicates the change takes effect on the user's next session (existing sessions are not invalidated by a role change — contrast with deactivation in US_045).

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value                                                                                                                                            |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| **UI Impact**          | Yes                                                                                                                                              |
| **Figma URL**          | N/A                                                                                                                                              |
| **Wireframe Status**   | PENDING                                                                                                                                          |
| **Wireframe Type**     | N/A                                                                                                                                              |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-role-assignment.[html\|png\|jpg]` or provide external URL                    |
| **Screen Spec**        | N/A (figma_spec.md not yet generated)                                                                                                            |
| **UXR Requirements**   | N/A (figma_spec.md not yet generated)                                                                                                            |
| **Design Tokens**      | N/A (designsystem.md not yet generated)                                                                                                          |

> **Wireframe Status: PENDING** — Implement using the component-level layout described in the Implementation Plan. Align to wireframe when it becomes AVAILABLE.

---

## Applicable Technology Stack

| Layer             | Technology                    | Version |
| ----------------- | ----------------------------- | ------- |
| Frontend          | Angular                       | 18.x    |
| Frontend State    | NgRx Signals                  | 18.x    |
| Frontend UI       | Angular Material              | 18.x    |
| Frontend Routing  | Angular Router                | 18.x    |
| HTTP Client       | Angular HttpClient            | 18.x    |
| Testing — Unit    | Jest / Angular Testing Library | —      |
| AI/ML             | N/A                           | N/A     |
| Mobile            | N/A                           | N/A     |

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

Implement two pieces of UI for US_046:

1. **`ReauthenticationModalComponent`** — A shared, reusable `MatDialog` component that prompts the Admin for their current password before any destructive action (role elevation to Admin; account deactivation from US_045). The modal enforces a 5-minute countdown timer (client-side `setTimeout`); on timeout it closes itself and emits a `cancelled` result. On successful re-auth (HTTP 200 from `POST /api/admin/reauthenticate`), it emits a short-lived `reAuthToken` string to the caller. On failed re-auth (HTTP 401), it displays an inline error and keeps the modal open.

2. **Role assignment integration** — Extend `UserTableComponent` (US_045) with a "Change Role" action per row. Selecting a new role from a `mat-select` dropdown triggers the re-auth modal gate when the target role is `Admin`. After successful re-auth (or without re-auth for downgrade to Staff), the new role is submitted to `PATCH /api/admin/users/{id}/role` via `AdminUserStore.updateUserRole`.

The modal is designed to be consumed by both US_046 (role elevation) and US_045 (account deactivation) — placement in `app/admin/components/reauth-modal/` makes it accessible across Admin workflows.

---

## Dependent Tasks

- `task_001_fe_admin_user_management_ui.md` (EP-009/us_045) — `UserTableComponent` and `AdminUserStore` must exist before role assignment integration can be added.
- `task_002_be_reauth_role_assignment_api.md` (EP-009/us_046) — `POST /api/admin/reauthenticate` and `PATCH /api/admin/users/{id}/role` must be available.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `ReauthenticationModalComponent` (new) | Admin Feature Module | CREATE — Shared MatDialog: password input, 5-min countdown, re-auth HTTP call, emit token or cancel |
| `AdminUserStore` (existing — US_045) | Admin State | MODIFY — Add `updateUserRole(id, role, reAuthToken?)` method and `roleUpdateLoading` signal |
| `AdminUserService` (existing — US_045) | Admin Data Access | MODIFY — Add `updateUserRole(id: string, role: string, reAuthToken?: string): Observable<UserDto>` → `PATCH /api/admin/users/{id}/role` |
| `AdminUserService` (existing — US_045) | Admin Data Access | MODIFY — Add `reauthenticate(password: string): Observable<ReAuthTokenResponse>` → `POST /api/admin/reauthenticate` |
| `UserTableComponent` (existing — US_045) | Admin Feature Module | MODIFY — Add "Change Role" `mat-select` per row; trigger re-auth modal when new role = Admin; emit `changeRole(user, newRole)` event |
| `UserManagementPageComponent` (existing — US_045) | Admin Feature Module | MODIFY — Handle `changeRole` event: open `ReauthenticationModalComponent` if elevation, call `AdminUserStore.updateUserRole`, show result snackbar |

---

## Implementation Plan

1. **Create `ReauthenticationModalComponent`** — Standalone `MatDialog` component:
   - Template: Password input (`type="password"`, `matInput`), submit button, inline error area, countdown display (e.g., "Action will time out in 4:32").
   - Receives `MatDialogData`: `{ actionLabel: string }` — displayed as "Confirm password to {actionLabel}" in the modal header.
   - On open: starts a `setTimeout(closeAndCancel, 5 * 60 * 1000)`. Clears timer on close.
   - On submit: calls `AdminUserService.reauthenticate(password)`:
     - HTTP 200: receives `{ reAuthToken: string }`; calls `this.dialogRef.close({ status: 'confirmed', reAuthToken })`.
     - HTTP 401: sets inline error signal `"Incorrect password — please try again"`; does not close modal; clears password field.
   - On timeout expiry: calls `this.dialogRef.close({ status: 'timeout' })`; caller shows "Action timed out — please try again" snackbar.
   - Emits result as `MatDialogRef<ReauthenticationModalComponent, ReAuthModalResult>` where `ReAuthModalResult = { status: 'confirmed' | 'cancelled' | 'timeout'; reAuthToken?: string }`.

2. **Extend `AdminUserService`** — Add two methods:
   - `reauthenticate(password: string): Observable<ReAuthTokenResponse>` → `POST /api/admin/reauthenticate { currentPassword: password }`; returns `{ reAuthToken: string }`.
   - `updateUserRole(id: string, role: 'Staff' | 'Admin', reAuthToken?: string): Observable<UserDto>` → `PATCH /api/admin/users/{id}/role { role, reAuthToken }`.

3. **Extend `AdminUserStore`** — Add:
   - `roleUpdateLoading` signal: `boolean`.
   - `updateUserRole(id: string, role: 'Staff' | 'Admin', reAuthToken?: string)` method: calls service, replaces matching entry's role in `users` signal on success (optimistic update on response).

4. **Modify `UserTableComponent`**:
   - Add a "Role" column with a `mat-select` (options: Staff | Admin) instead of a static text cell. The select is pre-set to the current role.
   - On selection change: emits `changeRole(user: UserDto, newRole: 'Staff' | 'Admin')` event to the page.
   - No direct API call from the table component — responsibility delegated to the page.

5. **Modify `UserManagementPageComponent`** — Handle `changeRole(user, newRole)` event:
   - If `newRole === 'Admin'`: open `ReauthenticationModalComponent` with `{ actionLabel: 'elevate this account to Admin' }`.
     - On result `status === 'confirmed'`: call `AdminUserStore.updateUserRole(user.id, newRole, result.reAuthToken)`.
     - On result `status === 'cancelled' | 'timeout'`: show snackbar "Action cancelled" or "Action timed out — please try again"; revert the select in the table (re-load user list or patch signal).
   - If `newRole === 'Staff'` (downgrade): call `AdminUserStore.updateUserRole(user.id, newRole)` directly (no re-auth required — FR-062 only mandates re-auth for Admin elevation and deactivation).
   - On store success: show snackbar "Role updated — change takes effect on next login".
   - On store error: show error snackbar; revert the role select display.

6. **Wire `ReauthenticationModalComponent`** into the existing US_045 deactivation flow:
   - In `UserManagementPageComponent`, replace the placeholder "re-authentication modal (US_046)" comment (from US_045 task) with actual `MatDialog.open(ReauthenticationModalComponent, { data: { actionLabel: 'deactivate this account' } })` call.
   - This completes the deactivation path defined in US_045/task_001.

---

## Current Project State

```
app/
  admin/
    pages/
      user-management/
        user-management.page.ts         ← EXISTS (US_045 task_001) — MODIFY
    components/
      user-table/
        user-table.component.ts         ← EXISTS (US_045 task_001) — MODIFY
      reauth-modal/                     ← folder to create
    store/
      admin-user.store.ts               ← EXISTS (US_045 task_001) — MODIFY
    services/
      admin-user.service.ts             ← EXISTS (US_045 task_001) — MODIFY
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `app/admin/components/reauth-modal/reauth-modal.component.ts` | Shared re-auth MatDialog: password input, 5-min countdown timer, re-auth API call, typed result emission |
| MODIFY | `app/admin/services/admin-user.service.ts` | Add `reauthenticate()` and `updateUserRole()` HTTP methods |
| MODIFY | `app/admin/store/admin-user.store.ts` | Add `updateUserRole()` method and `roleUpdateLoading` signal |
| MODIFY | `app/admin/components/user-table/user-table.component.ts` | Replace static role text with editable `mat-select`; emit `changeRole` event |
| MODIFY | `app/admin/pages/user-management/user-management.page.ts` | Handle `changeRole` event with re-auth gate; wire `ReauthenticationModalComponent` for deactivation (completing US_045 placeholder) |

---

## External References

- [Angular Material 18 — Dialog with typed data and result](https://material.angular.io/components/dialog/overview) — `MatDialog.open<C, D, R>()` typed signature
- [Angular Material 18 — Select](https://material.angular.io/components/select/overview) — `mat-select` with `(selectionChange)` output
- [NgRx Signals Store — withMethods](https://ngrx.io/guide/signals/signal-store#defining-store-methods) — Extending store with additional methods after initial creation
- [FR-061 (spec.md)](../.propel/context/docs/spec.md) — Admin role and permission assignment
- [FR-062 (spec.md)](../.propel/context/docs/spec.md) — Re-authentication required before Admin elevation or deactivation
- [UC-010 Sequence Diagram (models.md)](../.propel/context/docs/models.md) — `POST /api/admin/reauthenticate` flow; Admin role change via PATCH

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for Angular build and serve commands.

---

## Implementation Validation Strategy

- [ ] Unit tests pass (Jest / Angular Testing Library)
- [ ] `ReauthenticationModalComponent` opens with correct `actionLabel` header text
- [ ] Correct password → modal closes; `status === 'confirmed'`; `reAuthToken` returned
- [ ] Incorrect password → modal stays open; inline error visible; password field cleared
- [ ] Modal auto-closes after 5 minutes; snackbar "Action timed out — please try again" shown
- [ ] Role change to Staff (downgrade) submits without opening re-auth modal
- [ ] Role change to Admin triggers re-auth modal before PATCH call
- [ ] Successful Admin elevation → role badge updated in table; snackbar "Role updated — change takes effect on next login"
- [ ] Re-auth modal cancellation reverts the role select to previous value
- [ ] Deactivation flow in US_045's `UserManagementPageComponent` uses `ReauthenticationModalComponent` (no lingering placeholder)

---

## Implementation Checklist

- [ ] Create `ReauthenticationModalComponent`: password input, 5-min timeout (`setTimeout`), re-auth API call, typed `MatDialogRef` close with `ReAuthModalResult`
- [ ] Define `ReAuthModalResult` type: `{ status: 'confirmed' | 'cancelled' | 'timeout'; reAuthToken?: string }`
- [ ] Extend `AdminUserService`: add `reauthenticate(password)` and `updateUserRole(id, role, reAuthToken?)` methods
- [ ] Extend `AdminUserStore`: add `updateUserRole()` method and `roleUpdateLoading` signal
- [ ] Modify `UserTableComponent`: role column → `mat-select`; emit `changeRole(user, newRole)` event
- [ ] Modify `UserManagementPageComponent`: handle `changeRole` with re-auth gate for Admin elevation; wire modal to existing deactivation flow
- [ ] Validate AC-3: HTTP 401 from re-auth endpoint → inline modal error, no PATCH submitted
- [ ] Validate edge case: downgrade (Admin → Staff) requires no re-auth
