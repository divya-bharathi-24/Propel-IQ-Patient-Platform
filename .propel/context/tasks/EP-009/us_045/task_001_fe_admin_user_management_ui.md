# Task - task_001_fe_admin_user_management_ui

## Requirement Reference

- **User Story:** us_045 — Admin CRUD on Staff & Admin User Accounts
- **Story Location:** `.propel/context/tasks/EP-009/us_045/us_045.md`
- **Acceptance Criteria:**
  - AC-1: When the Admin navigates to User Management, the page displays a list of all Staff and Admin accounts showing name, email, role, status (Active/Deactivated), and last login date.
  - AC-2: When a new Staff account is created and saved, the UI reflects the new user in the list with `status = Active`; a success notification informs the Admin that a credential setup email was sent.
  - AC-3: When an account is deactivated (after re-authentication modal per US_046), the user row is updated to show `status = Deactivated` without being removed from the list.
  - AC-4: When a non-Admin user attempts to navigate to the User Management route, the Angular route guard redirects them; no data is fetched.
- **Edge Cases:**
  - Self-deactivation attempt: the Deactivate action is disabled (greyed out) on the row matching the currently authenticated Admin's own account; tooltip displays "Cannot deactivate your own account".
  - Credential email delivery failure: success notification displays a warning banner "Email delivery failed — Resend" with a resend action link on the user detail page.

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value                                                                                                                               |
| ---------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| **UI Impact**          | Yes                                                                                                                                 |
| **Figma URL**          | N/A                                                                                                                                 |
| **Wireframe Status**   | PENDING                                                                                                                             |
| **Wireframe Type**     | N/A                                                                                                                                 |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-admin-user-management.[html\|png\|jpg]` or provide external URL |
| **Screen Spec**        | N/A (figma_spec.md not yet generated)                                                                                               |
| **UXR Requirements**   | N/A (figma_spec.md not yet generated)                                                                                               |
| **Design Tokens**      | N/A (designsystem.md not yet generated)                                                                                             |

> **Wireframe Status: PENDING** — Implement using component-level layout described in the Implementation Plan. Align to wireframe when it becomes AVAILABLE.

---

## Applicable Technology Stack

| Layer            | Technology                     | Version |
| ---------------- | ------------------------------ | ------- |
| Frontend         | Angular                        | 18.x    |
| Frontend State   | NgRx Signals                   | 18.x    |
| Frontend UI      | Angular Material               | 18.x    |
| Frontend Routing | Angular Router                 | 18.x    |
| HTTP Client      | Angular HttpClient             | 18.x    |
| Testing — Unit   | Jest / Angular Testing Library | —       |
| AI/ML            | N/A                            | N/A     |
| Mobile           | N/A                            | N/A     |

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

Implement the Angular Admin Panel "User Management" page that gives Admins full lifecycle control over Staff and Admin accounts. The page renders a sortable, filterable `mat-table` of all managed users with columns for name, email, role, status, and last login date. Create, Edit, and Deactivate actions are provided per row. Creating or editing an account opens a `MatDialog` form; deactivation triggers the re-authentication modal (implemented in US_046) before proceeding. The route is protected by `AdminRoleGuard`. Self-deactivation is disabled at the row level. Credential email delivery failure surfaces as an inline warning on the user detail view with a Resend action.

---

## Dependent Tasks

- `task_002_be_admin_user_crud_api.md` (EP-009/us_045) — All API endpoints (`GET /api/admin/users`, `POST /api/admin/users`, `PATCH /api/admin/users/{id}`, `DELETE /api/admin/users/{id}`) MUST be available before UI can wire up calls.
- US_046 re-authentication modal — The deactivation flow depends on the re-auth modal component being in place before this task can complete the full deactivation path.

---

## Impacted Components

| Component                           | Module               | Action                                                                                                      |
| ----------------------------------- | -------------------- | ----------------------------------------------------------------------------------------------------------- |
| `UserManagementPageComponent` (new) | Admin Feature Module | CREATE — Routed page; hosts user table, toolbar, and action dispatch                                        |
| `UserTableComponent` (new)          | Admin Feature Module | CREATE — `mat-table` with sort, filter, pagination; row-level Deactivate/Edit/Resend actions                |
| `UserFormDialogComponent` (new)     | Admin Feature Module | CREATE — `MatDialog` reactive form for Create and Edit user (name, email, role)                             |
| `UserStatusBadgeComponent` (new)    | Admin Feature Module | CREATE — Status chip: green for Active, grey for Deactivated                                                |
| `AdminUserStore` (new)              | Admin State          | CREATE — NgRx Signals store: `users`, `loading`, `createUser`, `updateUser`, `deactivateUser` methods       |
| `AdminUserService` (new)            | Admin Data Access    | CREATE — Angular service: `getUsers`, `createUser`, `updateUser`, `deactivateUser`, `resendCredentialEmail` |
| `AdminRoleGuard` (new)              | Auth Guards          | CREATE — `CanActivateFn` that checks `role === 'Admin'` in JWT claims; redirects to dashboard if not Admin  |
| `admin.routes.ts` (new)             | App Routing          | CREATE — Admin module routes: `/admin/users` → `UserManagementPageComponent`, protected by `AdminRoleGuard` |

---

## Implementation Plan

1. **Define Angular service `AdminUserService`** — Injectable with:
   - `getUsers(): Observable<UserDto[]>` → `GET /api/admin/users`
   - `createUser(payload: CreateUserRequest): Observable<UserDto>` → `POST /api/admin/users`
   - `updateUser(id: string, payload: UpdateUserRequest): Observable<UserDto>` → `PATCH /api/admin/users/{id}`
   - `deactivateUser(id: string): Observable<void>` → `DELETE /api/admin/users/{id}`
   - `resendCredentialEmail(id: string): Observable<void>` → `POST /api/admin/users/{id}/resend-credentials`

2. **Implement `AdminUserStore`** — NgRx Signals store:
   - `users` signal: `UserDto[]`
   - `loading` signal: `boolean`
   - `loadUsers()` method: calls `getUsers`, sets `users`
   - `createUser(payload)` method: calls service, appends to `users` signal
   - `updateUser(id, payload)` method: calls service, replaces matching entry in `users`
   - `deactivateUser(id)` method: calls service, sets matching entry `status = 'Deactivated'` (optimistic)

3. **Implement `AdminRoleGuard`** — `CanActivateFn`: reads current user's role from `AuthStore` (or JWT service); returns `true` if `role === 'Admin'`, otherwise calls `router.navigate(['/dashboard'])` and returns `false`.

4. **Implement `UserStatusBadgeComponent`** — `@Input() status: 'Active' | 'Deactivated'`. Renders a `mat-chip` with green background for Active and grey for Deactivated.

5. **Implement `UserTableComponent`** — Standalone component:
   - Uses `MatTableModule`, `MatSortModule`, `MatPaginatorModule`, `MatInputModule` for filter
   - Columns: `name`, `email`, `role`, `status` (via `UserStatusBadgeComponent`), `lastLoginAt` (formatted date), `actions`
   - Actions column: Edit button (always enabled), Deactivate button (disabled and tooltip "Cannot deactivate your own account" when `user.id === currentAdminId`), Resend credentials button (shown only when `emailDeliveryFailed = true` on the user record)
   - Emits `editUser(user)`, `deactivateUser(user)`, `resendEmail(user)` events to the page component

6. **Implement `UserFormDialogComponent`** — Reactive form for Create and Edit:
   - Fields: Name (required, max 200), Email (required, email format), Role (`mat-select`: Staff | Admin)
   - On submit: calls `AdminUserStore.createUser` or `updateUser` based on whether an existing user is passed; closes dialog on success
   - Email field is read-only in Edit mode (email change not supported in this story)

7. **Implement `UserManagementPageComponent`** — Container:
   - On `ngOnInit`: calls `AdminUserStore.loadUsers()`
   - "Create User" button: opens `UserFormDialogComponent` in Create mode
   - Handles `editUser` event: opens `UserFormDialogComponent` in Edit mode pre-populated with user data
   - Handles `deactivateUser` event: opens re-authentication modal (US_046); on re-auth success, calls `AdminUserStore.deactivateUser(id)`; shows `MatSnackBar` "Account deactivated"
   - Handles `resendEmail` event: calls `AdminUserService.resendCredentialEmail(id)`; shows success/failure snackbar
   - Shows `MatSnackBar` warning "Email delivery failed — account created but email not sent" when `createUser` API returns `emailSent = false`

8. **Wire routes** — Create `admin.routes.ts` with `{ path: 'admin/users', component: UserManagementPageComponent, canActivate: [AdminRoleGuard] }`; add to root router configuration.

---

## Current Project State

```
app/
  admin/                                  ← folder to create
  auth/
    guards/
      staff-role.guard.ts                 ← existing guard pattern to follow for AdminRoleGuard
  shared/
    services/
      auth.store.ts                       ← existing AuthStore providing current user role
  app.routes.ts                           ← existing root routing to extend
