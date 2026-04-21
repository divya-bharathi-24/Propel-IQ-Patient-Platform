import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-email-verification-pending',
  standalone: true,
  imports: [MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './email-verification-pending.component.html',
  styleUrl: './email-verification-pending.component.scss',
})
export class EmailVerificationPendingComponent implements OnInit, OnDestroy {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly email = signal<string>('');
  readonly cooldownSeconds = signal(0);
  readonly isResending = signal(false);
  readonly resendSuccess = signal(false);
  readonly resendError = signal('');

  private cooldownInterval: ReturnType<typeof setInterval> | null = null;

  ngOnInit(): void {
    const nav = this.router.getCurrentNavigation();
    const stateEmail = (nav?.extras?.state as { email?: string } | undefined)
      ?.email;

    if (!stateEmail) {
      const historyState = history.state as { email?: string } | undefined;
      this.email.set(historyState?.email ?? '');
    } else {
      this.email.set(stateEmail);
    }
  }

  ngOnDestroy(): void {
    this.clearCooldown();
  }

  get cooldownActive(): boolean {
    return this.cooldownSeconds() > 0;
  }

  resendEmail(): void {
    if (this.cooldownActive || this.isResending()) {
      return;
    }

    const emailValue = this.email();
    if (!emailValue) {
      return;
    }

    this.isResending.set(true);
    this.resendSuccess.set(false);
    this.resendError.set('');

    this.authService.resendVerification(emailValue).subscribe({
      next: () => {
        this.isResending.set(false);
        this.resendSuccess.set(true);
        this.startCooldown(60);
      },
      error: () => {
        this.isResending.set(false);
        this.resendError.set('Unable to resend email. Please try again later.');
      },
    });
  }

  private startCooldown(seconds: number): void {
    this.cooldownSeconds.set(seconds);
    this.cooldownInterval = setInterval(() => {
      const current = this.cooldownSeconds();
      if (current <= 1) {
        this.cooldownSeconds.set(0);
        this.clearCooldown();
      } else {
        this.cooldownSeconds.set(current - 1);
      }
    }, 1000);
  }

  private clearCooldown(): void {
    if (this.cooldownInterval !== null) {
      clearInterval(this.cooldownInterval);
      this.cooldownInterval = null;
    }
  }
}
