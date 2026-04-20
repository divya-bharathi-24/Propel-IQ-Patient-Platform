# Task - task_001_fe_ai_intake_chat_ui

## Requirement Reference

- **User Story:** us_028 — AI Conversational Intake Chat Interface
- **Story Location:** `.propel/context/tasks/EP-005/us_028/us_028.md`
- **Acceptance Criteria:**
  - AC-1: Selecting "AI-Assisted" intake mode starts the chat session; an opening contextual question is displayed; a live form preview panel shows the fields to be auto-populated
  - AC-2: Patient types a free-text response; extracted fields appear in the live preview within 5 seconds; the next AI question is rendered in the chat pane
  - AC-3: Fields extracted with confidence below 80% are highlighted with a visual indicator; the AI generates a clarifying follow-up for those fields
  - AC-4: Patient reviews and confirms auto-populated fields; submitting saves the `IntakeRecord` (`source = AI`); success navigates to booking confirmation
- **Edge Cases:**
  - OpenAI API unavailable: HTTP 503 or `isFallback: true` in response → `IntakeChatStore.activateFallbackMode()` → preserve draft extracted fields in sessionStorage → navigate to manual intake form route with fields pre-populated
  - Too short / insufficient response: AI returns clarification question without populating fields; store does not update `extractedFields` for that turn

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value |
| ---------------------- | ----- |
| **UI Impact**          | Yes   |
| **Figma URL**          | N/A   |
| **Wireframe Status**   | PENDING |
| **Wireframe Type**     | N/A   |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-AI-INTAKE-chat.[html\|png\|jpg]` or provide external URL |
| **Screen Spec**        | N/A (figma_spec.md not yet generated) |
| **UXR Requirements**   | N/A (figma_spec.md not yet generated) |
| **Design Tokens**      | N/A (designsystem.md not yet generated) |

---

## Applicable Technology Stack

| Layer              | Technology            | Version |
| ------------------ | --------------------- | ------- |
| Frontend           | Angular               | 18.x    |
| Frontend State     | NgRx Signals          | 18.x    |
| Frontend UI        | Angular Material      | 18.x    |
| Frontend Routing   | Angular Router        | 18.x    |
| HTTP Client        | Angular HttpClient    | 18.x    |
| Testing — Unit     | Jest / Angular Testing Library | — |
| AI/ML              | N/A (consumed via BE API) | N/A |
| Mobile             | N/A                   | N/A     |

> All code and libraries MUST be compatible with versions above.

---

## AI References (AI Tasks Only)

| Reference Type           | Value |
| ------------------------ | ----- |
| **AI Impact**            | No (FE consumes AI-processed responses via BE API only) |
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

Implement the Angular 18 AI-Assisted Intake Chat Interface — a two-panel page that lets a patient complete intake through a conversational AI chat on the left panel, while a live form preview updates on the right panel as fields are extracted.

**Key components:**
- `AiIntakeChatComponent` — host page (two-panel layout); conditionally shown when the patient selects "AI-Assisted" mode from the intake mode step
- `IntakeChatStore` (NgRx Signals) — reactive signals for `messages`, `extractedFields`, `confidenceMap`, `chatMode` (`'ai' | 'fallback_manual'`), `isSubmitting`, `sessionId`
- `IntakePreviewPanelComponent` — renders extracted field values; fields with `confidence < 0.8` flagged with an amber `mat-icon warning` indicator + tooltip showing confidence %
- `AiIntakeService` — HTTP client wrapping three BE endpoints: session start, message send, submit
- Circuit-breaker fallback: on `isFallback: true` or HTTP 503 → store switches to `fallback_manual`, draft fields persisted to sessionStorage, router navigates to manual intake route with pre-populated state
- Confirmation step embedded in the chat flow: rendered after the AI declares all fields collected; patient edits inline and submits

---

## Dependent Tasks

- **EP-005/us_028 task_002_be_ai_intake_api** — `POST /api/intake/ai/session`, `POST /api/intake/ai/message`, `POST /api/intake/ai/submit` must be available
- **US_007 (Foundational)** — `IntakeRecord` entity with JSONB columns must exist before submit can persist
- **US_011 (EP-001)** — JWT access token must be attached to requests (`PatientId` from token, not body)

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `AiIntakeChatComponent` (standalone, lazy-loaded in `PatientModule`) | `client/src/app/features/patient/intake/ai-intake-chat/` |
| CREATE | `IntakePreviewPanelComponent` (standalone child) | `client/src/app/features/patient/intake/intake-preview-panel/` |
| CREATE | `IntakeChatStore` (NgRx Signals store) | `client/src/app/features/patient/intake/` |
| CREATE | `AiIntakeService` (Angular `Injectable`, `providedIn: 'root'`) | `client/src/app/core/services/ai-intake.service.ts` |
| MODIFY | `PatientModule` routing — add `/intake/ai` route → `AiIntakeChatComponent` | `client/src/app/features/patient/patient.routes.ts` |
| MODIFY | Intake mode selection step — add "AI-Assisted" option linking to `/intake/ai` | existing booking wizard or intake entry component |

---

## Implementation Plan

1. **`IntakeChatStore`** (NgRx Signals):
   - `sessionId: Signal<string | null>`
   - `messages: Signal<ChatMessage[]>` — `{ role: 'user' | 'assistant', content: string, timestamp: Date }`
   - `extractedFields: Signal<ExtractedField[]>` — `{ fieldName: string, value: string, confidence: number, needsClarification: boolean }`
   - `confidenceMap: Signal<Record<string, number>>` — keyed by `fieldName`
   - `chatMode: Signal<'ai' | 'fallback_manual'>` — starts as `'ai'`; switches on fallback event
   - `isSubmitting: Signal<boolean>`
   - `activateFallbackMode()` method: sets `chatMode = 'fallback_manual'`, writes `extractedFields` to `sessionStorage('intake_draft')`, emits router navigation event

2. **`AiIntakeService`** (HttpClient wrapper):
   - `startSession(appointmentId: string): Observable<{sessionId: string}>` → `POST /api/intake/ai/session`
   - `sendMessage(sessionId: string, userMessage: string): Observable<AiTurnResponse>` → `POST /api/intake/ai/message`
   - `submitIntake(sessionId: string, confirmedFields: ConfirmedIntakeFields): Observable<{intakeRecordId: string}>` → `POST /api/intake/ai/submit`
   - `AiTurnResponse`: `{ aiResponse: string, extractedFields: ExtractedField[], isFallback: boolean }`
   - Error handler: HTTP 503 or `isFallback: true` → call `store.activateFallbackMode()` (OWASP A05 — never expose raw server error detail to UI)

3. **`AiIntakeChatComponent`** lifecycle:
   - `ngOnInit`: call `startSession(appointmentId)` from query params → store `sessionId`; dispatch first AI message (opening question from response) into `messages`
   - Chat input: text area + send button; on submit → `sendMessage(sessionId, userText)` → append user turn to `messages`, then append AI response turn; update `extractedFields` signal from response
   - `chatMode` effect: when switches to `'fallback_manual'` → `router.navigate(['/intake/manual'], { state: { prefill: store.extractedFields() } })`
   - Scrolls chat container to latest message on each new message (using `ViewChild` + `nativeElement.scrollTop`)

4. **`IntakePreviewPanelComponent`**:
   - Subscribes to `store.extractedFields` signal
   - For each field: renders label + value; if `confidence < 0.8` → renders `<mat-icon color="warn">warning</mat-icon>` + `matTooltip="AI confidence: {n}% — awaiting clarification"`
   - Groups fields into four sections: Demographics, Medical History, Symptoms, Medications
   - During confirmation step: makes each field's value editable via `mat-form-field` inline edit

5. **Confirmation step** (rendered after AI messages include session-complete signal):
   - Shows full `IntakePreviewPanelComponent` in edit mode
   - "Confirm & Submit" button: sets `isSubmitting = true`, calls `submitIntake(sessionId, confirmedFields)`, navigates to booking confirmation on success, shows `mat-snack-bar` error on failure

6. **Accessibility**:
   - Chat messages container: `role="log"` + `aria-live="polite"` (AC-2 — new AI questions announced to screen readers)
   - Low-confidence warning icons: `aria-label="Low confidence – awaiting clarification"` (WCAG 2.2 AA)
   - Send button: `aria-label="Send message"` with disabled state during pending response

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no client/ scaffold yet — greenfield Angular 18 project)
```

