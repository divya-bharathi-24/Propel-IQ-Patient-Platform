# Task - task_001_fe_audit_log_ui

## Requirement Reference

- **User Story:** us_047 — Read-Only Immutable Audit Log Interface
- **Story Location:** `.propel/context/tasks/EP-009/us_047/us_047.md`
- **Acceptance Criteria:**
  - AC-1: When the Admin navigates to the Audit Log page, a paginated, time-ordered (descending) list of audit events is displayed with columns: user ID, role, entity type, entity ID, action type, IP address, and UTC timestamp.
  - AC-2: When the Admin applies filters (date range, user ID, action type, or entity type), the table updates to show only matching events without a full page reload; active filters are visually indicated.
  - AC-3: When the Admin expands an audit event for a clinical data modification, a structured before/after diff view is rendered from the `details` (JSONB) field, showing changed field names, old values, and new values side-by-side.
  - AC-4: When a non-Admin user navigates to `/admin/audit-log`, the Angular route guard redirects them; no audit data is fetched.
- **Edge Cases:**
  - Millions of records: cursor-based pagination is used; the page size is fixed at 50 records; a total-count badge is displayed.
  - No export: the UI contains no download, export, or copy-to-clipboard button for audit data (FR-059 hard requirement); if a developer accidentally adds one, the code review checklist must catch it.

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value                                                                                                                   |
| ---------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| **UI Impact**          | Yes                                                                                                                     |
| **Figma URL**          | N/A                                                                                                                     |
| **Wireframe Status**   | PENDING                                                                                                                 |
| **Wireframe Type**     | N/A                                                                                                                     |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-audit-log.[html\|png\|jpg]` or provide external URL |
| **Screen Spec**        | N/A (figma_spec.md not yet generated)                                                                                   |
| **UXR Requirements**   | N/A (figma_spec.md not yet generated)                                                                                   |
| **Design Tokens**      | N/A (designsystem.md not yet generated)                                                                                 |

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

Implement the Admin Panel "Audit Log" page — a read-only, HIPAA-compliant interface giving Admins visibility into all platform audit events. The page consists of:

- **Filter panel** — inline `mat-form-field` controls for date range (`MatDatepickerModule`), free-text user ID, action type (`mat-select`), and entity type (`mat-select`). Filter state is managed in the `AuditLogStore`; applying filters triggers a new API call with updated query parameters.
- **Event table** — `mat-table` sorted descending by `timestamp`; columns: `userId`, `role`, `entityType`, `entityId`, `actionType`, `ipAddress`, `timestamp` (formatted UTC). Rows are not selectable or copyable (read-only). Cursor-based pagination: "Load more" button appends the next 50 records using the `nextCursor` token from the API response; a total-count badge shows the aggregate record count.
- **Diff expansion panel** — each row has a `mat-expansion-panel` (or `mat-icon` expand toggle) that, when opened, renders a `DiffViewComponent` showing the `details` JSONB field as a structured before/after table. Only visible when the `details` field is non-null (i.e., clinical modification events per FR-058).
- **No export controls** — explicitly no download, CSV, or copy buttons anywhere on the page (FR-059 hard requirement).

The route `/admin/audit-log` is protected by the existing `AdminRoleGuard` (created in US_045).

---

## Dependent Tasks

- `task_001_fe_admin_user_management_ui.md` (EP-009/us_045) — `AdminRoleGuard` and admin module route structure must exist.
- `task_002_be_audit_log_api.md` (EP-009/us_047) — `GET /api/admin/audit-logs` endpoint with cursor-based pagination and filter query params must be available.

---

## Impacted Components

| Component                             | Module               | Action                                                                                                                                     |
| ------------------------------------- | -------------------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| `AuditLogPageComponent` (new)         | Admin Feature Module | CREATE — Routed page: hosts filter panel, event table, load-more pagination                                                                |
| `AuditEventTableComponent` (new)      | Admin Feature Module | CREATE — `mat-table` with 7 columns + expand toggle; renders `DiffViewComponent` in expansion row                                          |
| `AuditFilterPanelComponent` (new)     | Admin Feature Module | CREATE — Filter controls: date range, userId text, actionType select, entityType select                                                    |
| `DiffViewComponent` (new)             | Admin Shared         | CREATE — Renders before/after state from JSONB `details` as a structured two-column table                                                  |
| `AuditLogStore` (new)                 | Admin State          | CREATE — NgRx Signals store: `events`, `loading`, `filters`, `nextCursor`, `totalCount`, `loadAuditLogs()`, `loadMore()`, `applyFilters()` |
| `AuditLogService` (new)               | Admin Data Access    | CREATE — Angular service: `getAuditLogs(params)` → `GET /api/admin/audit-logs` with filter query params and cursor                         |
| `admin.routes.ts` (existing — US_045) | App Routing          | MODIFY — Add `{ path: 'audit-log', component: AuditLogPageComponent, canActivate: [AdminRoleGuard] }`                                      |

---

## Implementation Plan

1. **Define `AuditEventDto` interface** — Client-side type:

   ```typescript
   interface AuditEventDto {
     id: string;
     userId: string;
     userRole: "Patient" | "Staff" | "Admin";
     entityType: string;
     entityId: string;
     actionType: "Create" | "Read" | "Update" | "Delete";
     ipAddress: string;
     timestamp: string; // ISO 8601 UTC
     details: AuditEventDetails | null; // non-null for FR-058 clinical events
   }
   interface AuditEventDetails {
     before: Record<string, unknown>;
     after: Record<string, unknown>;
   }
   interface AuditLogResponse {
     events: AuditEventDto[];
     nextCursor: string | null;
     totalCount: number;
   }
   ```

2. **Implement `AuditLogService`** — Single method:
   - `getAuditLogs(params: AuditLogQueryParams): Observable<AuditLogResponse>` → `GET /api/admin/audit-logs` with query string: `cursor?`, `dateFrom?`, `dateTo?`, `userId?`, `actionType?`, `entityType?`.
   - `pageSize` is always 50 (fixed — not exposed as a param).

3. **Implement `AuditLogStore`** — NgRx Signals store:
   - State: `events: AuditEventDto[]`, `loading: boolean`, `filters: AuditLogQueryParams`, `nextCursor: string | null`, `totalCount: number`.
   - `loadAuditLogs()`: resets `events` to `[]`, clears `nextCursor`, calls service with current `filters`, sets all state on response.
   - `loadMore()`: calls service with `cursor = nextCursor`; appends results to existing `events` (do not reset).
   - `applyFilters(filters)`: sets `filters` signal, then calls `loadAuditLogs()` to refresh from the first page.

4. **Implement `DiffViewComponent`** — Input: `details: AuditEventDetails`. Renders a two-column `mat-table`-like structure (field name | before value | after value). Highlights changed fields using Angular class binding (`[class.changed]="before[key] !== after[key]"`). Falls back to "No detail available" if `details` is null.

5. **Implement `AuditEventTableComponent`** — Standalone `mat-table`:
   - Columns: `userId`, `userRole`, `entityType`, `entityId`, `actionType`, `ipAddress`, `timestamp`, `expand`.
   - `expand` column: `mat-icon-button` (chevron) toggles an expansion row beneath the clicked row using a `expandedRow` tracked signal; expansion row renders `<app-diff-view>` only when `event.details !== null`.
   - No sort headers (server returns time-ordered data); no `mat-paginator` (cursor-based "load more" pattern).
   - "Load More" button at the bottom: disabled when `nextCursor === null`; calls `AuditLogStore.loadMore()`.
   - Total count badge displayed above table: "Showing {{events.length}} of {{totalCount}} events".

6. **Implement `AuditFilterPanelComponent`** — Reactive form group:
   - `dateFrom` / `dateTo`: `MatDatepicker` inputs (ISO 8601 strings passed to store).
   - `userId`: plain text input.
   - `actionType`: `mat-select` with options Create | Read | Update | Delete | (All).
   - `entityType`: `mat-select` populated from a static list of known entity types (Patient, Appointment, Document, IntakeForm, MedicalCode, DataConflict, User, AuditLog — the last is excluded from filtering).
   - "Apply" button triggers `AuditLogStore.applyFilters(form.value)`.
   - "Clear" button resets form and calls `applyFilters({})`.

7. **Implement `AuditLogPageComponent`** — Container:
   - On `ngOnInit`: calls `AuditLogStore.loadAuditLogs()`.
   - Renders `<app-audit-filter-panel>` and `<app-audit-event-table>` stacked vertically.
   - **No export / download button present anywhere** (FR-059 hard requirement — verified in code review checklist).

8. **Add route to `admin.routes.ts`**:
   - `{ path: 'audit-log', component: AuditLogPageComponent, canActivate: [AdminRoleGuard] }`.

---

## Current Project State

```
app/
  admin/
    pages/
      user-management/
        user-management.page.ts         ← EXISTS (US_045 task_001)
      audit-log/                        ← folder to create
    components/
      user-table/                       ← EXISTS
      reauth-modal/                     ← EXISTS (US_046 task_001)
      audit-event-table/                ← folder to create
      audit-filter-panel/               ← folder to create
      diff-view/                        ← folder to create
    store/
      admin-user.store.ts               ← EXISTS
      audit-log.store.ts                ← file to create
    services/
      admin-user.service.ts             ← EXISTS
      audit-log.service.ts              ← file to create
    admin.routes.ts                     ← EXISTS (US_045) — MODIFY
