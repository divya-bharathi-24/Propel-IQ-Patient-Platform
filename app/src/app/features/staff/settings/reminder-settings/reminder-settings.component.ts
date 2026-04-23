import { Component, OnInit, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormArray,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import {
  ReminderSettingsService,
  ReminderSettingsServiceError,
} from './reminder-settings.service';

/**
 * Cross-field FormArray validator that enforces:
 *  - All interval values are unique within the array.
 *  - The array contains at most 10 intervals.
 *
 * Returns `{ duplicateIntervals: true }` or `{ maxIntervals: true }` when
 * violated; `null` when valid.
 */
const uniqueIntervalsValidator: ValidatorFn = (
  control: AbstractControl,
): ValidationErrors | null => {
  const array = control as FormArray;

  if (array.length > 10) {
    return { maxIntervals: true };
  }

  const values = array.controls.map((c) => c.value as number);
  const unique = new Set(values);

  if (unique.size !== values.length) {
    return { duplicateIntervals: true };
  }

  return null;
};

/**
 * ReminderSettingsComponent — `/settings/reminders`
 *
 * Displays and edits the system-wide reminder interval configuration
 * (in hours). Accessible to Staff and Admin roles only (guarded at route
 * level by `staffGuard`).
 *
 * AC-3 (US_033): Changes to default reminder intervals are persisted via
 * PUT /api/settings/reminders so future schedules use the new values.
 *
 * OWASP A03 — raw error stacks are never rendered; only safe messages are
 * shown.
 * WCAG 2.2 AA — `aria-label` on each input; `aria-busy` on save button.
 */
@Component({
  selector: 'app-reminder-settings',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
  ],
  templateUrl: './reminder-settings.component.html',
  styleUrl: './reminder-settings.component.scss',
})
export class ReminderSettingsComponent implements OnInit {
  private readonly service = inject(ReminderSettingsService);
  private readonly snackBar = inject(MatSnackBar);

  readonly form = new FormGroup({
    intervalHours: new FormArray<FormControl<number | null>>(
      [],
      [uniqueIntervalsValidator],
    ),
  });

  readonly isLoading = signal(true);
  readonly isSaving = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly saveError = signal<string | null>(null);

  ngOnInit(): void {
    this.service.getIntervals().subscribe({
      next: ({ intervalHours }) => {
        intervalHours.forEach((h) => this.addInterval(h));
        this.isLoading.set(false);
      },
      error: () => {
        this.loadError.set('Failed to load reminder settings. Please refresh.');
        this.isLoading.set(false);
      },
    });
  }

  /** Appends a new interval input row to the FormArray. */
  addInterval(value = 0): void {
    this.intervals.push(
      new FormControl<number | null>(value, {
        validators: [
          Validators.required,
          Validators.min(1),
          Validators.max(168),
        ],
      }),
    );
  }

  /** Removes the interval at the given index (minimum 1 row enforced). */
  removeInterval(index: number): void {
    if (this.intervals.length > 1) {
      this.intervals.removeAt(index);
    }
  }

  /** Saves the current interval list via PUT /api/settings/reminders. */
  save(): void {
    if (this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }

    const hours = this.intervals.value.filter(
      (v): v is number => v !== null,
    );

    this.isSaving.set(true);
    this.saveError.set(null);
    this.form.disable();

    this.service.updateIntervals(hours).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.form.enable();
        this.snackBar.open('Reminder settings saved.', 'Dismiss', {
          duration: 4000,
        });
      },
      error: (err: ReminderSettingsServiceError) => {
        this.isSaving.set(false);
        this.form.enable();
        this.saveError.set(err.message ?? 'Save failed. Please try again.');
      },
    });
  }

  get intervals(): FormArray<FormControl<number | null>> {
    return this.form.controls.intervalHours;
  }

  get hasDuplicates(): boolean {
    return this.intervals.hasError('duplicateIntervals');
  }

  get hasMaxExceeded(): boolean {
    return this.intervals.hasError('maxIntervals');
  }
}
