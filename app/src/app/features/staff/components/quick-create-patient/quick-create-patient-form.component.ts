import { Component, OnInit, inject, output } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { WalkInStore } from '../../state/walkin.store';

/** E.164 international phone number pattern (e.g. +14155552671). */
const E164_PATTERN = /^\+[1-9]\d{1,14}$/;

@Component({
  selector: 'app-quick-create-patient-form',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './quick-create-patient-form.component.html',
})
export class QuickCreatePatientFormComponent implements OnInit {
  protected readonly store = inject(WalkInStore);
  private readonly fb = inject(FormBuilder);

  /** Emits when staff clicks the back button to return to patient search. */
  readonly backRequested = output<void>();

  form!: FormGroup;

  ngOnInit(): void {
    this.form = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      contactNumber: ['', [Validators.pattern(E164_PATTERN)]],
      email: [
        '',
        [Validators.required, Validators.email, Validators.maxLength(254)],
      ],
    });
  }

  get nameControl(): AbstractControl {
    return this.form.get('name')!;
  }

  get contactNumberControl(): AbstractControl {
    return this.form.get('contactNumber')!;
  }

  get emailControl(): AbstractControl {
    return this.form.get('email')!;
  }

  onBack(): void {
    this.backRequested.emit();
  }

  onSubmit(): void {
    if (this.form.invalid || this.store.actionState() === 'submitting') {
      this.form.markAllAsTouched();
      return;
    }

    const { name, contactNumber, email } = this.form.getRawValue();
    this.store.submitWalkIn({
      mode: 'create',
      name: name.trim(),
      email: email.trim().toLowerCase(),
      ...(contactNumber?.trim() ? { contactNumber: contactNumber.trim() } : {}),
    });
  }

  onLinkToExisting(): void {
    const duplicate = this.store.duplicatePatient();
    if (!duplicate) return;
    this.store.submitWalkIn({ mode: 'link', patientId: duplicate.patientId });
  }
}