```

---

## Expected Changes

| Action | File Path                                                                 | Description                                                                                                 |
| ------ | ------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| CREATE | `app/admin/pages/audit-log/audit-log.page.ts`                             | Routed page: loads on init, hosts filter panel + event table                                                |
| CREATE | `app/admin/components/audit-event-table/audit-event-table.component.ts`   | mat-table with 7 columns, row expand toggle, DiffViewComponent, Load More button, total-count badge         |
| CREATE | `app/admin/components/audit-filter-panel/audit-filter-panel.component.ts` | Reactive filter form: date range, userId, actionType, entityType selects; Apply/Clear actions               |
| CREATE | `app/admin/components/diff-view/diff-view.component.ts`                   | Before/after structured diff table from JSONB details; null-safe fallback                                   |
| CREATE | `app/admin/store/audit-log.store.ts`                                      | NgRx Signals store: events, loading, filters, nextCursor, totalCount; loadAuditLogs, loadMore, applyFilters |
| CREATE | `app/admin/services/audit-log.service.ts`                                 | HTTP service: getAuditLogs(params) → GET /api/admin/audit-logs                                              |
| MODIFY | `app/admin/admin.routes.ts`                                               | Add `/audit-log` route with `AuditLogPageComponent` and `AdminRoleGuard`                                    |

---

## External References

- [Angular Material 18 — Table with expandable rows](https://material.angular.io/components/table/overview#expanding-rows) — `mat-expansion-panel` inside `mat-table` row
- [Angular Material 18 — Datepicker](https://material.angular.io/components/datepicker/overview) — `MatDatepickerModule` for date range filter inputs
- [Angular Material 18 — Select](https://material.angular.io/components/select/overview) — `mat-select` for actionType and entityType filter dropdowns
- [NgRx Signals Store — cursor pagination pattern](https://ngrx.io/guide/signals/signal-store) — Append-on-loadMore vs. replace-on-filter pattern
- [FR-057 (spec.md)](../.propel/context/docs/spec.md) — Audit log fields: userId, role, entityType, entityId, actionType, ipAddress, UTC timestamp
- [FR-058 (spec.md)](../.propel/context/docs/spec.md) — Clinical modification events include before/after state (drives DiffViewComponent)
- [FR-059 (spec.md)](../.propel/context/docs/spec.md) — No modification, deletion, or **export** of audit records from within the application
- [NFR-009 (design.md)](../.propel/context/docs/design.md) — Immutable audit log for all user actions involving patient data
- [AD-7 (design.md)](../.propel/context/docs/design.md) — Append-only audit log; INSERT-only repository pattern

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for Angular build and serve commands.

---

## Implementation Validation Strategy

- [ ] Unit tests pass (Jest / Angular Testing Library)
- [ ] Audit log page loads and displays event list on `ngOnInit`
- [ ] All 7 data columns render correctly for a sample event
- [ ] Events are displayed in descending timestamp order
- [ ] Filter panel: Apply triggers store refresh from first page; Clear resets all filters
- [ ] Cursor pagination: "Load More" appends next 50 records; button disabled when `nextCursor === null`
- [ ] Total-count badge displays correct "Showing X of Y events" text
- [ ] `DiffViewComponent`: expands correctly for events with non-null `details`; shows "No detail available" for null `details`
- [ ] **FR-059 check**: confirm no export, download, or copy button exists anywhere on the page
- [ ] `/admin/audit-log` route blocked for Staff and Patient roles; redirected to dashboard via `AdminRoleGuard`
- [ ] **[UI Tasks]** Visual comparison against wireframe (when wireframe becomes AVAILABLE)

---

## Implementation Checklist

- [x] Define `AuditEventDto`, `AuditEventDetails`, `AuditLogResponse`, `AuditLogQueryParams` interfaces
- [x] Create `AuditLogService` with `getAuditLogs(params)` HTTP method (GET with query string)
- [x] Create `AuditLogStore` (NgRx Signals): events, loading, filters, nextCursor, totalCount; loadAuditLogs, loadMore, applyFilters
- [x] Create `DiffViewComponent`: before/after table from JSONB details; null-safe fallback text
- [x] Create `AuditFilterPanelComponent`: date range + userId + actionType + entityType; Apply/Clear buttons
- [x] Create `AuditEventTableComponent`: 7-column mat-table, expand toggle, DiffViewComponent row, Load More button, total-count badge — **no export controls**
- [x] Create `AuditLogPageComponent`: ngOnInit load, renders filter panel + table stacked
- [x] Extend `admin.routes.ts` with `/audit-log` route protected by `AdminRoleGuard`
