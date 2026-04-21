import { Component, OnInit, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../services/auth.service';
import { passwordComplexityValidator } from '../../validators/password-complexity.validator';

type SetupState = 'form' | 'success' | 'expired' | 'already-used' | 'loading';

/** Cross-field validator: confirmPassword must equal password. */
function passwordMatchValidator(): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    const password = group.get('password')?.value ?? '';
    const confirm = group.get('confirmPassword')?.value ?? '';
    return password === confirm ? null : { passwordMismatch: true };
  };
}

@Component({
  selector: 'app-credential-setup',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './credential-setup.component.html',
})
export class CredentialSetupComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  readonly state = signal<SetupState>('form');
  readonly isSubmitting = signal(false);
  readonly fieldErrors = signal<Record<string, string>>({});

  private token = '';

  form!: FormGroup;

  ngOnInit(): void {
    this.token = this.route.snapshot.queryParamMap.get('token') ?? '';

    this.form = this.fb.group(
      {
        password: ['', [Validators.required, passwordComplexityValidator()]],
        confirmPassword: ['', [Validators.required]],
      },
      { validators: passwordMatchValidator() },
    );
  }

  get passwordControl(): AbstractControl {
    return this.form.get('password')!;
  }

  get confirmControl(): AbstractControl {
    return this.form.get('confirmPassword')!;
  }

  onSubmit(): void {
    if (this.form.invalid || this.isSubmitting()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.fieldErrors.set({});

    const { password } = this.form.getRawValue();

    this.authService
      .setupCredentials({ token: this.token, password })
      .subscribe({
        next: () => {
          this.isSubmitting.set(false);
          this.state.set('success');
        },
        error: (err: {
          status: number;
          message: string;
          fieldErrors?: Record<string, string>;
        }) => {
          this.isSubmitting.set(false);
          if (err.status === 410) {
            this.state.set('expired');
          } else if (err.status === 409) {
            this.state.set('already-used');
          } else if (err.status === 400 && err.fieldErrors) {
            this.fieldErrors.set(err.fieldErrors);
          } else {
            this.fieldErrors.set({
              general: 'An unexpected error occurred. Please try again.',
            });
          }
        },
      });
  }
}
