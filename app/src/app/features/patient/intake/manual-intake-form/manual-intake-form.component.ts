import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormArray,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, interval, merge } from 'rxjs';
import { debounceTime, switchMap } from 'rxjs/operators';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import {
  AutosaveStatus,
  ManualIntakeFormValue,
} from '../models/intake-edit-form.model';
import { IntakeService } from '../services/intake.service';
import { ResumeDraftBannerComponent } from '../resume-draft-banner/resume-draft-banner.component';

/** Describes a form field that failed validation — used for the error banner. */
interface InvalidFieldSummary {
  label: string;
  fieldId: string;
}

@Component({
  selector: 'app-manual-intake-form',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatExpansionModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatCheckboxModule,
    MatProgressSpinnerModule,
    MatIconModule,
    MatChipsModule,
    ResumeDraftBannerComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './manual-intake-form.component.html',
  styleUrl: './manual-intake-form.component.scss',
})
export class ManualIntakeFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);
  private readonly intakeService = inject(IntakeService);
  private readonly destroyRef = inject(DestroyRef);

  // ── State signals ─────────────────────────────────────────────────────────

  /** Route parameter :appointmentId */
  readonly appointmentId = signal('');

  /** Controls skeleton / error visibility while data loads. */
  readonly loadState = signal<'idle' | 'loading' | 'success' | 'error'>('idle');

  /** Status of the periodic autosave operation. */
  readonly saveStatus = signal<AutosaveStatus>('idle');

  /** Gates inline error display — errors only surface after the first submit attempt. */
  readonly hasSubmitAttempted = signal(false);

  /** Whether to display the resume-draft banner. */
  readonly showResumeDraft = signal(false);

  /** Stashed draft data for the "Resume" action. */
  private draftData: ManualIntakeFormValue | null = null;

  // ── Form ──────────────────────────────────────────────────────────────────

  form!: FormGroup;

  /** Subject that fires on any field blur — merged with interval for autosave. */
  private readonly blur$ = new Subject<void>();

  // ── Computed validity signals (AC-4) ──────────────────────────────────────

  /**
   * Returns an ordered list of fields that are required but empty, so the
   * error banner can render clickable anchor links to each invalid field.
   *
   * Re-evaluated whenever `hasSubmitAttempted` or form validity changes.
   * The signal is recomputed reactively because it reads `hasSubmitAttempted()`.
   */
  readonly invalidFields = computed<InvalidFieldSummary[]>(() => {
    if (!this.hasSubmitAttempted() || !this.form) {
      return [];
    }
    const results: InvalidFieldSummary[] = [];
    this.collectInvalidFields(results);
    return results;
  });

  /** True when there are no invalid required fields. Drives submit button state. */
  readonly isFormValid = computed(() => this.invalidFields().length === 0);

  // ── Lifecycle ─────────────────────────────────────────────────────────────

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('appointmentId') ?? '';
    this.appointmentId.set(id);

    this.buildForm();
    this.initAutosave(id);
    this.loadFormData(id);
  }

  // ── Form construction ─────────────────────────────────────────────────────

  private buildForm(): void {
    this.form = this.fb.group({
      demographics: this.fb.group({
        firstName: ['', [Validators.required, Validators.maxLength(100)]],
        lastName: ['', [Validators.required, Validators.maxLength(100)]],
        dateOfBirth: ['', Validators.required],
        gender: ['', Validators.required],
        phone: ['', [Validators.required, Validators.maxLength(30)]],
        street: ['', Validators.maxLength(200)],
        city: ['', Validators.maxLength(100)],
        postalCode: ['', Validators.maxLength(20)],
        emergencyContactName: ['', Validators.maxLength(100)],
        emergencyContactPhone: ['', Validators.maxLength(30)],
      }),
      medicalHistory: this.fb.group({
        conditions: this.fb.array([]),
        allergies: this.fb.array([]),
        surgeries: this.fb.array([]),
        familyHistory: ['', Validators.maxLength(2000)],
      }),
      symptoms: this.fb.array([]),
      medications: this.fb.array([]),
    });
  }

  // ── Data loading ──────────────────────────────────────────────────────────

  loadFormData(appointmentId: string): void {
    if (!appointmentId) {
      return;
    }

    this.loadState.set('loading');

    this.intakeService
      .getForm(appointmentId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          const hasDraft =
            response.manualDraft !== null &&
            response.manualDraft.completedAt === null;

          if (hasDraft && response.manualDraft) {
            // Store draft; show resume banner — do not auto-patch the form yet
            this.draftData = response.manualDraft.data;
            this.showResumeDraft.set(true);
          } else if (response.aiExtracted) {
            // Pre-populate from AI-extracted data (AC-2 — mode-switch handoff)
            this.hydrateForm(response.aiExtracted);
          }

          this.loadState.set('success');
        },
        error: () => {
          this.loadState.set('error');
        },
      });
  }

  // ── Resume-draft banner actions ───────────────────────────────────────────

  onResumeDraft(): void {
    if (this.draftData) {
      this.hydrateForm(this.draftData);
    }
    this.showResumeDraft.set(false);
  }

  onStartFresh(): void {
    this.showResumeDraft.set(false);
    this.draftData = null;

    this.intakeService
      .deleteDraft(this.appointmentId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe();
  }

  // ── Mode-switch integration (US_030) ─────────────────────────────────────

  /**
   * Publicly exposes form hydration for the IntakePageComponent orchestrator.
   * Called by the parent on AI → Manual mode switch (AC-1) with the
   * draftFields snapshot from IntakeModeStore.
   */
  patchValues(fields: ManualIntakeFormValue): void {
    this.hydrateForm(fields);
  }

  // ── Form hydration ────────────────────────────────────────────────────────

  private hydrateForm(value: ManualIntakeFormValue): void {
    this.form.patchValue({
      demographics: value.demographics,
      medicalHistory: { familyHistory: value.medicalHistory.familyHistory },
    });

    this.rebuildArray(
      this.conditionsArray,
      value.medicalHistory.conditions,
      (item) =>
        this.fb.group({
          condition: [item.condition, Validators.required],
          diagnosedAt: [item.diagnosedAt ?? ''],
          notes: [item.notes ?? ''],
        }),
    );

    this.rebuildArray(
      this.allergiesArray,
      value.medicalHistory.allergies,
      (item) =>
        this.fb.group({
          substance: [item.substance, Validators.required],
          reaction: [item.reaction ?? ''],
        }),
    );

    this.rebuildArray(
      this.surgeriesArray,
      value.medicalHistory.surgeries,
      (item) =>
        this.fb.group({
          procedure: [item.procedure, Validators.required],
          year: [item.year ?? ''],
          notes: [item.notes ?? ''],
        }),
    );

    this.rebuildArray(this.symptomsArray, value.symptoms, (item) =>
      this.fb.group({
        name: [item.name, Validators.required],
        severity: [item.severity ?? ''],
        onsetDate: [item.onsetDate ?? ''],
        duration: [item.duration ?? ''],
      }),
    );

    this.rebuildArray(this.medicationsArray, value.medications, (item) =>
      this.fb.group({
        name: [item.name, Validators.required],
        dosage: [item.dosage ?? ''],
        frequency: [item.frequency ?? ''],
        isOtcSupplement: [item.isOtcSupplement ?? false],
      }),
    );
  }

  private rebuildArray<T>(
    arr: FormArray,
    items: T[],
    buildGroup: (item: T) => FormGroup,
  ): void {
    arr.clear({ emitEvent: false });
    items.forEach((item) => arr.push(buildGroup(item), { emitEvent: false }));
  }

  // ── FormArray accessors ───────────────────────────────────────────────────

  get conditionsArray(): FormArray {
    return this.form.get('medicalHistory.conditions') as FormArray;
  }

  get allergiesArray(): FormArray {
    return this.form.get('medicalHistory.allergies') as FormArray;
  }

  get surgeriesArray(): FormArray {
    return this.form.get('medicalHistory.surgeries') as FormArray;
  }

  get symptomsArray(): FormArray {
    return this.form.get('symptoms') as FormArray;
  }

  get medicationsArray(): FormArray {
    return this.form.get('medications') as FormArray;
  }

  // ── Array item management ─────────────────────────────────────────────────

  addCondition(): void {
    this.conditionsArray.push(
      this.fb.group({
        condition: ['', Validators.required],
        diagnosedAt: [''],
        notes: [''],
      }),
    );
  }

  removeCondition(index: number): void {
    this.conditionsArray.removeAt(index);
  }

  addAllergy(): void {
    this.allergiesArray.push(
      this.fb.group({ substance: ['', Validators.required], reaction: [''] }),
    );
  }

  removeAllergy(index: number): void {
    this.allergiesArray.removeAt(index);
  }

  addSurgery(): void {
    this.surgeriesArray.push(
      this.fb.group({
        procedure: ['', Validators.required],
        year: [''],
        notes: [''],
      }),
    );
  }

  removeSurgery(index: number): void {
    this.surgeriesArray.removeAt(index);
  }

  addSymptom(): void {
    this.symptomsArray.push(
      this.fb.group({
        name: ['', Validators.required],
        severity: [''],
        onsetDate: [''],
        duration: [''],
      }),
    );
  }

  removeSymptom(index: number): void {
    this.symptomsArray.removeAt(index);
  }

  addMedication(): void {
    this.medicationsArray.push(
      this.fb.group({
        name: ['', Validators.required],
        dosage: [''],
        frequency: [''],
        isOtcSupplement: [false],
      }),
    );
  }

  removeMedication(index: number): void {
    this.medicationsArray.removeAt(index);
  }

  // ── Autosave ──────────────────────────────────────────────────────────────

  private initAutosave(appointmentId: string): void {
    merge(interval(30_000), this.blur$)
      .pipe(
        debounceTime(500),
        switchMap(() => {
          this.saveStatus.set('saving');
          const data = this.buildFormValue();
          return this.intakeService.autosave(appointmentId, data);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.saveStatus.set('saved');
        },
        error: () => {
          this.saveStatus.set('error');
        },
      });
  }

  /** Called from (blur) event bindings in the template. */
  onFieldBlur(): void {
    this.blur$.next();
  }

  // ── Validation helpers ────────────────────────────────────────────────────

  /**
   * Returns true when the control at `path` has the given error AND either
   * the control is touched or the user has attempted a submit.
   */
  hasError(path: string, error: string): boolean {
    const control = this.form.get(path);
    if (!control) {
      return false;
    }
    return (
      control.hasError(error) && (control.touched || this.hasSubmitAttempted())
    );
  }

  /**
   * Returns true when the control in the given FormArray at `index` has the
   * given error and the submit has been attempted.
   */
  hasArrayError(
    arr: FormArray,
    index: number,
    controlName: string,
    error: string,
  ): boolean {
    const control = arr.at(index)?.get(controlName);
    if (!control) {
      return false;
    }
    return (
      control.hasError(error) && (control.touched || this.hasSubmitAttempted())
    );
  }

  // ── Submit ────────────────────────────────────────────────────────────────

  onSubmit(): void {
    this.hasSubmitAttempted.set(true);
    this.form.markAllAsTouched();

    if (!this.form.valid) {
      // Scroll to the first invalid field via the error banner anchor links
      return;
    }

    const data = this.buildFormValue();
    this.saveStatus.set('saving');

    this.intakeService
      .submitManualIntake(this.appointmentId(), data)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.saveStatus.set('saved');
          this.router.navigate(['/dashboard']);
        },
        error: () => {
          this.saveStatus.set('error');
        },
      });
  }

  // ── Private helpers ───────────────────────────────────────────────────────

  private buildFormValue(): ManualIntakeFormValue {
    return this.form.getRawValue() as ManualIntakeFormValue;
  }

  /**
   * Walks the form tree and collects human-readable labels with element IDs
   * for every invalid required control. Used to populate the error banner.
   */
  private collectInvalidFields(results: InvalidFieldSummary[]): void {
    const demo = this.form.get('demographics');
    const requiredDemoFields: { key: string; label: string; id: string }[] = [
      { key: 'firstName', label: 'First Name', id: 'firstName' },
      { key: 'lastName', label: 'Last Name', id: 'lastName' },
      { key: 'dateOfBirth', label: 'Date of Birth', id: 'dateOfBirth' },
      { key: 'gender', label: 'Gender', id: 'gender' },
      { key: 'phone', label: 'Phone', id: 'phone' },
    ];

    for (const field of requiredDemoFields) {
      const ctrl = demo?.get(field.key) as AbstractControl | null;
      if (ctrl?.invalid) {
        results.push({ label: field.label, fieldId: field.id });
      }
    }

    this.conditionsArray.controls.forEach((group, i) => {
      if (group.get('condition')?.invalid) {
        results.push({
          label: `Condition ${i + 1}`,
          fieldId: `condition-${i}`,
        });
      }
    });

    this.allergiesArray.controls.forEach((group, i) => {
      if (group.get('substance')?.invalid) {
        results.push({ label: `Allergy ${i + 1}`, fieldId: `allergy-${i}` });
      }
    });

    this.surgeriesArray.controls.forEach((group, i) => {
      if (group.get('procedure')?.invalid) {
        results.push({ label: `Surgery ${i + 1}`, fieldId: `surgery-${i}` });
      }
    });

    this.symptomsArray.controls.forEach((group, i) => {
      if (group.get('name')?.invalid) {
        results.push({ label: `Symptom ${i + 1}`, fieldId: `symptom-${i}` });
      }
    });

    this.medicationsArray.controls.forEach((group, i) => {
      if (group.get('name')?.invalid) {
        results.push({
          label: `Medication ${i + 1}`,
          fieldId: `medication-${i}`,
        });
      }
    });
  }
}