```

---

## Expected Changes

| Action | File Path                                                               | Description                                                                           |
| ------ | ----------------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| CREATE | `app/admin/pages/user-management/user-management.page.ts`               | Routed page: loads user list, orchestrates create/edit/deactivate                     |
| CREATE | `app/admin/components/user-table/user-table.component.ts`               | mat-table with sort, filter, pagination, row actions                                  |
| CREATE | `app/admin/components/user-form-dialog/user-form-dialog.component.ts`   | MatDialog reactive form for Create/Edit user                                          |
| CREATE | `app/admin/components/user-status-badge/user-status-badge.component.ts` | Status chip: Active (green) / Deactivated (grey)                                      |
| CREATE | `app/admin/store/admin-user.store.ts`                                   | NgRx Signals store: users, loading, CRUD methods                                      |
| CREATE | `app/admin/services/admin-user.service.ts`                              | HTTP service: getUsers, createUser, updateUser, deactivateUser, resendCredentialEmail |
| CREATE | `app/auth/guards/admin-role.guard.ts`                                   | CanActivateFn: role === 'Admin' check                                                 |
| CREATE | `app/admin/admin.routes.ts`                                             | Admin module routes with AdminRoleGuard                                               |
| MODIFY | `app/app.routes.ts`                                                     | Add admin routes lazy-loaded from admin.routes.ts                                     |

---

## External References

- [Angular 18 Standalone Components](https://angular.dev/guide/components/importing) — `imports` array, no NgModule required
- [Angular Material 18 — Table, Sort, Paginator](https://material.angular.io/components/table/overview) — `MatTableModule`, `MatSortModule`, `MatPaginatorModule`
- [Angular Material 18 — Dialog](https://material.angular.io/components/dialog/overview) — `MatDialog.open()` with typed component and data
- [NgRx Signals Store](https://ngrx.io/guide/signals/signal-store) — `signalStore`, `withState`, `withMethods`, `withComputed`
- [Angular `CanActivateFn` Route Guards](https://angular.dev/guide/routing/common-router-tasks#preventing-unauthorized-access) — Functional guard pattern
- [Angular Reactive Forms (18)](https://angular.dev/guide/forms/reactive-forms) — `FormBuilder`, `Validators.email`, `Validators.required`
- [FR-060 (spec.md)](../.propel/context/docs/spec.md) — Admin CRUD on Staff/Admin accounts
- [NFR-006 (design.md)](../.propel/context/docs/design.md) — RBAC enforcement at API and UI route level
- [UC-010 Sequence Diagram (models.md)](../.propel/context/docs/models.md) — Full Admin user management flow

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for Angular build and serve commands.

---

## Implementation Validation Strategy

- [ ] Unit tests pass (Jest / Angular Testing Library)
- [ ] Integration tests pass (page → service mock → store updates)
- [ ] **[UI Tasks]** Visual comparison against wireframe at 375px, 768px, 1440px (when wireframe becomes AVAILABLE)
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment (when wireframe becomes AVAILABLE)
- [ ] User list renders all Staff and Admin accounts with all five columns
- [ ] Create User dialog: submits `POST /api/admin/users`; new row appears in table with `Active` badge
- [ ] Create User: snackbar shows credential email sent confirmation; warning shown if `emailSent = false`
- [ ] Edit User dialog: pre-populates name and role; email field is read-only; submits `PATCH`
- [ ] Deactivate: self-deactivate button is disabled with tooltip for current Admin's own row
- [ ] Deactivate: re-auth modal triggered before `DELETE` call; row updated to `Deactivated` on success
- [ ] Resend credentials: button visible on rows with `emailDeliveryFailed = true`; snackbar confirms resend
- [ ] `/admin/users` route blocked for non-Admin roles; redirected to dashboard

---

## Implementation Checklist

- [x] Create `AdminUserService` with five HTTP methods (getUsers, createUser, updateUser, deactivateUser, resendCredentialEmail)
- [x] Create `AdminUserStore` (NgRx Signals): users, loading, loadUsers, createUser, updateUser, deactivateUser
- [x] Create `AdminRoleGuard` (`CanActivateFn`): checks `role === 'Admin'`, redirects on failure
- [x] Create `UserStatusBadgeComponent`: Active (green chip) / Deactivated (grey chip)
- [x] Create `UserTableComponent`: mat-table, sort, paginator, filter, row actions with self-deactivate disable logic
- [x] Create `UserFormDialogComponent`: reactive form (name, email read-only on edit, role select); create/edit mode
- [x] Create `UserManagementPageComponent`: load on init, handle all action events, show snackbars
- [x] Create `admin.routes.ts` and extend `app.routes.ts` with lazy-loaded admin routes
