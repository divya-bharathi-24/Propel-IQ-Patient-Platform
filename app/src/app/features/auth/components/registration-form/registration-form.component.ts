import { Component, OnInit, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../services/auth.service';
import { passwordComplexityValidator } from '../../validators/password-complexity.validator';
import { RegistrationRequest } from '../../models/registration.models';

@Component({
  selector: 'app-registration-form',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatDatepickerModule,
    MatNativeDateModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './registration-form.component.html',
  styleUrl: './registration-form.component.scss',
})
export class RegistrationFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly isSubmitting = signal(false);
  readonly maxDob = new Date();

  form!: FormGroup;

  ngOnInit(): void {
    this.form = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      email: [
        '',
        [Validators.required, Validators.email, Validators.maxLength(254)],
      ],
      password: ['', [Validators.required, passwordComplexityValidator()]],
      phone: ['', [Validators.pattern(/^\+?[1-9]\d{6,14}$/)]],
      dateOfBirth: ['', [Validators.required]],
    });
  }

  get nameControl(): AbstractControl {
    return this.form.get('name')!;
  }

  get emailControl(): AbstractControl {
    return this.form.get('email')!;
  }

  get passwordControl(): AbstractControl {
    return this.form.get('password')!;
  }

  get phoneControl(): AbstractControl {
    return this.form.get('phone')!;
  }

  get dobControl(): AbstractControl {
    return this.form.get('dateOfBirth')!;
  }

  onSubmit(): void {
    if (this.form.invalid || this.isSubmitting()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);

    const raw = this.form.getRawValue();
    const dobValue: Date | string = raw.dateOfBirth;
    const dob =
      dobValue instanceof Date
        ? dobValue.toISOString().split('T')[0]
        : String(dobValue);

    const payload: RegistrationRequest = {
      name: raw.name.trim(),
      email: raw.email.trim().toLowerCase(),
      password: raw.password,
      phone: raw.phone?.trim() || undefined,
      dateOfBirth: dob,
    };

    this.authService.register(payload).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.router.navigate(['/auth/verify-pending'], {
          state: { email: payload.email },
        });
      },
      error: (err: { status: number; message: string }) => {
        this.isSubmitting.set(false);
        if (err.status === 409) {
          this.emailControl.setErrors({ alreadyRegistered: true });
        } else if (err.status === 400) {
          this.emailControl.setErrors({ serverError: err.message });
        }
      },
    });
  }
}
