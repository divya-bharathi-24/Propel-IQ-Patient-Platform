import { Component, OnInit, inject, output, signal } from '@angular/core';
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
import { MatSelectModule } from '@angular/material/select';
import { WalkInStore } from '../../state/walkin.store';
import { SpecialtyService } from '../../../appointments/services/specialty.service';
import { SpecialtyDto } from '../../../appointments/models/slot.models';

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
    MatSelectModule,
  ],
  templateUrl: './quick-create-patient-form.component.html',
})
export class QuickCreatePatientFormComponent implements OnInit {
  protected readonly store = inject(WalkInStore);
  private readonly fb = inject(FormBuilder);
  private readonly specialtyService = inject(SpecialtyService);

  /** Emits when staff clicks the back button to return to patient search. */
  readonly backRequested = output<void>();

  form!: FormGroup;
  specialties = signal<SpecialtyDto[]>([]);

  /** Today's date in YYYY-MM-DD for the min date attribute */
  readonly todayStr = new Date().toISOString().split('T')[0];

  ngOnInit(): void {
    this.form = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      contactNumber: ['', [Validators.pattern(E164_PATTERN)]],
      email: [
        '',
        [Validators.required, Validators.email, Validators.maxLength(254)],
      ],
      specialtyId: ['', Validators.required],
      date: [this.todayStr, Validators.required],
    });

    this.specialtyService.getSpecialties().subscribe({
      next: (list) => this.specialties.set(list),
      error: () => {
        /* non-critical */
      },
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

  get specialtyIdControl(): AbstractControl {
    return this.form.get('specialtyId')!;
  }

  get dateControl(): AbstractControl {
    return this.form.get('date')!;
  }

  onBack(): void {
    this.backRequested.emit();
  }

  onSubmit(): void {
    if (this.form.invalid || this.store.actionState() === 'submitting') {
      this.form.markAllAsTouched();
      return;
    }

    const { name, contactNumber, email, specialtyId, date } =
      this.form.getRawValue();
    this.store.submitWalkIn({
      mode: 'create',
      name: name.trim(),
      email: email.trim().toLowerCase(),
      specialtyId,
      date,
      ...(contactNumber?.trim() ? { contactNumber: contactNumber.trim() } : {}),
    });
  }

  onLinkToExisting(): void {
    const duplicate = this.store.duplicatePatient();
    if (!duplicate) return;
    const specialtyId = this.form.value.specialtyId;
    const date = this.form.value.date;
    if (!specialtyId || !date) {
      this.form.markAllAsTouched();
      return;
    }
    this.store.submitWalkIn({
      mode: 'link',
      patientId: duplicate.patientId,
      specialtyId,
      date,
    });
  }
}
