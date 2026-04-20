# Task - task_001_fe_360_view_ui

## Requirement Reference

- **User Story:** us_041 — 360-Degree Patient View Aggregation & Staff Verification
- **Story Location:** `.propel/context/tasks/EP-008-I/us_041/us_041.md`
- **Acceptance Criteria:**
  - AC-1: 360-degree view page loads and displays aggregated sections for Vitals, Medications, Diagnoses, Allergies, Immunizations, and Surgical History with duplicates collapsed into single entries
  - AC-2: Each data element shows a source citation (document name, page number, uploaded timestamp) and a confidence indicator; fields below 80% confidence are visually flagged for priority review
  - AC-3: "Verify Profile" button updates profile status to Verified when clicked (success state shown in UI)
  - AC-4: "Verify Profile" is blocked with a message listing unresolved Critical conflicts when they exist
- **Edge Cases:**
  - >10 documents: progress indicator is shown with message "Verification available — SLA not applicable for >10 documents"
  - Extraction failure: failed document shows a "Processing Failed" badge and a Retry button per document

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value |
| ---------------------- | ----- |
| **UI Impact**          | Yes   |
| **Figma URL**          | N/A   |
| **Wireframe Status**   | PENDING |
| **Wireframe Type**     | N/A   |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-360-VIEW-patient.[html\|png\|jpg]` or provide external URL |
| **Screen Spec**        | N/A (figma_spec.md not yet generated) |
| **UXR Requirements**   | N/A (figma_spec.md not yet generated) |
| **Design Tokens**      | N/A (designsystem.md not yet generated) |

---

## Applicable Technology Stack

| Layer              | Technology                     | Version |
| ------------------ | ------------------------------ | ------- |
| Frontend           | Angular                        | 18.x    |
| Frontend State     | NgRx Signals                   | 18.x    |
| Frontend UI        | Angular Material               | 18.x    |
| Frontend Routing   | Angular Router                 | 18.x    |
| HTTP Client        | Angular HttpClient             | 18.x    |
| Testing — Unit     | Jest / Angular Testing Library | —       |
| AI/ML              | N/A (FE renders pre-aggregated data from API) | N/A |
| Mobile             | N/A                            | N/A     |

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

Implement the Angular 18 UI for the 360-degree patient view feature. The page presents six aggregated clinical sections (Vitals, Medications, Diagnoses, Allergies, Immunizations, Surgical History), each rendered as an expandable `mat-expansion-panel`. Individual data rows show source citation tooltips and confidence badges, with a distinct visual flag for sub-80% confidence fields.

A "Verify Profile" button drives the Trust-First verification workflow: it is disabled when unresolved Critical conflicts exist (with an inline conflict list), and shows a success confirmation on completion. Document-level failure badges with retry actions address the extraction-failure edge case.

All interactive elements comply with WCAG 2.2 AA — confidence flags use both colour and icon (not colour alone), conflict messages are announced via `aria-live`, and the verify button exposes `aria-disabled` with a descriptive `aria-describedby` when blocked.

---

## Dependent Tasks

- **EP-008-I/us_041/task_002_be_360_aggregation_api** — `GET /api/staff/patients/{patientId}/360-view` and `POST /api/staff/patients/{patientId}/360-view/verify` must be in place before UI integration
- **EP-008-I/us_041/task_003_ai_deduplication_service** — de-duplication and confidence data must be pre-populated in the API response
- **EP-001/us_011** — Staff-role JWT and `StaffGuard` required

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `Patient360ViewComponent` (routed page component) | `client/src/app/features/staff/patients/patient-360-view/patient-360-view.component.ts` |
| CREATE | `ClinicalSectionComponent` (expandable panel per data type) | `client/src/app/features/staff/patients/patient-360-view/clinical-section/clinical-section.component.ts` |
| CREATE | `ClinicalDataRowComponent` (standalone row with citation + confidence badge) | `client/src/app/features/staff/patients/patient-360-view/clinical-data-row/clinical-data-row.component.ts` |
| CREATE | `ConfidenceBadgeComponent` (standalone badge: colour + icon, WCAG compliant) | `client/src/app/shared/components/confidence-badge/confidence-badge.component.ts` |
| CREATE | `Patient360ViewStore` (NgRx Signals) | `client/src/app/features/staff/patients/patient-360-view/patient-360-view.store.ts` |
| CREATE | `Patient360ViewService` (HttpClient wrapper) | `client/src/app/core/services/patient-360-view.service.ts` |
| MODIFY | Staff patient routing module | Add `/staff/patients/:patientId/360-view` → `Patient360ViewComponent` (lazy, `StaffGuard`) |

---

## Implementation Plan

1. **`Patient360ViewService`** — HttpClient wrapper with typed DTOs:
   ```typescript
   interface Patient360ViewDto {
     patientId: string;
     verificationStatus: 'Unverified' | 'Verified';
     verifiedAt?: string;
     verifiedByStaffName?: string;
     unresolvedCriticalConflicts: ConflictSummaryDto[];
     documents: DocumentStatusDto[];
     sections: ClinicalSectionDto[];
   }
   interface ClinicalSectionDto {
     sectionType: 'Vitals' | 'Medications' | 'Diagnoses' | 'Allergies' | 'Immunizations' | 'SurgicalHistory';
     items: ClinicalItemDto[];
   }
   interface ClinicalItemDto {
     fieldName: string;
     value: string;
     confidence: number;    // 0–1
     isLowConfidence: boolean;  // confidence < 0.8
     sources: SourceCitationDto[];
   }
   interface SourceCitationDto {
     documentName: string;
     pageNumber?: number;
     uploadedAt: string;
     textSnippet?: string;
   }
   interface ConflictSummaryDto { fieldName: string; reason: string; }
   interface DocumentStatusDto { documentName: string; status: 'Completed' | 'Failed'; }
   get360View(patientId: string): Observable<Patient360ViewDto>;
   verifyProfile(patientId: string): Observable<VerifyProfileResponseDto>;
   ```

2. **`Patient360ViewStore`** (NgRx Signals):
   - `view360: Signal<Patient360ViewDto | null>`
   - `loadingState: Signal<'idle' | 'loading' | 'loaded' | 'error'>`
   - `verifyState: Signal<'idle' | 'loading' | 'success' | 'error'>`
   - `load360View(patientId: string)` and `verifyProfile(patientId: string)` methods

3. **`ConfidenceBadgeComponent`** (standalone input-driven, WCAG 2.2 AA):
   - `@Input() confidence: number` (0–1)
   - Renders `<mat-chip>` with colour class + warning icon (`mat-icon`) for confidence < 0.8
   - `aria-label`: `"Confidence: ${(confidence * 100).toFixed(0)}%${isLow ? ' — low confidence, priority review required' : ''}"` — colour not the sole indicator

4. **`ClinicalDataRowComponent`** (standalone):
   - Displays `fieldName`, `value`, `<app-confidence-badge>`, and a `matTooltip` on the citation icon showing document name, page number, and upload date
   - Row highlighted with `class="low-confidence-row"` when `isLowConfidence = true`

5. **`ClinicalSectionComponent`** (standalone):
   - Wraps each section in `<mat-expansion-panel>` with section title and item count in header
   - Iterates `items` with `<app-clinical-data-row>` per item
   - `panelOpenState` signal per section for expand/collapse accessibility

6. **`Patient360ViewComponent`** (routed):
   - Renders six `<app-clinical-section>` panels for each `sectionType`
   - Shows "Verify Profile" `<button mat-raised-button color="primary">` — disabled when `unresolvedCriticalConflicts.length > 0` or `verifyState() === 'loading'`
   - Conflict block: `<mat-card class="conflict-warning">` listing each `unresolvedCriticalConflict` with `aria-live="assertive"` when block becomes visible
   - Success confirmation: "Profile verified: [timestamp] by [staff name]" shown after `verifyState() === 'success'`
   - Document failure row: per failed document in `documents`, shows `<mat-chip class="failed-badge">Processing Failed</mat-chip>` + retry button calling re-trigger API
   - >10 documents banner: if `documents.length > 10`, show `<mat-banner>` "Showing data from all documents — 2-minute SLA applies to ≤10 documents"

7. **Progress/Loading state**: `<mat-progress-bar mode="indeterminate">` shown during `loadingState() === 'loading'`; for >10 document count, show spinner with "Aggregating data, this may take a moment..."

---

## Current Project State

```
client/
├── src/
│   └── app/
│       ├── core/
│       │   └── services/                     # Patient360ViewService to be added
│       ├── shared/
│       │   └── components/                   # ConfidenceBadgeComponent to be added
│       └── features/
│           └── staff/
│               └── patients/
│                   └── patient-360-view/     # New folder — all 360-view components here
```

> Placeholder — update tree once task_002 is complete and route is confirmed.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `client/src/app/core/services/patient-360-view.service.ts` | HttpClient wrapper: `get360View()` and `verifyProfile()` with typed DTOs |
| CREATE | `client/src/app/features/staff/patients/patient-360-view/patient-360-view.store.ts` | NgRx Signals store: `view360`, `loadingState`, `verifyState`, load/verify actions |
| CREATE | `client/src/app/features/staff/patients/patient-360-view/patient-360-view.component.ts` | Routed page with sections, verify button, conflict list, document failure badges |
| CREATE | `client/src/app/features/staff/patients/patient-360-view/clinical-section/clinical-section.component.ts` | Expandable panel per clinical data type |
| CREATE | `client/src/app/features/staff/patients/patient-360-view/clinical-data-row/clinical-data-row.component.ts` | Row with citation tooltip and confidence badge |
| CREATE | `client/src/app/shared/components/confidence-badge/confidence-badge.component.ts` | Reusable confidence badge (colour + icon, WCAG AA) |
| MODIFY | Staff routing module | Add lazy route `/staff/patients/:patientId/360-view` with `StaffGuard` |

---

## External References

- [Angular 18 Signals](https://angular.dev/guide/signals)
- [NgRx Signals Store](https://ngrx.io/guide/signals)
- [Angular Material Expansion Panel](https://material.angular.io/components/expansion/overview)
- [Angular Material Chips](https://material.angular.io/components/chips/overview)
- [Angular Material Tooltip](https://material.angular.io/components/tooltip/overview)
- [WCAG 2.2 AA — Use of Color (1.4.1)](https://www.w3.org/WAI/WCAG22/Understanding/use-of-color.html)
- [WCAG 2.2 AA — Status Messages (4.1.3)](https://www.w3.org/WAI/WCAG22/Understanding/status-messages.html)

---

## Build Commands

- Frontend build: `ng build --configuration production` (from `client/` folder)
- Frontend serve: `ng serve`
- Frontend tests: `ng test`

---

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Six clinical sections render with correct section titles and item counts
- [ ] Source citation tooltip shows document name, page number, and upload timestamp on hover
- [ ] Fields with `confidence < 0.8` display warning icon + distinct highlight (not colour alone — WCAG 1.4.1)
- [ ] "Verify Profile" button is visually disabled and shows conflict list when `unresolvedCriticalConflicts.length > 0`
- [ ] After successful verification, confirmation panel shows staff name and timestamp
- [ ] Documents with `status = Failed` show "Processing Failed" badge and Retry button
- [ ] >10 document banner renders correctly
- [ ] Lighthouse accessibility score ≥ 90 on 360-view page
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px once wireframe is available

---

## Implementation Checklist

- [ ] Create `Patient360ViewService` with `get360View()` and `verifyProfile()` typed observables
- [ ] Create `Patient360ViewStore` (NgRx Signals) with `view360`, `loadingState`, `verifyState`, and `load360View()` / `verifyProfile()` actions
- [ ] Create standalone `ConfidenceBadgeComponent` — colour chip + warning icon for < 80%, full `aria-label` description (WCAG AA)
- [ ] Create standalone `ClinicalDataRowComponent` — field/value display, source citation tooltip, confidence badge, low-confidence row highlight class
- [ ] Create `ClinicalSectionComponent` — `mat-expansion-panel` per section type with item count in header
- [ ] Implement `Patient360ViewComponent` with six section panels, verify button (disabled on conflicts), conflict block (`aria-live="assertive"`), and verification success confirmation
- [ ] Add document failure badges (per document) with Retry button and >10 documents SLA banner
- [ ] Register lazy route `/staff/patients/:patientId/360-view` with `StaffGuard` in staff routing module
