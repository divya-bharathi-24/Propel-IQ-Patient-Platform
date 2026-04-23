import {
  ChangeDetectionStrategy,
  Component,
  Input,
  OnInit,
  inject,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { CalendarSyncStore } from '../../../features/patient/calendar/calendar-sync.store';
import { CalendarSyncService } from '../../../core/services/calendar-sync.service';

/**
 * Context-aware Google Calendar sync button (EP-007 / us_035, AC-1).
 *
 * Renders one of four states based on the current CalendarSyncStore status:
 * - `none`     → "Add to Google Calendar" (primary raised button)
 * - `synced`   → "Update Calendar Event" (stroked secondary button)
 * - `expired`  → "Reconnect Google" (warn flat button — edge case: token expiry)
 * - `pending` / `isSyncing` → disabled spinner button
 *
 * Clicking initiates a full window.location.href redirect to the BE OAuth URL
 * (NOT Angular router.navigate()) so the browser can load Google's consent screen.
 *
 * WCAG 2.2 AA: button has explicit aria-label; disabled state communicated via
 * [disabled] attribute so screen readers announce the unavailable state.
 */
@Component({
  selector: 'app-calendar-sync-button',
  standalone: true,
  imports: [MatButtonModule, MatProgressSpinnerModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (store.isSyncing() || store.syncStatus() === 'pending') {
      <!-- Pending / syncing: disabled spinner (AC-1 edge case) -->
      <button
        mat-raised-button
        disabled
        aria-label="Syncing to Google Calendar, please wait"
        aria-busy="true"
      >
        <mat-spinner diameter="18" strokeWidth="2" />
        Syncing…
      </button>
    } @else if (store.syncStatus() === 'expired') {
      <!-- Token expired — prompt reconnect (edge case) -->
      <button
        mat-flat-button
        color="warn"
        (click)="onSync()"
        aria-label="Your Google connection has expired — reconnect to Google Calendar"
      >
        Reconnect Google
      </button>
    } @else if (store.syncStatus() === 'synced') {
      <!-- Already synced — offer update (duplicate sync edge case; BE upserts) -->
      <button
        mat-stroked-button
        (click)="onSync()"
        aria-label="Update this appointment in Google Calendar"
      >
        Update Calendar Event
      </button>
    } @else {
      <!-- Default: none | failed | declined — show primary add button -->
      <button
        mat-raised-button
        color="primary"
        (click)="onSync()"
        aria-label="Sync appointment to Google Calendar"
      >
        Add to Google Calendar
      </button>
    }
  `,
  styles: [
    `
      :host {
        display: inline-flex;
        align-items: center;
      }

      button mat-spinner {
        display: inline-block;
        margin-right: 0.5rem;
        vertical-align: middle;
      }
    `,
  ],
})
export class CalendarSyncButtonComponent implements OnInit {
  /** UUID of the appointment to sync. Required. */
  @Input({ required: true }) appointmentId!: string;

  protected readonly store = inject(CalendarSyncStore);
  private readonly service = inject(CalendarSyncService);

  ngOnInit(): void {
    // Load the current sync status for this appointment on first render
    // so the button reflects the actual server state (e.g. already synced).
    if (this.store.syncStatus() === 'none') {
      this.store.loadSyncStatus(this.appointmentId);
    }
  }

  /**
   * Initiates the Google Calendar OAuth flow via a full browser redirect.
   * The service uses window.location.href rather than Angular Router.
   */
  protected onSync(): void {
    this.service.initiateGoogleSync(this.appointmentId);
  }
}