> Update with actual `client/src/` tree after scaffold is complete.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `client/src/app/features/patient/intake/ai-intake-chat/ai-intake-chat.component.ts` | Host two-panel AI chat component; session lifecycle, message send/receive, fallback routing |
| CREATE | `client/src/app/features/patient/intake/intake-preview-panel/intake-preview-panel.component.ts` | Live form preview; confidence flagging; confirmation edit mode |
| CREATE | `client/src/app/features/patient/intake/intake-chat.store.ts` | NgRx Signals store: messages, extractedFields, confidenceMap, chatMode, isSubmitting |
| CREATE | `client/src/app/core/services/ai-intake.service.ts` | HTTP wrapper: startSession, sendMessage, submitIntake; fallback detection |
| MODIFY | `client/src/app/features/patient/patient.routes.ts` | Add `/intake/ai` lazy route → `AiIntakeChatComponent` |

---

## External References

- [Angular 18 Signals — NgRx Signal Store](https://ngrx.io/guide/signals/signal-store)
- [Angular 18 HttpClient — error handling](https://angular.dev/guide/http/making-requests#handling-request-failure)
- [Angular Material 18 — MatTooltip](https://material.angular.io/components/tooltip/overview)
- [Angular Material 18 — MatSnackBar](https://material.angular.io/components/snack-bar/overview)
- [ARIA live regions — WAI-ARIA 1.2 role="log"](https://www.w3.org/TR/wai-aria-1.2/#log)
- [WCAG 2.2 AA — Success Criterion 4.1.3 Status Messages](https://www.w3.org/TR/WCAG22/#status-messages)
- [Angular Router — navigation state (router.navigate with state)](https://angular.dev/guide/routing/common-router-tasks#passing-state)

---

## Build Commands

```bash
# Install dependencies
npm install

# Serve Angular development server
ng serve

# Build for production
ng build --configuration production

# Run Angular unit tests
ng test
```

---

## Implementation Validation Strategy

- [ ] `/intake/ai` route requires Patient-role JWT — accessing without auth redirects to login
- [ ] Chat session starts: opening question from AI appears in chat pane; live preview panel shows empty field groups
- [ ] Sending a substantive patient response: extracted fields appear in the preview within 5 seconds (AC-2)
- [ ] Low-confidence field (< 80%): amber warning icon shown in preview panel; AI follow-up question rendered in chat
- [ ] HTTP 503 from BE: store switches to `fallback_manual`; router navigates to `/intake/manual`; pre-populated fields present in manual form state
- [ ] Confirmation step: all extracted fields editable; submitting calls `/api/intake/ai/submit`; success navigates to booking confirmation
- [ ] Screen reader test: new AI messages announced via `aria-live="polite"`

---

## Implementation Checklist

- [ ] Create `IntakeChatStore` (NgRx Signals): `messages`, `extractedFields`, `confidenceMap`, `chatMode`, `isSubmitting` signals; `activateFallbackMode()` persists draft to `sessionStorage`
- [ ] Create `AiIntakeService`: `startSession`, `sendMessage`, `submitIntake` HTTP calls; HTTP 503 / `isFallback: true` triggers `activateFallbackMode()`
- [ ] Create `AiIntakeChatComponent`: `ngOnInit` starts session; chat input submits via `sendMessage`; `chatMode` effect navigates to `/intake/manual` on fallback with router state pre-fill
- [ ] Create `IntakePreviewPanelComponent`: render fields grouped by category; `confidence < 0.8` → amber warning icon + matTooltip; editable in confirmation mode
- [ ] Implement confirmation step inside `AiIntakeChatComponent`: displayed when AI signals completion; editable preview + "Confirm & Submit" button calling `submitIntake`
- [ ] Add ARIA `role="log"` + `aria-live="polite"` to chat messages container; add `aria-label` to warning icons and send button
- [ ] Register `/intake/ai` lazy route in `patient.routes.ts`; guard with Patient-role `AuthGuard`
- [ ] Verify sessionStorage draft cleared on successful `submitIntake` to prevent stale pre-fill on next intake
