# Task - TASK_001

## Requirement Reference

- **User Story**: US_029 — Manual Intake Form with All Clinical Fields
- **Story Location**: `.propel/context/tasks/EP-005/us_029/us_029.md`
- **Acceptance Criteria**:
  - AC-1: Given I select "Manual Form" intake mode, When the form loads, Then all required fields are displayed across four sections: Demographics, Medical History, Current Symptoms, and Current Medications — with clear labels and required-field indicators.
  - AC-2: Given I switch from AI mode to manual mode, When the manual form loads, Then all fields already captured by the AI chat are pre-populated in the correct manual form fields without data loss.
  - AC-4: Given I submit the form with missing required fields, When validation runs, Then each missing or invalid field is highlighted with an inline error message, and submission is blocked until resolved.
- **Edge Cases**:
  - Long form scroll-to-error: A summary error banner at the top of the form lists all fields with errors as clickable anchor links (`<a href="#fieldId">`) that scroll to and focus each field.
  - Autosave on exit: `interval(30_000)` merged with field-blur events drives `POST /api/intake/autosave`; on form init, if a draft exists (`completedAt == null`), a "Resume draft" banner is shown with Resume / Start Fresh options.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | PENDING |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-manual-intake.[html\|png\|jpg]` or provide external URL |
| **Screen Spec** | N/A (figma_spec.md not yet generated) |
| **UXR Requirements** | N/A (figma_spec.md not yet generated) |
| **Design Tokens** | N/A (designsystem.md not yet generated) |

### **CRITICAL: Wireframe Implementation Requirement**

**Wireframe Status = PENDING:** When wireframe becomes available, implementation MUST:

- Match layout, spacing, typography, and section grouping from the wireframe
- Implement all states: Loading (skeleton), Form (default filled), Validation Error (inline + banner), Empty (all blank), Saved (autosave chip), Resume Draft (banner)
- Validate implementation against wireframe at breakpoints: 375px, 768px, 1440px
- Run `/analyze-ux` after implementation to verify pixel-perfect alignment

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Frontend | Angular | 18.x |
| Frontend State | NgRx Signals | 18.x |
| Backend | ASP.NET Core Web API | .net 10 |
| Database | PostgreSQL | 16+ |
| Library | Angular Router | 18.x |
| Library | Angular `HttpClient` + `interval()` (RxJS) | 18.x |
| AI/ML | N/A | N/A |
| Vector Store | N/A | N/A |
| AI Gateway | N/A | N/A |
| Mobile | N/A | N/A |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement the `ManualIntakeFormComponent` — an Angular 18 standalone, `ChangeDetectionStrategy.OnPush` patient-facing intake form rendered in four collapsible sections:

1. **Demographics** — full name, date of birth, gender, phone, address (street, city, postcode), emergency contact name and phone
2. **Medical History** — past diagnoses / conditions (multi-entry), known allergies (multi-entry), previous surgeries (multi-entry), family history (free-text)
3. **Current Symptoms** — presenting symptoms (multi-entry with severity selector: Mild / Moderate / Severe), symptom duration, onset date
4. **Current Medications** — medication name + dosage + frequency (multi-entry), over-the-counter supplements flag

**Pre-population from AI mode (AC-2):** On component init, `IntakeService.getForm(appointmentId)` returns `IntakeFormResponseDto` containing `manualDraft` (existing manual partial save) and `aiExtracted` (any AI-sourced IntakeRecord for the same appointment). The component merges: `manualDraft ?? aiExtracted ?? empty`. FE signals are patched from the merged data, enabling seamless mode switching without data loss (FR-018).

**Client-side validation (AC-4):** Each required field is backed by a `computed()` validity signal. A `hasSubmitAttempted` signal gates error display — errors only become visible after first submit attempt. Inline error messages appear below each invalid field. A summary error banner at the top (`role="alert"` + `aria-live="assertive"`) lists all invalid fields as clickable `<a>` anchor links, each scrolling to and focusing the corresponding `id`-tagged input.

**Autosave (edge case):** `merge(interval(30_000), blur$)` emits periodically and on field-blur; each emission triggers `switchMap(() => IntakeService.autosave(...))` guarded by a debounce to prevent rapid back-to-back saves. A `saveStatus` signal (`'idle' | 'saving' | 'saved' | 'error'`) drives a chip display ("Saving…" / "Saved ✓" / "Save failed"). `takeUntilDestroyed()` prevents memory leaks.

**Resume-draft (edge case):** On init, if `IntakeFormResponseDto.manualDraft != null` and `completedAt == null`, the component renders a `ResumeDraftBannerComponent` ("You have an incomplete form. Resume your draft or start fresh?"). "Resume" patches signals from draft; "Start Fresh" discards draft and triggers `DELETE /api/intake/draft?appointmentId={id}`.

**Route:** `/patient/intake/:appointmentId` with `authGuard` (Patients only); query parameter `?mode=manual` selects this component from the parent `IntakeHostComponent` (which also hosts the US_028 AI chat).

**WCAG 2.2 AA:** `aria-required="true"` on required inputs; `aria-describedby` links each input to its error message `<span>`; `aria-invalid="true"` on invalid fields after submit attempt; `role="alert"` on error banner.

## Dependent Tasks

- **US_029 / TASK_002** — `GET /api/intake/form`, `POST /api/intake/autosave`, `POST /api/intake/submit`, `DELETE /api/intake/draft` backend endpoints must be implemented before end-to-end integration.
- **US_028 / TASK_001** — `IntakeHostComponent` (mode selector) and `IntakeModeService` must exist; AI-extracted field data is read from `IntakeModeService` or from `GET /api/intake/form` response.
- **US_011 / TASK_001** — `AuthInterceptor` must attach Bearer token to all `HttpClient` calls.
- **US_007 (EP-DATA)** — `IntakeRecord` entity and `intake_records` table must exist; `completedAt` nullable column is the draft-vs-complete discriminator.

## Impacted Components

| Component | Status | Location |
|-----------|--------|----------|
| `ManualIntakeFormComponent` | NEW | `app/features/intake/manual-form/manual-intake-form.component.ts` |
| `IntakeSectionComponent` | NEW | `app/features/intake/manual-form/intake-section/intake-section.component.ts` |
| `ResumeDraftBannerComponent` | NEW | `app/features/intake/manual-form/resume-draft-banner/resume-draft-banner.component.ts` |
| `IntakeErrorSummaryComponent` | NEW | `app/features/intake/manual-form/intake-error-summary/intake-error-summary.component.ts` |
| `IntakeService` | NEW | `app/features/intake/intake.service.ts` |
| `IntakeModels` (TypeScript interfaces) | NEW | `app/features/intake/intake.models.ts` |
| `IntakeHostComponent` | MODIFY | Add `?mode=manual` routing branch; pass `appointmentId` route param to `ManualIntakeFormComponent` |
| `AppRoutingModule` | MODIFY | Add `/patient/intake/:appointmentId` route with `authGuard` and `title: 'Complete Intake'` |

## Implementation Plan

1. **TypeScript models** (`intake.models.ts`):

   ```typescript
   export interface DemographicsFields {
     fullName: string;
     dateOfBirth: string;          // ISO date
     gender: string;
     phone: string;
     addressStreet: string;
     addressCity: string;
     addressPostcode: string;
     emergencyContactName: string;
     emergencyContactPhone: string;
   }

   export interface MedicalHistoryFields {
     pastConditions: string[];
     allergies: string[];
     previousSurgeries: string[];
     familyHistory: string;
   }

   export interface SymptomsFields {
     symptoms: Array<{ name: string; severity: 'Mild' | 'Moderate' | 'Severe' }>;
     duration: string;
     onsetDate: string;             // ISO date
   }

   export interface MedicationsFields {
     medications: Array<{ name: string; dosage: string; frequency: string }>;
     includesSupplements: boolean;
   }

   export interface IntakeFormResponseDto {
     appointmentId: string;
     manualDraft: IntakeDraftData | null;
     aiExtracted: IntakeDraftData | null;
   }

   export interface IntakeDraftData {
     demographics: Partial<DemographicsFields>;
     medicalHistory: Partial<MedicalHistoryFields>;
     symptoms: Partial<SymptomsFields>;
     medications: Partial<MedicationsFields>;
   }

   export interface AutosaveIntakeRequest {
     appointmentId: string;
     demographics: Partial<DemographicsFields>;
     medicalHistory: Partial<MedicalHistoryFields>;
     symptoms: Partial<SymptomsFields>;
     medications: Partial<MedicationsFields>;
   }
   ```

2. **`IntakeService`**:

   ```typescript
   @Injectable({ providedIn: 'root' })
   export class IntakeService {
     private readonly http = inject(HttpClient);

     getForm(appointmentId: string): Observable<IntakeFormResponseDto> {
       return this.http.get<IntakeFormResponseDto>(`/api/intake/form`, {
         params: { appointmentId }
       });
     }

     autosave(request: AutosaveIntakeRequest): Observable<void> {
       return this.http.post<void>('/api/intake/autosave', request);
     }

     submitIntake(request: AutosaveIntakeRequest): Observable<void> {
       return this.http.post<void>('/api/intake/submit', request);
     }

     deleteDraft(appointmentId: string): Observable<void> {
       return this.http.delete<void>('/api/intake/draft', {
         params: { appointmentId }
       });
     }
   }
   ```

3. **`ManualIntakeFormComponent`** — signal-based form state:

   ```typescript
   @Component({
     standalone: true,
     selector: 'app-manual-intake-form',
     changeDetection: ChangeDetectionStrategy.OnPush,
     imports: [IntakeSectionComponent, IntakeErrorSummaryComponent, ResumeDraftBannerComponent, ...],
     template: `...`
   })
   export class ManualIntakeFormComponent {
     private readonly route = inject(ActivatedRoute);
     private readonly intakeService = inject(IntakeService);
     private readonly destroyRef = inject(DestroyRef);
     private readonly router = inject(Router);

     appointmentId = toSignal(this.route.paramMap.pipe(map(p => p.get('appointmentId')!)));

     // Form state signals
     demographics = signal<Partial<DemographicsFields>>({});
     medicalHistory = signal<Partial<MedicalHistoryFields>>({});
     symptoms = signal<Partial<SymptomsFields>>({});
     medications = signal<Partial<MedicationsFields>>({});

     // UI state signals
     isLoading = signal(true);
     hasDraft = signal(false);
     hasSubmitAttempted = signal(false);
     saveStatus = signal<'idle' | 'saving' | 'saved' | 'error'>('idle');
     submitError = signal<string | null>(null);

     // Validation: computed per-field errors (shown only after hasSubmitAttempted)
     demographicsErrors = computed(() =>
       this.hasSubmitAttempted()
         ? validateDemographics(this.demographics())
         : []
     );
     // ... medicalHistoryErrors, symptomsErrors, medicationsErrors computed similarly

     allErrors = computed(() => [
       ...this.demographicsErrors(),
       ...this.medicalHistoryErrors(),
       ...this.symptomsErrors(),
       ...this.medicationsErrors()
     ]);
     isFormValid = computed(() => this.allErrors().length === 0);

     private readonly blur$ = new Subject<void>();

     constructor() {
       // Initial load: fetch form data (draft + AI pre-populated)
       this.intakeService.getForm(this.appointmentId()!).pipe(
         takeUntilDestroyed(this.destroyRef)
       ).subscribe(response => {
         const data = response.manualDraft ?? response.aiExtracted;
         if (data) {
           this.demographics.set(data.demographics);
           this.medicalHistory.set(data.medicalHistory);
           this.symptoms.set(data.symptoms);
           this.medications.set(data.medications);
         }
         this.hasDraft.set(response.manualDraft != null);
         this.isLoading.set(false);
       });

       // Autosave pipeline
       merge(interval(30_000), this.blur$).pipe(
         debounceTime(500),
         switchMap(() => {
           this.saveStatus.set('saving');
           return this.intakeService.autosave(this.buildRequest()).pipe(
             catchError(() => {
               this.saveStatus.set('error');
               return EMPTY;
             })
           );
         }),
         takeUntilDestroyed(this.destroyRef)
       ).subscribe(() => {
         this.saveStatus.set('saved');
         // Reset chip to 'idle' after 3s
         timer(3000).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() =>
           this.saveStatus.set('idle')
         );
       });
     }

     onSubmit(): void {
       this.hasSubmitAttempted.set(true);
       if (!this.isFormValid()) return;      // AC-4: blocks submission
       this.intakeService.submitIntake(this.buildRequest()).pipe(
         takeUntilDestroyed(this.destroyRef)
       ).subscribe({
         next: () => this.router.navigate(['/patient/dashboard']),
         error: (err) => this.submitError.set(err?.error?.message ?? 'Submission failed. Please try again.')
       });
     }

     onResumeDiscarded(): void {
       // "Start Fresh" — clear signals and delete server draft
       this.demographics.set({});
       this.medicalHistory.set({});
       this.symptoms.set({});
       this.medications.set({});
       this.hasDraft.set(false);
       this.intakeService.deleteDraft(this.appointmentId()!).pipe(
         takeUntilDestroyed(this.destroyRef)
       ).subscribe();
     }

     onFieldBlur(): void { this.blur$.next(); }

     private buildRequest(): AutosaveIntakeRequest {
       return {
         appointmentId: this.appointmentId()!,
         demographics: this.demographics(),
         medicalHistory: this.medicalHistory(),
         symptoms: this.symptoms(),
         medications: this.medications()
       };
     }
   }
   ```

4. **Template** — `@if`/`@for`/`@empty` control flow:

   ```html
   @if (isLoading()) {
     <!-- skeleton placeholder -->
   } @else {
     @if (hasDraft()) {
       <app-resume-draft-banner
         (resume)="hasDraft.set(false)"
         (startFresh)="onResumeDiscarded()" />
     }

     @if (allErrors().length > 0 && hasSubmitAttempted()) {
       <app-intake-error-summary [errors]="allErrors()" role="alert" aria-live="assertive" />
     }

     <form (ngSubmit)="onSubmit()" novalidate>
       <app-intake-section title="Demographics" [errors]="demographicsErrors()">
         <!-- demographics fields: name, dob, gender, phone, address, emergency contact -->
         <!-- each input: [id]="fieldId" [attr.aria-required]="true"
              [attr.aria-invalid]="fieldHasError()" [attr.aria-describedby]="fieldId + '-error'"
              (blur)="onFieldBlur()" -->
       </app-intake-section>

       <app-intake-section title="Medical History" [errors]="medicalHistoryErrors()">
         <!-- past conditions, allergies, previous surgeries, family history -->
       </app-intake-section>

       <app-intake-section title="Current Symptoms" [errors]="symptomsErrors()">
         <!-- symptoms list with severity; duration; onset date -->
       </app-intake-section>

       <app-intake-section title="Current Medications" [errors]="medicationsErrors()">
         <!-- medications list: name, dosage, frequency; supplements flag -->
       </app-intake-section>

       <!-- Save status chip -->
       @if (saveStatus() === 'saving') { <span class="chip saving">Saving…</span> }
       @if (saveStatus() === 'saved') { <span class="chip saved">Saved ✓</span> }
       @if (saveStatus() === 'error') { <span class="chip error">Save failed</span> }

       <button type="submit" [disabled]="hasSubmitAttempted() && !isFormValid()">
         Submit Intake
       </button>
     </form>
   }
   ```

5. **Error summary anchor navigation** (`IntakeErrorSummaryComponent`):

   ```typescript
   // Each field registered with a unique [id] (e.g., "field-fullName")
   // Error summary renders: <a href="#field-fullName" (click)="scrollToField($event, 'field-fullName')">Full Name is required</a>
   scrollToField(event: MouseEvent, fieldId: string): void {
     event.preventDefault();
     const el = document.getElementById(fieldId);
     el?.scrollIntoView({ behavior: 'smooth', block: 'center' });
     el?.focus();
   }
   ```

6. **Route registration**:

   ```typescript
   {
     path: 'patient/intake/:appointmentId',
     component: IntakeHostComponent,
     canActivate: [authGuard],
     title: 'Complete Intake'
   }
   ```

   `IntakeHostComponent` reads `?mode` query param; routes to `ManualIntakeFormComponent` when `mode=manual` (or default after booking wizard step 2).

## Current Project State

```
app/
├── features/
│   ├── auth/             (US_011 — completed)
│   ├── booking/          (US_019 — completed)
│   ├── staff/            (US_026, US_027 — completed)
│   └── intake/           ← NEW (this task + US_028)
│       ├── intake.service.ts
│       ├── intake.models.ts
│       ├── intake-host/
│       │   └── intake-host.component.ts       ← MODIFY (add manual mode routing)
│       └── manual-form/
│           ├── manual-intake-form.component.ts  ← NEW
│           ├── intake-section/
│           │   └── intake-section.component.ts  ← NEW
│           ├── resume-draft-banner/
│           │   └── resume-draft-banner.component.ts ← NEW
│           └── intake-error-summary/
│               └── intake-error-summary.component.ts ← NEW
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `app/features/intake/intake.models.ts` | TypeScript interfaces: `DemographicsFields`, `MedicalHistoryFields`, `SymptomsFields`, `MedicationsFields`, `IntakeFormResponseDto`, `AutosaveIntakeRequest` |
| CREATE | `app/features/intake/intake.service.ts` | `IntakeService`: `getForm()`, `autosave()`, `submitIntake()`, `deleteDraft()` HTTP calls |
| CREATE | `app/features/intake/manual-form/manual-intake-form.component.ts` | Root form: 4-section layout, signals, `interval(30_000)` autosave pipeline, required-field validation, `hasSubmitAttempted` gate, pre-population from `aiExtracted` / `manualDraft` |
| CREATE | `app/features/intake/manual-form/intake-section/intake-section.component.ts` | Presentational section wrapper: title, collapsible, error count badge |
| CREATE | `app/features/intake/manual-form/resume-draft-banner/resume-draft-banner.component.ts` | Banner: "Resume draft" / "Start Fresh" outputs; shown when `hasDraft = true` |
| CREATE | `app/features/intake/manual-form/intake-error-summary/intake-error-summary.component.ts` | Error summary: `role="alert"`, clickable anchor links scrolling to each invalid field |
| MODIFY | `app/features/intake/intake-host/intake-host.component.ts` | Add `mode=manual` routing branch to render `ManualIntakeFormComponent` |
| MODIFY | `app/app.routes.ts` | Add `/patient/intake/:appointmentId` route with `authGuard` and `title: 'Complete Intake'` |

