# Task - task_002_fe_conflict_review_ui

## Requirement Reference

- **User Story:** us_044 — Data Conflict Detection, Visual Highlighting & Resolution
- **Story Location:** `.propel/context/tasks/EP-008-II/us_044/us_044.md`
- **Acceptance Criteria:**
  - AC-2: When Staff view the 360-degree view and conflicts exist in the affected fields, each conflict is displayed with a visually distinct indicator — red border for `Critical`, amber border for `Warning` — showing both conflicting values side by side with the source document identified for each.
  - AC-3: When Staff select the authoritative value (or enter a free-text third value) and submit the resolution, the conflict card transitions to a "Resolved" visual state; the resolution is sent to `POST /api/conflicts/{id}/resolve`.
  - AC-4: When Staff attempt to click "Verify Profile" and there are unresolved Critical conflicts, the action is blocked with a modal listing each unresolved Critical conflict that must be resolved first.
- **Edge Cases:**
  - Free-text third value: the resolution form includes an "Enter custom value" field alongside the two AI-detected options; all three choices are mutually exclusive radio/toggle options.
  - New document upload after resolution: on page reload, newly detected conflict cards appear as Unresolved; resolved cards retain their Resolved visual state from the API response.

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value                                                                                                                                             |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| **UI Impact**          | Yes                                                                                                                                               |
| **Figma URL**          | N/A                                                                                                                                               |
| **Wireframe Status**   | PENDING                                                                                                                                           |
| **Wireframe Type**     | N/A                                                                                                                                               |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-conflict-resolution.[html\|png\|jpg]` or provide external URL                 |
| **Screen Spec**        | N/A (figma_spec.md not yet generated)                                                                                                             |
| **UXR Requirements**   | N/A (figma_spec.md not yet generated)                                                                                                             |
| **Design Tokens**      | N/A (designsystem.md not yet generated)                                                                                                           |

> **Wireframe Status: PENDING** — Implement using component-level layout described in the Implementation Plan. Align to wireframe when it becomes AVAILABLE.

---

## Applicable Technology Stack

| Layer             | Technology             | Version |
| ----------------- | ---------------------- | ------- |
| Frontend          | Angular                | 18.x    |
| Frontend State    | NgRx Signals           | 18.x    |
| Frontend UI       | Angular Material       | 18.x    |
| Frontend Routing  | Angular Router         | 18.x    |
| HTTP Client       | Angular HttpClient     | 18.x    |
| Testing — Unit    | Jest / Angular Testing Library | — |
| AI/ML             | N/A                    | N/A     |
| Mobile            | N/A                    | N/A     |

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

Extend the existing 360-degree patient view (built in US_041) to embed conflict highlighting directly within affected data field cards, and introduce a conflict resolution form that allows Staff to select or enter the authoritative value. Each conflicted field card gains a severity-coded border (red for `Critical`, amber for `Warning`) and a side-by-side display of the two conflicting values with their source documents. The "Verify Profile" button is guarded by an Angular route/action guard that blocks action when any `Critical` conflict remains `Unresolved`, surfacing a modal with the list of blockers. The frontend does not detect conflicts itself — all conflict data is loaded from the `GET /api/patients/{id}/360view` response payload, which now includes a `conflicts[]` array.

---

## Dependent Tasks

- `task_003_be_conflict_resolution_api.md` (EP-008-II/us_044) — `POST /api/conflicts/{id}/resolve` and `GET /api/patients/{id}/conflicts` endpoints MUST be available.
- `task_001_fe_360_view_ui.md` (EP-008-I/us_041) — The 360-degree view page component MUST exist as this task extends it with conflict highlighting.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `Patient360ViewPageComponent` (existing) | Clinical Feature Module | MODIFY — Consume `conflicts[]` from API response; pass to field cards |
| `ConflictHighlightDirective` (new) | Clinical Feature Module | CREATE — Structural directive: applies `conflict-critical` or `conflict-warning` CSS class and border based on severity |
| `ConflictCardComponent` (new) | Clinical Feature Module | CREATE — Side-by-side conflict display: value1 + sourceDoc1 vs value2 + sourceDoc2; resolution form |
| `ConflictResolutionFormComponent` (new) | Clinical Feature Module | CREATE — Radio group (choose value1 / choose value2 / enter custom) + submit button |
| `UnresolvedCriticalBlockerModalComponent` (new) | Clinical Feature Module | CREATE — `MatDialog` listing all unresolved Critical conflicts; triggered before "Verify Profile" action |
| `ClinicalConflictStore` (new) | Clinical State | CREATE — NgRx Signals slice: `conflicts` signal, `unresolvedCriticalCount` computed signal, `resolveConflict` method |
| `ConflictService` (new) | Clinical Data Access | CREATE — Angular service: `getConflicts(patientId)`, `resolveConflict(conflictId, payload)` |
| `clinical-360.store.ts` (existing) | Clinical State | MODIFY — Integrate `ClinicalConflictStore`; expose `canVerify` computed signal (`unresolvedCriticalCount === 0`) |
| `clinical.styles.scss` (existing) | Styles | MODIFY — Add `.conflict-critical` (red border, background tint) and `.conflict-warning` (amber border) utility classes |

---

## Implementation Plan

1. **Define conflict state in `ClinicalConflictStore`** — NgRx Signals store with:
   - `conflicts` signal: `DataConflictDto[]` (loaded from API or from 360-view response payload)
   - `unresolvedCriticalCount` computed signal: count where `severity === 'Critical' && resolutionStatus === 'Unresolved'`
   - `resolveConflict(conflictId, resolvedValue)` method: optimistic update → sets local status to `Resolved` → calls `ConflictService.resolveConflict()`

2. **Implement `ConflictService`** — Angular injectable:
   - `getConflicts(patientId: string): Observable<DataConflictDto[]>` → `GET /api/patients/{patientId}/conflicts`
   - `resolveConflict(conflictId: string, payload: ResolveConflictPayload): Observable<void>` → `POST /api/conflicts/{conflictId}/resolve`

3. **Implement `ConflictHighlightDirective`** — `@Directive({ selector: '[appConflictHighlight]' })`: receives `@Input() severity: 'Critical' | 'Warning' | null`. When severity is set, applies corresponding CSS class (`conflict-critical` / `conflict-warning`) to the host element. When severity is `null` (no conflict), applies no class.

4. **Implement `ConflictCardComponent`** — Standalone component:
   - `@Input() conflict: DataConflictDto`
   - Renders two columns: left (value1 + sourceDoc1 name), right (value2 + sourceDoc2 name)
   - Severity badge: `mat-chip` with `color="warn"` for Critical, `color="accent"` for Warning
   - Embeds `ConflictResolutionFormComponent`; on form submit emits `resolved` event with `{ conflictId, resolvedValue }`

5. **Implement `ConflictResolutionFormComponent`** — Reactive form with three mutually exclusive options via `mat-radio-group`:
   - Option A: "Use value from [sourceDoc1]" → `resolvedValue = value1`
   - Option B: "Use value from [sourceDoc2]" → `resolvedValue = value2`
   - Option C: "Enter custom value" → reveals a `mat-form-field` text input
   - Submit button disabled until one option is selected (and custom text is non-empty when Option C is selected)

6. **Implement `UnresolvedCriticalBlockerModalComponent`** — `MatDialog` content component:
   - Lists each unresolved Critical conflict: field name + both conflicting values
   - "Close" button dismisses dialog; no navigate-away action (staff must resolve in the view)

7. **Extend `Patient360ViewPageComponent`** — In `ngOnInit`, after loading 360-view data:
   - Populate `ClinicalConflictStore.conflicts` from the `conflicts[]` field in the API response (or call `ConflictService.getConflicts` separately if not co-located in the 360-view response)
   - Apply `appConflictHighlight` directive to each field card where the field name matches a conflict
   - Render `ConflictCardComponent` below each conflicted field card
   - Override "Verify Profile" button click: if `unresolvedCriticalCount > 0`, open `UnresolvedCriticalBlockerModalComponent` instead of invoking verify; proceed only when count is zero

8. **Add CSS utility classes** — In `clinical.styles.scss`:
   - `.conflict-critical` — `border: 2px solid #D32F2F; background-color: #FFEBEE;`
   - `.conflict-warning` — `border: 2px solid #F57C00; background-color: #FFF3E0;`

