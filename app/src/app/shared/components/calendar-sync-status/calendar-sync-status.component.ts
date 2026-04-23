import {
  ChangeDetectionStrategy,
  Component,
  Input,
  inject,
} from '@angular/core';
import { MatChipsModule } from '@angular/material/chips';
import { MatButtonModule } from '@angular/material/button';
import { CalendarSyncStore } from '../../../features/patient/calendar/calendar-sync.store';
import { CalendarSyncService } from '../../../core/services/calendar-sync.service';

/**
 * Displays the Google Calendar sync status badge for an appointment (AC-2 – AC-4).
 *
 * Rendered states:
 * - `synced`   → green chip "Synced" + external link to Google Calendar event (AC-2)
 * - `failed`   → red chip "Sync failed" + ICS download button + Retry button (AC-4)
 * - `declined` → amber chip "Not connected" + guidance text (AC-3)
 * - `none` / `pending` → renders nothing (button alone is sufficient)
 * - `expired`  → renders nothing (CalendarSyncButtonComponent handles the prompt)
 *
 * Security:
 * OWASP A05 — external Google Calendar event links include rel="noopener noreferrer"
 * and target="_blank" to prevent tab-napping.
 *
 * Accessibility (WCAG 2.2 AA):
 * Status chips include text labels alongside color; external link aria-label
 * describes the destination explicitly.
 */
@Component({
  selector: 'app-calendar-sync-status',
  standalone: true,
  imports: [MatChipsModule, MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (store.syncStatus() === 'synced') {
      <!-- AC-2: Synced — green badge + event link -->
      <div class="sync-status" role="status" aria-live="polite">
        <mat-chip-set aria-label="Calendar sync status">
          <mat-chip
            class="chip-synced"
            [attr.aria-label]="'Calendar sync status: Synced'"
          >
            ✓ Synced to Google Calendar
          </mat-chip>
        </mat-chip-set>

        @if (store.eventLink()) {
          <a
            [href]="store.eventLink()"
            target="_blank"
            rel="noopener noreferrer"
            class="event-link"
            aria-label="View this appointment in Google Calendar (opens in new tab)"
          >
            View in Google Calendar
          </a>
        }
      </div>
    }

    @if (store.syncStatus() === 'failed') {
      <!-- AC-4: Failed — red badge + ICS download + Retry -->
      <div class="sync-status" role="alert" aria-live="assertive">
        <mat-chip-set aria-label="Calendar sync status">
          <mat-chip
            class="chip-failed"
            [attr.aria-label]="'Calendar sync status: Sync failed'"
          >
            ✕ Sync failed
          </mat-chip>
        </mat-chip-set>

        <div class="sync-actions">
          <button
            mat-stroked-button
            (click)="onDownloadIcs()"
            aria-label="Download ICS calendar file for this appointment"
          >
            Download ICS
          </button>

          <button
            mat-flat-button
            color="primary"
            (click)="onRetry()"
            aria-label="Retry syncing this appointment to Google Calendar"
          >
            Retry
          </button>
        </div>
      </div>
    }

    @if (store.syncStatus() === 'declined') {
      <!-- AC-3: Declined — amber badge + guidance text -->
      <div class="sync-status" role="status" aria-live="polite">
        <mat-chip-set aria-label="Calendar sync status">
          <mat-chip
            class="chip-declined"
            [attr.aria-label]="'Calendar sync status: Not connected'"
          >
            ⚠ Not connected
          </mat-chip>
        </mat-chip-set>

        <p class="guidance-text">
          To add to Google Calendar, click "Add to Google Calendar" and approve
          the calendar permission request.
        </p>
      </div>
    }
  `,
  styles: [
    `
      .sync-status {
        display: flex;
        flex-direction: column;
        gap: 0.5rem;
        margin-top: 0.75rem;
      }

      /* Synced — green */
      .chip-synced {
        background-color: #e8f5e9;
        color: #1b5e20;
        font-weight: 600;
      }

      /* Failed — red */
      .chip-failed {
        background-color: #fce4ec;
        color: #b71c1c;
        font-weight: 600;
      }

      /* Declined — amber */
      .chip-declined {
        background-color: #fff8e1;
        color: #e65100;
        font-weight: 600;
      }

      .event-link {
        font-size: 0.875rem;
        color: #1565c0;
        text-decoration: underline;
      }

      .sync-actions {
        display: flex;
        gap: 0.5rem;
        flex-wrap: wrap;
      }

      .guidance-text {
        margin: 0;
        font-size: 0.875rem;
        color: #5f6368;
      }
    `,
  ],
})
export class CalendarSyncStatusComponent {
  /** UUID of the appointment this status is displayed for. */
  @Input({ required: true }) appointmentId!: string;

  protected readonly store = inject(CalendarSyncStore);
  private readonly service = inject(CalendarSyncService);

  /** Triggers browser-native ICS download via anchor element (AC-4). */
  protected onDownloadIcs(): void {
    this.service.downloadIcs(this.appointmentId);
  }

  /**
   * Re-initiates the OAuth flow.
   * The BE will upsert the event to prevent duplicates.
   */
  protected onRetry(): void {
    this.service.retrySyncRelink(this.appointmentId);
  }
}
