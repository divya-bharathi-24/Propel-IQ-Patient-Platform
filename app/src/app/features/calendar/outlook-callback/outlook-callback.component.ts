import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CalendarSyncService } from '../../../core/services/calendar-sync.service';

/**
 * OAuth 2.0 callback handler for Microsoft Outlook Calendar sync (EP-007 / US_036).
 *
 * Mounted at `/calendar/outlook/callback` — the redirect URI registered in the
 * Microsoft Entra app registration. This route intentionally has NO auth guard
 * because Microsoft redirects here before the patient's session is restored.
 *
 * Flow:
 * 1. Reads `code` and `state` from query parameters (set by Microsoft).
 * 2. Calls `CalendarSyncService.exchangeOutlookCode()` →
 *    GET /api/calendar/outlook/callback?code=&state=
 *    The backend exchanges the code, creates the Graph event, and stores the
 *    CalendarSync record.
 * 3. On success → navigates to `/patient/dashboard`.
 * 4. On failure → navigates to `/patient/dashboard?calendarError=outlook`
 *    so the dashboard can surface a user-friendly error banner (AC-4).
 *
 * OWASP A07 — `state` parameter is forwarded to the backend for CSRF
 *             validation within the PKCE flow; no token is handled in the FE.
 */
@Component({
  selector: 'app-outlook-callback',
  standalone: true,
  imports: [],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="callback-container" role="status" aria-live="polite">
      <p>Completing Outlook Calendar sync…</p>
    </div>
  `,
  styles: [
    `
      .callback-container {
        display: flex;
        justify-content: center;
        align-items: center;
        min-height: 200px;
        font-size: 1rem;
        color: #5f6368;
      }
    `,
  ],
})
export class OutlookCallbackComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly svc = inject(CalendarSyncService);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    const code = this.route.snapshot.queryParamMap.get('code') ?? '';
    const state = this.route.snapshot.queryParamMap.get('state') ?? '';

    this.svc
      .exchangeOutlookCode(code, state)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.router.navigate(['/patient/dashboard']),
        error: () =>
          this.router.navigate(['/patient/dashboard'], {
            queryParams: { calendarError: 'outlook' },
          }),
      });
  }
}
