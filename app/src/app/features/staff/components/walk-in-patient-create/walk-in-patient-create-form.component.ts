import { Component, OnInit, inject, output, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { StaffService } from '../../services/staff.service';
import { PatientSearchResult } from '../../models/staff.models';

/** E.164 phone number pattern (international format). */
const E164_PATTERN = /^\+[1-9]\d{1,14}$/;

@Component({
  selector: 'app-walk-in-patient-create-form',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './walk-in-patient-create-form.component.html',
})
export class WalkInPatientCreateFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly staffService = inject(StaffService);

  /** Emits the newly created patient's ID to the parent booking component. */
  readonly patientCreated = output<string>();

  readonly isExpanded = signal(false);
  readonly isSubmitting = signal(false);
  readonly inlineError = signal<string | null>(null);
  readonly duplicateEmail = signal<string | null>(null);
  readonly existingPatient = signal<PatientSearchResult | null>(null);
  readonly isLinkingPatient = signal(false);

  form!: FormGroup;

  ngOnInit(): void {
    this.form = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      phone: ['', [Validators.pattern(E164_PATTERN)]],
      email: [
        '',
        [Validators.required, Validators.email, Validators.maxLength(254)],
      ],
    });
  }

  get nameControl(): AbstractControl {
    return this.form.get('name')!;
  }

  get phoneControl(): AbstractControl {
    return this.form.get('phone')!;
  }

  get emailControl(): AbstractControl {
    return this.form.get('email')!;
  }

  toggleExpand(): void {
    this.isExpanded.update((v) => !v);
  }

  onSubmit(): void {
    if (this.form.invalid || this.isSubmitting()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.inlineError.set(null);
    this.duplicateEmail.set(null);
    this.existingPatient.set(null);

    const { name, phone, email } = this.form.getRawValue();
    const dto = {
      name: name.trim(),
      email: email.trim().toLowerCase(),
      ...(phone?.trim() ? { phone: phone.trim() } : {}),
    };

    this.staffService.createWalkInPatient(dto).subscribe({
      next: (res) => {
        this.isSubmitting.set(false);
        this.patientCreated.emit(res.patientId);
        this.form.reset();
        this.isExpanded.set(false);
      },
      error: (err: { status: number; message: string }) => {
        this.isSubmitting.set(false);
        if (err.status === 409) {
          this.duplicateEmail.set(email.trim().toLowerCase());
        } else if (err.status === 403) {
          this.inlineError.set('Insufficient permissions to create a patient.');
        } else {
          this.inlineError.set(
            'An unexpected error occurred. Please try again.',
          );
        }
      },
    });
  }

  linkExistingPatient(): void {
    const email = this.duplicateEmail();
    if (!email) return;

    this.isLinkingPatient.set(true);
    this.staffService.searchPatient(email).subscribe({
      next: (patient) => {
        this.isLinkingPatient.set(false);
        this.existingPatient.set(patient);
      },
      error: () => {
        this.isLinkingPatient.set(false);
        this.inlineError.set(
          'Could not retrieve existing patient. Please search manually.',
        );
      },
    });
  }

  selectExistingPatient(): void {
    const patient = this.existingPatient();
    if (patient) {
      this.patientCreated.emit(patient.patientId);
      this.form.reset();
      this.isExpanded.set(false);
      this.duplicateEmail.set(null);
      this.existingPatient.set(null);
    }
  }
}
