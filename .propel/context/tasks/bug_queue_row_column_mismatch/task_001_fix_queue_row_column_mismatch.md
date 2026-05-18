# Bug Fix Task - bug_queue_row_column_mismatch

## Bug Report Reference

- Bug ID: bug_queue_row_column_mismatch
- Source: Visual comparison — wireframe SCR-014 (Same-Day Queue, Hi-Fidelity) vs. running Angular application

## Bug Summary

### Issue Classification

- **Priority**: High
- **Severity**: Core feature degraded — staff cannot see chief complaint, wait time, risk level, or correct queue position; key clinical triage data is hidden
- **Affected Version**: Current `main` branch
- **Environment**: Angular 17+, browser (Chromium), `/staff/queue` route

### Steps to Reproduce

1. Log in as a Staff or Admin user.
2. Navigate to `/staff/queue` (Same-Day Queue page).
3. Observe the queue table rows rendered by `QueueRowComponent`.

- **Expected**: Each row shows **eight** aligned columns matching the `<thead>`: Position (#1, #2…), Patient, Chief Complaint (text), Arrival Time, Wait Time, Risk (High / Medium / Low badge), Status, Actions (Arrived button + 360° link).
- **Actual**: Each row shows only **five** misaligned cells — Position renders the raw time-slot value ("09:00:00"), Chief Complaint renders a booking-type badge ("Self-Booked"), and the Arrival Time, Wait Time, Risk, and 360° link columns are absent entirely.

**Error Output**:

```text
No runtime error thrown.
Visual regression only:
- <td class="col-time"> renders "09:00:00" under "Position" header
- <td class="col-type"> renders <app-booking-type-badge> under "Chief complaint" header
- Arrival time, Wait, Risk, and 360° action <td> cells are missing
- Table has 8 <th> headers but only 5 <td> cells per row → browser skews layout
```

### Root Cause Analysis

- **File**: `app/src/app/features/staff/queue/queue-row/queue-row.component.ts:37-58` (inline template)
- **Component**: `QueueRowComponent`
- **Function**: inline `template` — `<tr class="queue-row">` block
- **Cause**:
  The `QueueRowComponent` was built against an earlier data contract that only surfaced `timeSlotStart`, `bookingType`, and `arrivalStatus`. When the parent template (`same-day-queue.component.html`) was later aligned to the SCR-014 wireframe (8-column layout), the row component was **not updated** to match. This introduced a structural mismatch: 8 `<th>` headers vs 5 `<td>` cells per row, and the wrong fields bound to the visible cells.

  Secondary cause: `QueueItem` model (`queue.models.ts`) is missing the fields required by the wireframe — `chiefComplaint`, `waitTime`, `riskLevel`, and `queuePosition` — so even correcting the template requires a model and backend DTO extension.

### Impact Assessment

- **Affected Features**: Same-Day Queue table — the primary clinical triage view for nursing staff
- **User Impact**: Staff cannot see chief complaint text, wait duration, or patient risk level; Position column shows a time string instead of queue number — critical triage context is invisible
- **Data Integrity Risk**: No — read-only display defect, no data is written incorrectly
- **Security Implications**: None

## Fix Overview

1. Extend `QueueItem` model with `chiefComplaint: string`, `waitTime: string | null`, `riskLevel: 'High' | 'Medium' | 'Low' | null`, and `queuePosition: number`.
2. Rewrite `QueueRowComponent` inline template to render **eight aligned `<td>` cells** matching the parent `<thead>` in `same-day-queue.component.html`.
3. Update `QueueService` response-mapping to populate the new fields from the backend DTO.
4. Update the backend `QueueItemDto` (C# response model) to include `chiefComplaint`, `waitTime`, and `riskLevel` fields.

## Fix Dependencies

- Backend API must return `chiefComplaint`, `waitTime`, and `riskLevel` in the queue endpoint response before the frontend mapping can be exercised end-to-end.
- The risk badge styles (`risk--high`, `risk--medium`, `risk--low`) must exist in the component stylesheet (or a shared token) — add inline styles if absent.

## Impacted Components

### Frontend — Angular

- `app/src/app/features/staff/queue/queue.models.ts` — **MODIFY**: add new fields to `QueueItem`
- `app/src/app/features/staff/queue/queue-row/queue-row.component.ts` — **MODIFY**: rewrite inline template + update styles
- `app/src/app/features/staff/queue/queue.service.ts` — **MODIFY**: map new DTO fields
- `app/src/app/features/staff/queue/same-day-queue.component.ts` — **MODIFY**: pass `$index + 1` as `queuePosition` input to `QueueRowComponent`

### Backend — .NET

- `server/…/Dtos/QueueItemDto.cs` — **MODIFY**: add `ChiefComplaint`, `WaitTime`, `RiskLevel` properties
- `server/…/Services/QueueService.cs` (or equivalent mapping layer) — **MODIFY**: populate new DTO fields from appointment/triage data

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | `app/src/app/features/staff/queue/queue.models.ts` | Add `chiefComplaint: string`, `waitTime: string \| null`, `riskLevel: 'High' \| 'Medium' \| 'Low' \| null`, `queuePosition: number` to `QueueItem` |
| MODIFY | `app/src/app/features/staff/queue/queue-row/queue-row.component.ts` | Rewrite inline `<tr>` template: 8 cells — Position (#N), Patient, Chief Complaint (text), Arrival Time (formatted), Wait Time, Risk badge, Status chip, Actions (Arrived/Undo + 360° link) |
| MODIFY | `app/src/app/features/staff/queue/same-day-queue.component.ts` | Bind `[queuePosition]="i + 1"` on `<app-queue-row>` |
| MODIFY | `app/src/app/features/staff/queue/queue.service.ts` | Map `chiefComplaint`, `waitTime`, `riskLevel` from backend DTO response |
| MODIFY | Backend `QueueItemDto.cs` | Add `string ChiefComplaint`, `string? WaitTime`, `string? RiskLevel` properties |

## Implementation Plan

1. **Extend `QueueItem` model** (`queue.models.ts`):
   - Add `queuePosition: number`
   - Add `chiefComplaint: string`
   - Add `waitTime: string | null` (e.g., `"47 min"`)
   - Add `riskLevel: 'High' | 'Medium' | 'Low' | null`

2. **Update `QueueRowComponent` template** (`queue-row.component.ts`):
   - Add `@Input() queuePosition!: number` input
   - Replace the 5-cell inline template with an 8-cell template:
     - Cell 1 — Position: `#{{ queuePosition }}`
     - Cell 2 — Patient: `{{ item.patientName }}` + MRN sub-line
     - Cell 3 — Chief Complaint: `{{ item.chiefComplaint }}`
     - Cell 4 — Arrival Time: `{{ item.timeSlotStart }}`
     - Cell 5 — Wait Time: `{{ item.waitTime ?? '—' }}`
     - Cell 6 — Risk: span with class `risk--{{ item.riskLevel?.toLowerCase() }}` or `'—'`
     - Cell 7 — Status: `<app-queue-status-chip [status]="item.arrivalStatus" />`
     - Cell 8 — Actions: existing Arrived / Undo buttons + `360°` router link to patient chart
   - Update inline styles to add `.risk--high` (red), `.risk--medium` (orange), `.risk--low` (green) classes

3. **Update parent component binding** (`same-day-queue.component.html` / `.ts`):
   - Pass `[queuePosition]="i + 1"` to `<app-queue-row>`

4. **Update `QueueService`** to map the three new fields from the backend JSON payload.

5. **Update backend `QueueItemDto`** with the three new properties and populate them in the query/mapper.

6. **Manual smoke test**: Load `/staff/queue` and verify 8 columns render with correct data matching the SCR-014 wireframe.

## Regression Prevention Strategy

- [x] Unit test for `QueueRowComponent`: verify 8 `<td>` cells are rendered when a fully-populated `QueueItem` is bound
- [x] Unit test for `QueueRowComponent`: verify `#{{ queuePosition }}` renders in position cell
- [x] Unit test for `QueueRowComponent`: verify `chiefComplaint` text appears (not `bookingType` badge) in chief-complaint cell
- [x] Unit test for `QueueRowComponent`: verify risk badge class is `risk--high` / `risk--medium` / `risk--low` based on `riskLevel`
- [x] Unit test for `QueueRowComponent`: verify `waitTime` falls back to `—` when `null`
- [ ] Integration test: `GET /api/queue` response includes `chiefComplaint`, `waitTime`, `riskLevel` fields

## Rollback Procedure

1. Revert commits affecting `queue-row.component.ts`, `queue.models.ts`, `queue.service.ts` via `git revert`.
2. Validate `/staff/queue` page loads without JavaScript errors.
3. No database migration needed — read-only display change.

## External References

- Wireframe: `.propel/context/wireframes/Hi-Fi/wireframe-SCR-014-same-day-queue.html`
- Angular `@Input` signal API: <https://angular.dev/guide/components/inputs>
- WCAG 2.2 AA — 1.3.1 Info and Relationships (table column alignment)

## Build Commands

```bash
# Frontend
cd app
npm run build

# Run unit tests
npm test -- --include="**/queue-row*"
```

## Implementation Validation Strategy

- [ ] Navigate to `/staff/queue` — table shows exactly 8 columns with headers matching SCR-014 wireframe
- [ ] Position column shows `#1`, `#2`, `#3`… (not raw time strings)
- [ ] Chief complaint column shows appointment complaint text (not booking-type badge)
- [ ] Wait time and risk columns render real data or `—` placeholder
- [x] All existing queue-row unit tests pass (24/24 — Chrome 148, Karma 6.4)
- [x] New regression tests pass (24/24)

## Implementation Checklist

- [x] Add 4 new fields to `QueueItem` model
- [x] Add `@Input() queuePosition` to `QueueRowComponent`
- [x] Rewrite `QueueRowComponent` inline template to 8 cells
- [x] Add risk badge CSS classes to `QueueRowComponent` styles
- [x] Bind `[queuePosition]="i + 1"` in parent template
- [x] Map new fields in `QueueService`
- [x] Update backend `QueueItemDto` and mapper
- [x] Write/run unit regression tests for `QueueRowComponent`
