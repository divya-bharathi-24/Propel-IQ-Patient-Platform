import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
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
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatCardModule } from '@angular/material/card';
import { DatePipe } from '@angular/common';
import { debounceTime } from 'rxjs';
import {
  IntakeConflictPayload,
  IntakeFormValue,
  IntakeMissingFieldsError,
} from './models/intake-edit-form.model';
import { IntakeService } from './services/intake.service';
import { IntakeStore } from './state/intake.state';
import { IntakeConflictModalComponent } from './components/intake-conflict-modal/intake-conflict-modal.component';

@Component({
  selector: 'app-intake-edit',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    DatePipe,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    IntakeConflictModalComponent,
  ],
  providers: [IntakeStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './intake-edit.component.html',
  styleUrl: './intake-edit.component.scss',
})
export class IntakeEditComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);
  private readonly intakeService = inject(IntakeService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);

  readonly store = inject(IntakeStore);

  /** Route param */
  readonly appointmentId = signal('');

  /** Whether the conflict modal is visible. */
  readonly showConflictModal = signal(false);

  form!: FormGroup;

  // ── Lifecycle ────────────────────────────────────────────────────────────

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('appointmentId') ?? '';
    this.appointmentId.set(id);

    this.buildForm();
    this.checkAiHandoff();
    this.loadData(id);
  }

  // ── Form construction ────────────────────────────────────────────────────

  private buildForm(): void {
    this.form = this.fb.group({
      demographics: this.fb.group({
        firstName: ['', [Validators.required, Validators.maxLength(100)]],
        lastName: ['', [Validators.required, Validators.maxLength(100)]],
        dateOfBirth: ['', Validators.required],
        biologicalSex: ['', Validators.required],
        phone: ['', [Validators.required, Validators.maxLength(20)]],
        street: ['', Validators.maxLength(200)],
        city: ['', Validators.maxLength(100)],
        state: ['', Validators.maxLength(100)],
        postalCode: ['', Validators.maxLength(20)],
        country: ['', Validators.maxLength(100)],
      }),
      medicalHistory: this.fb.array([]),
      symptoms: this.fb.array([]),
      medications: this.fb.array([]),
    });

    // Debounced autosave on any value change
    this.form.valueChanges
      .pipe(debounceTime(800), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.autosaveDraft());
  }

  // ── AI-mode handoff ──────────────────────────────────────────────────────

  /**
   * If the patient arrives via AI-assisted intake, the router state will
   * contain pre-collected `intakeData`. Hydrate the form from that data
   * immediately so no data is lost on mode switch.
   */
  private checkAiHandoff(): void {
    const state = this.router.getCurrentNavigation()?.extras?.state;
    if (state?.['intakeData']) {
      this.hydrateForm(state['intakeData'] as IntakeFormValue);
    }
  }

  // ── Data loading ─────────────────────────────────────────────────────────

  loadData(appointmentId: string): void {
    this.store.setLoading('loading');

    this.intakeService
      .getDraft(appointmentId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (draftResponse) => {
          if (draftResponse.exists && draftResponse.draftData) {
            this.hydrateForm(draftResponse.draftData);
            this.store.setLoading('success');
            this.snackBar.open(
              'Draft restored from your last session.',
              'Dismiss',
              { duration: 4000 },
            );
            return;
          }
          this.loadPersistedRecord(appointmentId);
        },
        error: () => {
          this.loadPersistedRecord(appointmentId);
        },
      });
  }

  private loadPersistedRecord(appointmentId: string): void {
    this.intakeService
      .getRecord(appointmentId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ record, eTag }) => {
          this.store.setETag(eTag || record.rowVersion);
          this.hydrateForm(record.data);
          this.store.setLoading('success');
        },
        error: () => {
          this.store.setLoading('error');
        },
      });
  }

  // ── Form hydration helpers ───────────────────────────────────────────────

  private hydrateForm(value: IntakeFormValue): void {
    this.form.patchValue({ demographics: value.demographics });
    this.rebuildArray('medicalHistory', value.medicalHistory, (item) =>
      this.fb.group({
        condition: [item.condition, Validators.required],
        diagnosedAt: [item.diagnosedAt ?? ''],
        notes: [item.notes ?? ''],
      }),
    );
    this.rebuildArray('symptoms', value.symptoms, (item) =>
      this.fb.group({
        name: [item.name, Validators.required],
        severity: [item.severity ?? ''],
        onsetDate: [item.onsetDate ?? ''],
      }),
    );
    this.rebuildArray('medications', value.medications, (item) =>
      this.fb.group({
        name: [item.name, Validators.required],
        dosage: [item.dosage ?? ''],
        frequency: [item.frequency ?? ''],
      }),
    );
  }

  private rebuildArray<T>(
    controlName: string,
    items: T[],
    buildGroup: (item: T) => FormGroup,
  ): void {
    const arr = this.form.get(controlName) as FormArray;
    arr.clear({ emitEvent: false });
    items.forEach((item) => arr.push(buildGroup(item), { emitEvent: false }));
  }

  // ── FormArray accessors ──────────────────────────────────────────────────

  get medicalHistoryArray(): FormArray {
    return this.form.get('medicalHistory') as FormArray;
  }

  get symptomsArray(): FormArray {
    return this.form.get('symptoms') as FormArray;
  }

  get medicationsArray(): FormArray {
    return this.form.get('medications') as FormArray;
  }

  // ── Array item management ─────────────────────────────────────────────────

  addMedicalHistoryItem(): void {
    this.medicalHistoryArray.push(
      this.fb.group({
        condition: ['', Validators.required],
        diagnosedAt: [''],
        notes: [''],
      }),
    );
  }

  removeMedicalHistoryItem(index: number): void {
    this.medicalHistoryArray.removeAt(index);
  }

  addSymptomItem(): void {
    this.symptomsArray.push(
      this.fb.group({
        name: ['', Validators.required],
        severity: [''],
        onsetDate: [''],
      }),
    );
  }

  removeSymptomItem(index: number): void {
    this.symptomsArray.removeAt(index);
  }

  addMedicationItem(): void {
    this.medicationsArray.push(
      this.fb.group({
        name: ['', Validators.required],
        dosage: [''],
        frequency: [''],
      }),
    );
  }

  removeMedicationItem(index: number): void {
    this.medicationsArray.removeAt(index);
  }

  // ── Autosave ─────────────────────────────────────────────────────────────

  private autosaveDraft(): void {
    const value = this.form.getRawValue() as IntakeFormValue;
    this.intakeService
      .saveDraft(this.appointmentId(), value)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.store.setDraftSavedAt(new Date().toISOString());
        },
        error: () => {
          // Autosave failures are silently swallowed — the form remains editable
        },
      });
  }

  // ── Save ─────────────────────────────────────────────────────────────────

  onSave(): void {
    if (this.store.savingState() === 'saving') {
      return;
    }

    this.store.clearMissingFields();
    this.form.markAllAsTouched();

    const value = this.form.getRawValue() as IntakeFormValue;
    const eTag = this.store.eTag();

    this.store.setSaving('saving');

    this.intakeService
      .saveRecord(this.appointmentId(), value, eTag)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.store.setSaving('success');
          if (response.headers.get('ETag')) {
            this.store.setETag(response.headers.get('ETag')!);
          }
          this.snackBar.open('Intake updated successfully.', 'Dismiss', {
            duration: 4000,
          });
          this.router.navigate(['/dashboard']);
        },
        error: (err) => {
          this.store.setSaving('error');
          this.handleSaveError(err, value);
        },
      });
  }

  private handleSaveError(
    err: { status: number; error: unknown },
    localVersion: IntakeFormValue,
  ): void {
    if (err?.status === 422) {
      // Partial save — highlight missing fields (AC-3)
      const body = err.error as IntakeMissingFieldsError;
      const missing = body?.missingFields ?? [];
      this.store.setMissingFields(missing);
      missing.forEach((field) => {
        const control = this.form.get(field);
        if (control) {
          control.setErrors({ required: true });
        }
      });
      return;
    }

    if (err?.status === 409) {
      // Concurrent conflict — open reconciliation modal
      const serverPayload = err.error as {
        data: IntakeFormValue;
        rowVersion: string;
      };
      const conflictPayload: IntakeConflictPayload = {
        serverVersion: serverPayload.data,
        serverRowVersion: serverPayload.rowVersion,
        localVersion,
      };
      this.store.setConflictPayload(conflictPayload);
      this.showConflictModal.set(true);
      return;
    }

    this.snackBar.open(
      'An error occurred while saving. Please try again.',
      'Dismiss',
      { duration: 5000 },
    );
  }

  // ── Conflict modal handlers ──────────────────────────────────────────────

  onConflictResolved(resolvedValue: IntakeFormValue): void {
    this.showConflictModal.set(false);

    const payload = this.store.conflictPayload();
    if (payload) {
      this.store.setETag(payload.serverRowVersion);
    }
    this.store.setConflictPayload(null);

    this.hydrateForm(resolvedValue);
    // Re-submit with the newly chosen values and updated ETag
    this.onSave();
  }

  onConflictCancelled(): void {
    this.showConflictModal.set(false);
    this.store.setConflictPayload(null);
  }

  // ── Navigation ────────────────────────────────────────────────────────────

  onCancel(): void {
    this.router.navigate(['/dashboard']);
  }

  // ── Error helper for template ─────────────────────────────────────────────

  hasError(controlPath: string, errorCode: string): boolean {
    const control: AbstractControl | null = this.form.get(controlPath);
    return !!control && control.touched && control.hasError(errorCode);
  }
}
