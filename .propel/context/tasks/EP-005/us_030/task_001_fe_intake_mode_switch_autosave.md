# Task — FE: Intake Mode Switch & Autosave Orchestrator

## Task Metadata

| Field            | Value                                                            |
| ---------------- | ---------------------------------------------------------------- |
| **Task ID**      | task_001                                                         |
| **Story**        | US_030 — Intake Mode Switch, Autosave & Resume                  |
| **Epic**         | EP-005 — Digital Patient Intake — AI Conversational & Manual    |
| **Layer**        | Frontend                                                         |
| **Priority**     | High                                                             |
| **Estimate**     | 6 hours                                                          |
| **Status**       | Completed                                                        |
| **Depends On**   | US_028 task_001 (AI chat component), US_029 task_001 (manual form component) |
| **Blocks**       | None                                                             |

---

## Objective

Implement the Angular 18 orchestration layer for the intake page that:

1. Manages the active intake **mode** (`AI` | `Manual`) in a shared NgRx Signal store.
2. Executes **mode-switch transitions** — AI → Manual (AC-1) and Manual → AI (AC-2) — without data loss, calling the BE resume endpoint for Manual → AI context injection.
3. Drives a **30-second autosave timer** that calls `POST /api/intake/{appointmentId}/draft` on any field mutation; shows a persistent "Saved" indicator.
4. Maintains a **localStorage fallback** (`intake_draft_{appointmentId}`) when the autosave network call fails; syncs the local draft to the server via `POST /api/intake/sync-local-draft` on reconnect.
5. On page load, detects an existing server draft or localStorage draft and restores fields automatically, displaying a "Resuming your saved intake" toast (AC-3).

---

## Checklist

- [X] **1 — `IntakeStateSignal` store** — Add signal fields: `mode: 'AI' | 'Manual'`, `draftFields: IntakeFieldMap`, `autosaveStatus: 'Idle' | 'Saving' | 'Saved' | 'Error'`, `hasDraft: boolean`; export typed `IntakeFieldMap` interface covering all four JSONB sections (demographics, medicalHistory, symptoms, medications).
- [X] **2 — `IntakePageComponent` scaffold** — Standalone Angular 18 component at route `intake/:appointmentId`; reads `IntakeStateSignal`; conditionally renders `<app-ai-intake-chat>` or `<app-manual-intake-form>` based on `mode` signal; includes "Switch to Manual Form" and "Switch to AI Mode" toggle buttons (hidden when already in that mode).
- [X] **3 — AI → Manual switch** (AC-1) — On "Switch to Manual Form" click: read all AI-extracted fields from `IntakeStateSignal.draftFields`; call `ManualIntakeFormComponent.patchValues(fields)`; update `mode` to `'Manual'`; no BE call required; no data loss.
- [X] **4 — Manual → AI switch** (AC-2) — On "Switch to AI Mode" click: collect current `draftFields` snapshot; call `IntakeService.resumeAiSession(appointmentId, draftFields)` → `POST /api/intake/session/resume`; on success set `mode` to `'AI'` and pass returned `nextQuestion` to `AiIntakeChatComponent.initWithContext()`; on HTTP error keep `mode` as `'Manual'` and show inline error toast.
- [X] **5 — Autosave timer** (AC-4) — `IntakeAutosaveService`: uses `Subject<IntakeFieldMap>` with `debounceTime(30_000)` + `switchMap` to call `IntakeService.saveOrchestratedDraft(appointmentId, fields)`; on success set `autosaveStatus = 'Saved'`, reset to `'Idle'` after 3 s; on error set `autosaveStatus = 'Error'` and call `LocalDraftService.save(appointmentId, fields, Date.now())`; cancel timer on component destroy.
- [X] **6 — LocalStorage fallback** (`LocalDraftService`) — Saves draft as `localStorage.setItem('intake_draft_{appointmentId}', JSON.stringify({fields, ts}))`; exposes `load(appointmentId)` and `clear(appointmentId)`; on reconnect (`online` event listener) calls `IntakeService.syncLocalDraft(appointmentId, localDraft)` → `POST /api/intake/sync-local-draft`; on 200 clears local draft; on 409 conflict displays conflict modal (keep server / keep local / merge).
- [X] **7 — Draft resume on load** (AC-3) — In `IntakePageComponent.ngOnInit`: call `IntakeService.getDraft(appointmentId)` → `GET /api/intake/{appointmentId}/draft`; if `200` with non-null `draftData` patch form and show `"Resuming your saved intake"` toast via `MatSnackBar`; if `404` check `LocalDraftService.load()` and apply local draft if present; `hasDraft` signal drives a `role="status"` live-region announcement for screen readers.
- [X] **8 — "Saved" indicator & accessibility** — Render `<span class="autosave-status" aria-live="polite">` bound to `autosaveStatus`; display `"Saved"` ✓ text for 3 s after each successful autosave, `"Saving…"` spinner during in-flight call, `"Save failed — data kept locally"` on error; `aria-live="polite"` ensures screen-reader announcement without interrupting focus.

