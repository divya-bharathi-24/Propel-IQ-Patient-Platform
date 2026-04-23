import {
  Component,
  EventEmitter,
  Input,
  OnDestroy,
  OnInit,
  Output,
  inject,
  signal,
} from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { Subject, debounceTime, takeUntil } from 'rxjs';
import {
  PatientProfileDto,
  UpdatePatientProfileDto,
} from '../../models/patient-profile.models';
import { PatientProfileDraftService } from '../../services/patient-profile-draft.service';
import { PatientService } from '../../services/patient.service';
import { e164PhoneValidator } from '../../validators/e164-phone.validator';

@Component({
  selector: 'app-patient-profile-edit-form',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatSnackBarModule,
  ],
  templateUrl: './patient-profile-edit-form.component.html',
  styleUrl: './patient-profile-edit-form.component.scss',
})
export class PatientProfileEditFormComponent implements OnInit, OnDestroy {
  @Input() initialValue!: UpdatePatientProfileDto;
  @Input() eTag!: string;

  @Output() saved = new EventEmitter<PatientProfileDto>();
  @Output() cancelled = new EventEmitter<void>();

  private readonly fb = inject(FormBuilder);
  private readonly patientService = inject(PatientService);
  private readonly draftService = inject(PatientProfileDraftService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly router = inject(Router);

  private readonly destroy$ = new Subject<void>();

  readonly isSubmitting = signal(false);
  readonly conflictWarning = signal(false);
  readonly draftBanner = signal(false);

  form!: FormGroup;

  ngOnInit(): void {
    this.form = this.fb.group({
      phone: [
        this.initialValue.phone ?? '',
        [e164PhoneValidator(), Validators.maxLength(20)],
      ],
      address: this.fb.group({
        street: [
          this.initialValue.address?.street ?? '',
          Validators.maxLength(200),
        ],
        city: [
          this.initialValue.address?.city ?? '',
          Validators.maxLength(200),
        ],
        state: [
          this.initialValue.address?.state ?? '',
          Validators.maxLength(200),
        ],
        postalCode: [
          this.initialValue.address?.postalCode ?? '',
          Validators.maxLength(200),
        ],
        country: [
          this.initialValue.address?.country ?? '',
          Validators.maxLength(200),
        ],
      }),
      emergencyContact: this.fb.group({
        name: [
          this.initialValue.emergencyContact?.name ?? '',
          Validators.maxLength(200),
        ],
        phone: [
          this.initialValue.emergencyContact?.phone ?? '',
          [e164PhoneValidator(), Validators.maxLength(20)],
        ],
        relationship: [
          this.initialValue.emergencyContact?.relationship ?? '',
          Validators.maxLength(200),
        ],
      }),
      communicationPreferences: this.fb.group({
        emailOptIn: [
          this.initialValue.communicationPreferences?.emailOptIn ?? false,
        ],
        smsOptIn: [
          this.initialValue.communicationPreferences?.smsOptIn ?? false,
        ],
        preferredLanguage: [
          this.initialValue.communicationPreferences?.preferredLanguage ?? 'en',
          Validators.maxLength(10),
        ],
      }),
    });

    // Restore draft if present (session-expiry resilience)
    const draft = this.draftService.loadDraft();
    if (draft) {
      this.form.patchValue(draft);
      this.draftBanner.set(true);
    }

    // Auto-save draft on every change (debounced)
    this.form.valueChanges
      .pipe(debounceTime(500), takeUntil(this.destroy$))
      .subscribe((value) => {
        this.draftService.saveDraft(value as UpdatePatientProfileDto);
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ── Convenience accessors ──────────────────────────────────────────────────

  get phoneControl(): AbstractControl {
    return this.form.get('phone')!;
  }

  get emergencyPhoneControl(): AbstractControl {
    return this.form.get('emergencyContact.phone')!;
  }

  // ── Form submission ────────────────────────────────────────────────────────

  onSubmit(): void {
    if (this.form.invalid || this.isSubmitting()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.conflictWarning.set(false);

    const dto = this.form.getRawValue() as UpdatePatientProfileDto;

    this.patientService.updateProfile(dto, this.eTag).subscribe({
      next: (updated) => {
        this.isSubmitting.set(false);
        this.draftService.clearDraft();
        this.snackBar.open('Profile updated successfully', 'Dismiss', {
          duration: 4000,
        });
        this.saved.emit(updated);
      },
      error: (err) => {
        this.isSubmitting.set(false);

        if (err?.status === 409) {
          this.conflictWarning.set(true);
          // Refresh eTag in background so the user can resubmit
          this.patientService.getProfile().subscribe({
            next: ({ eTag }) => {
              // Update local eTag; form values stay intact for review
              this.eTag = eTag;
            },
          });
          return;
        }

        if (err?.status === 403) {
          this.router.navigate(['/access-denied']);
          return;
        }

        if (err?.status === 400 && err?.error?.errors) {
          this.applyServerValidationErrors(
            err.error.errors as Array<{ field: string; message: string }>,
          );
          return;
        }

        // Generic error — do not interpolate raw server messages (NFR-014)
        this.snackBar.open(
          'An error occurred while saving. Please try again.',
          'Dismiss',
          { duration: 5000 },
        );
        console.error(
          'PatientProfileEditFormComponent: updateProfile failed',
          err?.status,
        );
      },
    });
  }

  onCancel(): void {
    this.cancelled.emit();
  }

  // ── Private helpers ────────────────────────────────────────────────────────

  private applyServerValidationErrors(
    errors: Array<{ field: string; message: string }>,
  ): void {
    for (const { field, message } of errors) {
      const control = this.form.get(field);
      if (control) {
        control.setErrors({ serverError: message });
      }
    }
  }
}