## External References

- [Angular Signals — `signal()`, `computed()`, `toSignal()`](https://angular.dev/guide/signals)
- [RxJS `merge()` + `interval()` + `debounceTime()` for autosave](https://rxjs.dev/api/index/function/merge)
- [Angular `takeUntilDestroyed()` memory leak prevention](https://angular.dev/api/core/rxjs-interop/takeUntilDestroyed)
- [WCAG 2.2 — 1.3.1 Info and Relationships (`aria-required`, `aria-describedby`, `aria-invalid`)](https://www.w3.org/TR/WCAG22/#info-and-relationships)
- [WCAG 2.2 — 3.3.1 Error Identification (`role="alert"`, `aria-live="assertive"`)](https://www.w3.org/TR/WCAG22/#error-identification)
- [FR-017 — Manual intake form fallback (spec.md#FR-017)](spec.md#FR-017)
- [FR-018 — Seamless mode switch preserving data (spec.md#FR-018)](spec.md#FR-018)
- [UC-003 — Patient Completes Manual Intake Form (models.md)](models.md#UC-003)

## Build Commands

- Refer to: `.propel/build/frontend-build.md`

## Implementation Validation Strategy

- [ ] Unit tests pass: `ManualIntakeFormComponent` renders all four sections with correct field labels
- [ ] Unit tests pass: `allErrors` computed signal returns errors for empty required fields after `hasSubmitAttempted = true`
- [ ] Unit tests pass: `submitIntake()` NOT called when `isFormValid() = false`
- [ ] Pre-population verified: form signals patched from `aiExtracted` when switching from AI mode (no data loss — AC-2)
- [ ] Autosave verified: `IntakeService.autosave()` called on `interval(30_000)` emission and on field blur
- [ ] Resume-draft banner shown when `manualDraft != null` on init
- [ ] "Start Fresh" calls `deleteDraft()` and clears all form signals
- [ ] Error summary anchor links scroll to and focus the correct input elements
- [ ] **[UI Tasks]** Visual comparison against wireframe at 375px, 768px, 1440px (when wireframe is available)
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment (when wireframe is available)

## Implementation Checklist

- [ ] Create `ManualIntakeFormComponent` (standalone, `OnPush`): 4-section form with signal-based state for `demographics`, `medicalHistory`, `symptoms`, `medications`; `isLoading` set only on initial `getForm()` call; on init merge `manualDraft ?? aiExtracted` into signals for pre-population (AC-1, AC-2, FR-018)
- [ ] `hasSubmitAttempted` signal gates error display; `computed()` validity signals per section produce error arrays; `allErrors = computed(...)` aggregates; `onSubmit()` sets `hasSubmitAttempted(true)` then guards with `if (!isFormValid()) return` before calling `submitIntake()` (AC-4)
- [ ] Autosave pipeline: `merge(interval(30_000), blur$).pipe(debounceTime(500), switchMap(() => intakeService.autosave(...)), takeUntilDestroyed())`; `saveStatus` signal drives "Saving…" / "Saved ✓" chip; error caught silently (EMPTY) to avoid blocking UX
- [ ] `ResumeDraftBannerComponent`: rendered when `hasDraft()` is true; `(resume)` output hides banner; `(startFresh)` output clears all signals + calls `deleteDraft(appointmentId)` + sets `hasDraft(false)` (edge case — draft restore)
- [ ] `IntakeErrorSummaryComponent`: `role="alert"` + `aria-live="assertive"`; renders one `<a href="#field-{id}">` per error; `scrollToField()` scrolls + focuses target input via `document.getElementById(fieldId)`; shown only when `allErrors().length > 0 && hasSubmitAttempted()` (edge case — error navigation)
- [ ] Each section input: `[id]="fieldId"`, `aria-required="true"`, `[attr.aria-invalid]="fieldHasError()"`, `[attr.aria-describedby]="fieldId + '-error'"`, `(blur)="onFieldBlur()"` (WCAG 2.2 AA — 1.3.1, 3.3.1)
