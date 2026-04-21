import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../services/auth.service';

type VerifyState = 'loading' | 'success' | 'expired' | 'already-used' | 'error';

@Component({
  selector: 'app-email-verify-callback',
  standalone: true,
  imports: [RouterLink, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './email-verify-callback.component.html',
  styleUrl: './email-verify-callback.component.scss',
})
export class EmailVerifyCallbackComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly authService = inject(AuthService);

  readonly state = signal<VerifyState>('loading');

  ngOnInit(): void {
    const token = this.route.snapshot.queryParamMap.get('token');

    if (!token) {
      this.state.set('error');
      return;
    }

    this.authService.verifyEmail(token).subscribe({
      next: () => {
        this.state.set('success');
        setTimeout(() => this.router.navigate(['/booking']), 2000);
      },
      error: (err: { status: number }) => {
        if (err.status === 410) {
          this.state.set('expired');
        } else if (err.status === 409) {
          this.state.set('already-used');
        } else {
          this.state.set('error');
        }
      },
    });
  }
}