---

## Acceptance Criteria Coverage

| AC | Covered By |
|----|------------|
| AC-1 — AI→Manual preserves fields | Checklist item 3 |
| AC-2 — Manual→AI with context summary | Checklist item 4 |
| AC-3 — Draft restored on return visit | Checklist item 7 |
| AC-4 — Autosave within 30 s + "Saved" indicator | Checklist items 5, 8 |
| Edge — Network failure → localStorage backup | Checklist item 6 |
| Edge — Most-recently-edited value wins on switch | Checklist item 1 (`draftFields` is single source of truth) |

---

## Technical Notes

- `IntakeAutosaveService` must be provided in `IntakePageComponent` providers array (not root) to scope the timer to the intake route lifetime.
- `debounceTime(30_000)` — debounce starts from *last* field change; if user keeps typing, the 30-second clock resets each keystroke, which satisfies "within 30 seconds of *any* modification".
- `LocalDraftService` stores raw `IntakeFieldMap`; avoid storing PHI in localStorage beyond session — document that localStorage is a transient fallback cleared immediately after successful sync.
- Mode-switch buttons must be `[disabled]="autosaveStatus === 'Saving'"` to prevent switching while an in-flight save could overwrite the transitioning state.
- `IntakeFieldMap` must match the four JSONB column shapes in `IntakeRecord` exactly so that `patchValues()` and server draft restoration map without transformation.
- Reuse existing `GET /api/intake/{appointmentId}/draft` and `POST /api/intake/{appointmentId}/draft` endpoints delivered in EP-002/US_017 task_002; do not create duplicate draft endpoints.

---

## Design References

| Field               | Value                                                                                                   |
| ------------------- | ------------------------------------------------------------------------------------------------------- |
| **Wireframe Status**| PENDING                                                                                                 |
| **Wireframe Path**  | `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-intake-mode-switch.[html\|png\|jpg]`               |
| **Screen ID(s)**    | N/A (figma_spec.md not yet generated)                                                                   |
| **Figma Spec**      | N/A                                                                                                     |
| **UX Notes**        | "Switch" buttons must be visually prominent; "Saved" indicator must not overlap form action buttons.    |

---

## Requirement References

| Requirement | Description                                           |
| ----------- | ----------------------------------------------------- |
| FR-018      | Seamless mode switch preserving all entered data       |
| FR-019      | Autosave; resume on next session; no duplicate record |
| NFR-013     | HIPAA — localStorage draft is transient; cleared on sync |

---

## AI References

| Field | Value |
|-------|-------|
| AI Involved | No |
| AIR Ref | N/A |
| Model | N/A |
| Prompt Path | N/A |
| Guardrails | N/A |

---

## UI Impact

- **UI Impact**: Yes
- **New Components**: `IntakePageComponent`, `IntakeAutosaveService`, `LocalDraftService`
- **Modified Components**: `IntakeStateSignal` (extend signal fields)
- **New Routes**: `intake/:appointmentId` (or extend existing route if already scaffolded in US_028)
