import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  Input,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { catchError, of } from 'rxjs';
import { CalendarSyncService } from '../../../core/services/calendar-sync.service';
import {
  OutlookSyncState,
  OutlookSyncStatus,
} from '../../patient/calendar/calendar-sync.models';

/**
 * Outlook Calendar sync button + status widget (EP-007 / US_036).
 *
 * Standalone, OnPush component surfaced on:
 * - Booking confirmation page (AC-1, AC-2, AC-3, AC-4)
 * - Patient dashboard — per appointment card (AC-1 – AC-4)
 *
 * Sync flow:
 * 1. Patient clicks "Add to Outlook Calendar" → `initiateSync()` calls
 *    POST /api/calendar/outlook/initiate → redirects to Microsoft consent via
 *    `window.location.href` (PKCE OAuth 2.0 — no token stored in JS).
 * 2. Microsoft redirects to `/calendar/outlook/callback`; `OutlookCallbackComponent`
 *    exchanges the code and creates the Graph event.
 * 3. On next render (e.g. dashboard reload) `ngOnInit` polls
 *    `getOutlookSyncStatus()` to restore synced state and show the event link.
 *
 * OWASP A05 — external Outlook event links rendered with rel="noopener noreferrer".
 * WCAG 2.2 — aria-live regions for status messages; role="alert" for errors.
 */