---

## Current Project State

```
app/
  clinical/
    pages/
      patient-360-view/
        patient-360-view.component.ts      ← existing page to extend
    components/
      clinical-field-card/
        clinical-field-card.component.ts   ← existing card; directive applied here
    store/
      clinical-360.store.ts                ← existing store to extend with canVerify signal
    services/
      clinical.service.ts                  ← existing service; add getConflicts/resolveConflict
    clinical.styles.scss                   ← existing styles to extend
    clinical.routes.ts                     ← existing routing (no change needed)
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `app/clinical/directives/conflict-highlight.directive.ts` | Directive: applies severity CSS class to host element |
| CREATE | `app/clinical/components/conflict-card/conflict-card.component.ts` | Side-by-side conflict display + resolution form host |
| CREATE | `app/clinical/components/conflict-resolution-form/conflict-resolution-form.component.ts` | Reactive form: choose value1/value2/custom → emit resolved value |
| CREATE | `app/clinical/components/unresolved-critical-modal/unresolved-critical-modal.component.ts` | MatDialog listing unresolved Critical conflict blockers |
| CREATE | `app/clinical/store/clinical-conflict.store.ts` | NgRx Signals store: conflicts, unresolvedCriticalCount, resolveConflict |
| CREATE | `app/clinical/services/conflict.service.ts` | Angular service: getConflicts, resolveConflict |
| MODIFY | `app/clinical/pages/patient-360-view/patient-360-view.component.ts` | Populate conflict store; render conflict cards; gate Verify Profile button |
| MODIFY | `app/clinical/store/clinical-360.store.ts` | Add `canVerify` computed signal from `unresolvedCriticalCount === 0` |
| MODIFY | `app/clinical/clinical.styles.scss` | Add `.conflict-critical` and `.conflict-warning` utility classes |

---

## External References

- [Angular 18 Signals — NgRx SignalStore](https://ngrx.io/guide/signals/signal-store) — `withState`, `withComputed`, `withMethods`
- [Angular Material 18 — Dialog, Chips, Radio Group](https://material.angular.io/components/categories) — `MatDialogModule`, `MatChipsModule`, `MatRadioModule`
- [Angular Reactive Forms (18)](https://angular.dev/guide/forms/reactive-forms) — Form group with conditional validation
- [Angular Directives (18)](https://angular.dev/guide/directives) — `@Directive`, `ElementRef`, `HostBinding`
- [FR-055 (spec.md)](../.propel/context/docs/spec.md) — Visual conflict highlighting with source identification
- [FR-056 (spec.md)](../.propel/context/docs/spec.md) — Mandatory conflict resolution before profile completion
- [NFR-012 (design.md)](../.propel/context/docs/design.md) — Frontend LCP < 2.5s; conflict cards must not block initial page render
- [UC-008 Sequence Diagram (models.md)](../.propel/context/docs/models.md) — Full 360-view + conflict resolution flow

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for Angular build and serve commands.

---

## Implementation Validation Strategy

- [ ] Unit tests pass (Jest / Angular Testing Library)
- [ ] Integration tests pass (page component → conflict service mock → store updates)
- [ ] **[UI Tasks]** Visual comparison against wireframe at 375px, 768px, 1440px (when wireframe becomes AVAILABLE)
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment (when wireframe becomes AVAILABLE)
- [ ] Critical conflict field cards render with red border and `conflict-critical` class
- [ ] Warning conflict field cards render with amber border and `conflict-warning` class
- [ ] Both conflicting values displayed side by side with source document names
- [ ] Resolution form: selecting option A/B populates `resolvedValue`; Option C enables custom text input
- [ ] Submit disabled until a resolution option is selected (and custom text non-empty for Option C)
- [ ] Successful resolution optimistically marks card as Resolved; reverts on API failure
- [ ] "Verify Profile" blocked by modal when `unresolvedCriticalCount > 0`; modal lists field names
- [ ] "Verify Profile" proceeds normally when all Critical conflicts are resolved

---

## Implementation Checklist

- [x] Create `ClinicalConflictStore`: `conflicts`, `unresolvedCriticalCount` computed, `resolveConflict` method
- [x] Create `ConflictService`: `getConflicts` and `resolveConflict` HTTP methods
- [x] Create `ConflictHighlightDirective`: severity input → CSS class on host element
- [x] Create `ConflictResolutionFormComponent`: radio group (value1/value2/custom) + conditional custom input + submit
- [x] Create `ConflictCardComponent`: severity badge, side-by-side values, embeds resolution form
- [x] Create `UnresolvedCriticalBlockerModalComponent`: MatDialog listing unresolved Critical conflicts
- [x] Extend `Patient360ViewPageComponent`: populate conflict store, apply directive, render cards, gate Verify button
- [x] Add `canVerify` computed signal to `clinical-360.store.ts`
- [x] Add `.conflict-critical` and `.conflict-warning` CSS classes to `clinical.styles.scss`
