import { Component, OnInit, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../services/auth.service';
import { SessionTimerService } from '../../../../core/auth/session-timer.service';

type SessionReason = 'idle_timeout' | 'session_expired';

const SESSION_MESSAGES: Record<SessionReason, string> = {
  idle_timeout: 'Your session expired due to inactivity. Please log in again.',
  session_expired:
    'Your session was invalidated for security. Please log in again.',
};

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly sessionTimer = inject(SessionTimerService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly isSubmitting = signal(false);
  readonly serverError = signal<string | null>(null);

  /** Non-null when the user landed here due to session expiry or idle timeout. */
  readonly sessionBannerMessage = signal<string | null>(null);

  form!: FormGroup;

  ngOnInit(): void {
    this.form = this.fb.group({
      email: [
        '',
        [Validators.required, Validators.email, Validators.maxLength(254)],
      ],
      password: ['', [Validators.required, Validators.minLength(8)]],
    });

    // Show non-dismissable banner if redirected with a session expiry reason
    const reason = this.route.snapshot.queryParamMap.get(
      'reason',
    ) as SessionReason | null;
    if (reason && SESSION_MESSAGES[reason]) {
      this.sessionBannerMessage.set(SESSION_MESSAGES[reason]);
    }
  }

  get emailControl(): AbstractControl {
    return this.form.get('email')!;
  }

  get passwordControl(): AbstractControl {
    return this.form.get('password')!;
  }

  onSubmit(): void {
    if (this.form.invalid || this.isSubmitting()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.serverError.set(null);

    const { email, password } = this.form.getRawValue();

    this.authService.login(email.trim().toLowerCase(), password).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        // Wire session timer: stop callback registered so AuthService can stop
        // the timer on logout without a direct circular dependency.
        this.authService.registerSessionTimerStop(() =>
          this.sessionTimer.stop(),
        );
        this.sessionTimer.start(() => this.authService.logout('idle_timeout'));
        const role = this.authService.currentRole();
        if (role === 'Admin') {
          this.router.navigate(['/admin/users']);
        } else if (role === 'Staff') {
          this.router.navigate(['/staff/walkin']);
        } else {
          this.router.navigate(['/dashboard']);
        }
      },
      error: (err: { status: number; message: string }) => {
        this.isSubmitting.set(false);
        if (err.status === 401 || err.status === 400) {
          this.serverError.set('Invalid email or password. Please try again.');
        } else {
          this.serverError.set(
            err.message ?? 'An unexpected error occurred. Please try again.',
          );
        }
      },
    });
  }
}
