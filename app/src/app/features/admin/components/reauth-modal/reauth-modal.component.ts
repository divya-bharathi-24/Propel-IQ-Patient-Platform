import {
  Component,
  OnDestroy,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import {
  MAT_DIALOG_DATA,
  MatDialogModule,
  MatDialogRef,
} from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ReAuthModalData, ReAuthModalResult } from '../../models/admin.models';
import { AdminService } from '../../services/admin.service';

const TIMEOUT_MS = 5 * 60 * 1000;
const COUNTDOWN_TOTAL_SECONDS = 5 * 60;

/**
 * Shared re-authentication MatDialog (US_046 / FR-062).
 *
 * Opens with `{ actionLabel: string }` dialog data.
 * Closes with a typed `ReAuthModalResult`:
 *   - `{ status: 'confirmed', reAuthToken }` on successful re-auth (HTTP 200)
 *   - `{ status: 'cancelled' }` when the user dismisses the dialog
 *   - `{ status: 'timeout' }` after the 5-minute countdown expires
 *
 * Inline error "Incorrect password — please try again" is displayed on HTTP 401;
 * the modal remains open and the password field is cleared.
 */
@Component({
  selector: 'app-reauth-modal',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './reauth-modal.component.html',
  styleUrl: './reauth-modal.component.scss',
})
export class ReauthenticationModalComponent implements OnInit, OnDestroy {
  private readonly dialogRef = inject(
    MatDialogRef<ReauthenticationModalComponent, ReAuthModalResult>,
  );
  readonly data = inject<ReAuthModalData>(MAT_DIALOG_DATA);
  private readonly adminService = inject(AdminService);

  readonly passwordControl = new FormControl('', [Validators.required]);
  readonly inlineError = signal<string | null>(null);
  readonly loading = signal(false);
  readonly remainingSeconds = signal(COUNTDOWN_TOTAL_SECONDS);

  readonly countdownDisplay = computed(() => {
    const s = this.remainingSeconds();
    const minutes = Math.floor(s / 60);
    const seconds = s % 60;
    return `${minutes}:${String(seconds).padStart(2, '0')}`;
  });

  private timeoutHandle: ReturnType<typeof setTimeout> | null = null;
  private intervalHandle: ReturnType<typeof setInterval> | null = null;

  ngOnInit(): void {
    this.timeoutHandle = setTimeout(() => {
      this.dialogRef.close({ status: 'timeout' });
    }, TIMEOUT_MS);

    this.intervalHandle = setInterval(() => {
      this.remainingSeconds.update((s) => Math.max(0, s - 1));
    }, 1000);
  }

  ngOnDestroy(): void {
    if (this.timeoutHandle !== null) {
      clearTimeout(this.timeoutHandle);
    }
    if (this.intervalHandle !== null) {
      clearInterval(this.intervalHandle);
    }
  }

  onSubmit(): void {
    if (this.passwordControl.invalid || this.loading()) return;

    const password = this.passwordControl.value!;
    this.loading.set(true);
    this.inlineError.set(null);

    this.adminService.reauthenticate(password).subscribe({
      next: ({ reAuthToken }) => {
        this.loading.set(false);
        this.dialogRef.close({ status: 'confirmed', reAuthToken });
      },
      error: (err: { status: number; message: string }) => {
        this.loading.set(false);
        if (err.status === 401) {
          this.inlineError.set('Incorrect password — please try again');
          this.passwordControl.reset('');
        } else {
          this.inlineError.set(
            'An unexpected error occurred. Please try again.',
          );
        }
      },
    });
  }

  onCancel(): void {
    this.dialogRef.close({ status: 'cancelled' });
  }
}
