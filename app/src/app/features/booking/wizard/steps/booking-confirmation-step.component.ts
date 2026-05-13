import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { BookingWizardStore } from '../booking-wizard.store';

@Component({
  selector: 'app-booking-confirmation-step',
  standalone: true,
  imports: [
    RouterLink,
    MatButtonModule,
    MatProgressSpinnerModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="confirmation-step" aria-label="Step 4: Booking confirmed">
      @if (result()) {
        <div
          class="confirmation-card"
          role="region"
          aria-label="Booking confirmation details"
        >
          <div class="check-icon" aria-hidden="true">✓</div>

          <h2 class="confirmation-heading">Booking Confirmed!</h2>

          <dl class="detail-list">
            <div class="detail-row">
              <dt>Reference</dt>
              <dd class="reference-number">{{ result()!.referenceNumber }}</dd>
            </div>
            <div class="detail-row">
              <dt>Date</dt>
              <dd>{{ formattedDate() }}</dd>
            </div>
            <div class="detail-row">
              <dt>Time</dt>
              <dd>{{ result()!.timeSlotStart }}</dd>
            </div>
            <div class="detail-row">
              <dt>Specialty</dt>
              <dd>{{ result()!.specialtyName }}</dd>
            </div>
          </dl>

          <div class="actions">
            <button
              mat-stroked-button
              (click)="downloadCalendar()"
              aria-label="Download ICS calendar file"
            >
              Download ICS
            </button>

            <a
              mat-flat-button
              color="primary"
              routerLink="/dashboard"
              aria-label="Return to your dashboard"
            >
              Back to Dashboard
            </a>
          </div>
        </div>
      } @else if (store.isSubmitting()) {
        <div class="loading-state" role="status" aria-live="polite">
          <mat-spinner diameter="40" aria-label="Confirming your booking" />
          <p class="loading-text">Confirming your booking…</p>
        </div>
      } @else {
        <p class="no-result" role="status">Loading confirmation details…</p>
      }
    </section>
  `,
  styles: [
    `
      .confirmation-step {
        display: flex;
        justify-content: center;
      }

      .confirmation-card {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: 1.5rem;
        padding: 2rem;
        border: 1px solid #e0e0e0;
        border-radius: 12px;
        max-width: 480px;
        width: 100%;
        text-align: center;
      }

      .check-icon {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 56px;
        height: 56px;
        border-radius: 50%;
        background-color: #e8f5e9;
        color: #1b5e20;
        font-size: 1.75rem;
        font-weight: 700;
      }

      .confirmation-heading {
        margin: 0;
        font-size: 1.5rem;
        font-weight: 700;
        color: #1b5e20;
      }

      .detail-list {
        width: 100%;
        margin: 0;
        display: flex;
        flex-direction: column;
        gap: 0.75rem;
        text-align: left;
      }

      .detail-row {
        display: flex;
        justify-content: space-between;
        align-items: baseline;
        border-bottom: 1px solid #f5f5f5;
        padding-bottom: 0.5rem;

        dt {
          color: #5f6368;
          font-size: 0.875rem;
        }

        dd {
          margin: 0;
          font-weight: 500;
        }
      }

      .reference-number {
        font-family: monospace;
        font-size: 1rem;
        letter-spacing: 0.05em;
      }

      .actions {
        display: flex;
        gap: 0.75rem;
        flex-wrap: wrap;
        justify-content: center;
      }

      .no-result {
        color: #5f6368;
        font-size: 0.875rem;
      }

      .loading-state {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: 1rem;
        padding: 2rem;
      }

      .loading-text {
        color: #5f6368;
        font-size: 0.9rem;
        margin: 0;
      }
    `,
  ],
})
export class BookingConfirmationStepComponent {
  protected readonly store = inject(BookingWizardStore);
  private readonly router = inject(Router);

  protected readonly result = computed(() => this.store.bookingResult());

  protected readonly formattedDate = computed(() => {
    const date = this.result()?.date;
    if (!date) return '';
    return new Date(date).toLocaleDateString('en-AU', {
      weekday: 'long',
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  });

  /** Generates a .ics data URI and triggers a browser download. */
  protected downloadCalendar(): void {
    const r = this.result();
    if (!r) return;

    // Build compact date-time strings for iCalendar (YYYYMMDDTHHMMSSZ)
    const dtStart = this.toIcsDateTime(r.date, r.timeSlotStart);
    // Default end to 30 min after start if not available
    const dtEnd = r.timeSlotEnd
      ? this.toIcsDateTime(r.date, r.timeSlotEnd)
      : dtStart;

    const ics = [
      'BEGIN:VCALENDAR',
      'VERSION:2.0',
      'PRODID:-//Propel IQ//Patient Platform//EN',
      'BEGIN:VEVENT',
      `SUMMARY:Appointment - ${r.specialtyName}`,
      `DTSTART:${dtStart}`,
      `DTEND:${dtEnd}`,
      `DESCRIPTION:Reference: ${r.referenceNumber}`,
      'END:VEVENT',
      'END:VCALENDAR',
    ].join('\r\n');

    const blob = new Blob([ics], { type: 'text/calendar' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `appointment-${r.referenceNumber}.ics`;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    URL.revokeObjectURL(url);
  }

  /**
   * Converts a date string "YYYY-MM-DD" and time "HH:mm" into
   * iCalendar UTC format "YYYYMMDDTHHMMSSZ".
   */
  private toIcsDateTime(date: string, time: string): string {
    const [year, month, day] = date.split('-');
    const [hour, minute] = time.split(':');
    return `${year}${month}${day}T${hour}${minute}00Z`;
  }
}