@Component({
  selector: 'app-outlook-calendar-sync',
  standalone: true,
  imports: [MatButtonModule, MatChipsModule, MatProgressSpinnerModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @switch (syncState().status) {
      @case ('Unknown') {
        <div class="sync-actions">
          <button
            mat-raised-button
            color="primary"
            (click)="initiateSync()"
            aria-label="Add this appointment to Outlook Calendar"
          >
            Add to Outlook Calendar
          </button>
          <button
            mat-stroked-button
            (click)="downloadIcs()"
            aria-label="Download ICS file for manual calendar import"
          >
            Download ICS
          </button>
        </div>
      }

      @case ('Initiating') {
        <div class="sync-initiating" role="status" aria-live="polite">
          <mat-spinner diameter="18" strokeWidth="2" />
          <span>Redirecting to Microsoft…</span>
        </div>
      }

      @case ('Synced') {
        <!-- AC-2: Show green chip + external event link -->
        <div class="sync-status" role="status" aria-live="polite">
          <mat-chip-set aria-label="Outlook Calendar sync status">
            <mat-chip
              class="chip-synced"
              [attr.aria-label]="'Outlook Calendar sync status: Synced'"
            >
              Synced to Outlook ✓
            </mat-chip>
          </mat-chip-set>

          @if (syncState().eventLink) {
            <a
              [href]="syncState().eventLink"
              target="_blank"
              rel="noopener noreferrer"
              class="event-link"
              aria-label="Open this appointment in Outlook Calendar (opens in new tab)"
            >
              Open in Outlook
            </a>
          }
        </div>
      }

      @case ('Failed') {
        <!-- AC-4: Show amber chip + prominent ICS fallback + retry -->
        <div class="sync-status" role="alert" aria-live="assertive">
          <mat-chip-set aria-label="Outlook Calendar sync status">
            <mat-chip
              class="chip-failed"
              [attr.aria-label]="'Outlook Calendar sync status: Sync failed'"
            >
              Sync failed
            </mat-chip>
          </mat-chip-set>

          <p class="fallback-message" aria-live="polite">
            Sync failed. Download ICS as a fallback.
          </p>

          <div class="sync-actions">
            <button
              mat-flat-button
              color="warn"
              class="fallback-prominent"
              (click)="downloadIcs()"
              aria-label="Download ICS calendar file as a fallback for this appointment"
            >
              Download ICS
            </button>
            <button
              mat-stroked-button
              (click)="initiateSync()"
              aria-label="Retry syncing this appointment to Outlook Calendar"
            >
              Retry Outlook Sync
            </button>
          </div>
        </div>
      }

      @case ('Revoked') {
        <!-- Edge case: OAuth consent revoked — prompt re-authorization -->
        <div class="sync-status" role="status" aria-live="polite">
          <mat-chip-set aria-label="Outlook Calendar sync status">
            <mat-chip
              class="chip-revoked"
              [attr.aria-label]="'Outlook Calendar sync status: Disconnected'"
            >
              Outlook disconnected
            </mat-chip>
          </mat-chip-set>

          <button
            mat-flat-button
            color="primary"
            (click)="initiateSync()"
            aria-label="Reconnect Outlook Calendar to re-authorize and sync this appointment"
          >
            Reconnect Outlook
          </button>
        </div>
      }
    }
  `,
  styles: [
    `
      :host {
        display: block;
      }

      .sync-actions {
        display: flex;
        gap: 0.75rem;
        flex-wrap: wrap;
        align-items: center;
      }

      .sync-initiating {
        display: flex;
        align-items: center;
        gap: 0.5rem;
        color: #5f6368;
        font-size: 0.875rem;
      }

      .sync-status {
        display: flex;
        flex-direction: column;
        gap: 0.75rem;
      }

      .event-link {
        font-size: 0.875rem;
        color: #0078d4;
        text-decoration: underline;
      }

      .fallback-message {
        margin: 0;
        font-size: 0.875rem;
        color: #5f6368;
      }

      .chip-synced {
        background-color: #e8f5e9;
        color: #1b5e20;
      }

      .chip-failed {
        background-color: #fff8e1;
        color: #f57f17;
      }

      .chip-revoked {
        background-color: #fce4ec;
        color: #880e4f;
      }

      .fallback-prominent {
        font-weight: 600;
      }
    `,
  ],
})
export class OutlookCalendarSyncComponent implements OnInit {
  /** UUID of the appointment to sync. Required by the parent. */
  @Input({ required: true }) appointmentId!: string;

  private readonly svc = inject(CalendarSyncService);
  private readonly destroyRef = inject(DestroyRef);

  /** Signal holding the current Outlook sync state for this appointment. */
  readonly syncState = signal<OutlookSyncState>({
    status: 'Unknown',
    eventLink: null,
    errorMessage: null,
  });

  ngOnInit(): void {
    // Restore existing sync state from the server so the component reflects
    // actual state (e.g. already synced on dashboard reload).
    this.svc
      .getOutlookSyncStatus(this.appointmentId, 'Outlook')
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        catchError(() => of(null)),
      )
      .subscribe((res) => {
        if (res) {
          this.syncState.set({
            status: res.syncStatus as OutlookSyncStatus,
            eventLink: res.eventLink,
            errorMessage: null,
          });
        }
      });
  }

  /**
   * Starts the Microsoft OAuth 2.0 PKCE flow.
   *
   * Sets status to `Initiating` optimistically, calls the backend to obtain
   * the authorization URL, then performs a full browser redirect so Microsoft's
   * consent screen loads. On API error the status falls back to `Failed`.
   */
  initiateSync(): void {
    this.syncState.update((s) => ({ ...s, status: 'Initiating' }));

    this.svc
      .initiateOutlookSync(this.appointmentId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => {
          // Full browser redirect — Angular router must NOT intercept this.
          window.location.href = res.authorizationUrl;
        },
        error: () => {
          this.syncState.set({
            status: 'Failed',
            eventLink: null,
            errorMessage: 'Could not initiate Outlook sync. Please try again.',
          });
        },
      });
  }

  /**
   * Downloads the appointment ICS file as a `Blob` and triggers a browser
   * download via a transient anchor element (AC-3).
   *
   * `URL.revokeObjectURL()` is called after the click to free memory.
   */
  downloadIcs(): void {
    this.svc
      .downloadIcsBlob(this.appointmentId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((blob) => {
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = 'appointment.ics';
        anchor.click();
        URL.revokeObjectURL(url);
      });
  }
}
