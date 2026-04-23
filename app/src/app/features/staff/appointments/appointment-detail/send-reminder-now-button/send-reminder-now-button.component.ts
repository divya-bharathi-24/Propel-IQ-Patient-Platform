import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

/**
 * Reusable "Send Reminder Now" button for the appointment detail view (US-034, AC-1).
 *
 * Handles the idle and loading visual states internally; the parent is
 * responsible for disabling the button during cooldown or when the appointment
 * is in a state that disallows reminders.
 *
 * Accessibility (WCAG 2.2 AA):
 * - `aria-busy="true"` is set during loading so assistive technologies
 *   announce the in-progress state.
 * - The spinner is `aria-hidden` to avoid duplicate announcements; the
 *   parent's `aria-live="polite"` region is the primary announcement channel.
 * - The button label changes to "Sending…" during loading so colour is never
 *   the sole state indicator (WCAG 1.4.1).
 */
@Component({
  selector: 'app-send-reminder-now-button',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatProgressSpinnerModule],
  template: `
    <button
      mat-raised-button
      color="primary"
      class="send-reminder-btn"
      [disabled]="disabled || isLoading"
      [attr.aria-busy]="isLoading ? 'true' : null"
      aria-label="Send an immediate reminder to the patient"
      (click)="onButtonClick()"
    >
      @if (isLoading) {
        <mat-spinner diameter="18" class="btn-spinner" aria-hidden="true" />
        <span class="btn-label">Sending…</span>
      } @else {
        Send Reminder Now
      }
    </button>
  `,
  styles: [
    `
      .send-reminder-btn {
        display: inline-flex;
        align-items: center;
        gap: 8px;
      }

      .btn-spinner {
        flex-shrink: 0;
      }

      .btn-label {
        font-size: 0.875rem;
      }
    `,
  ],
})
export class SendReminderNowButtonComponent {
  /** Whether the button should appear in its loading/spinning state. */
  @Input() isLoading = false;

  /**
   * Whether the button should be fully disabled (e.g. during a cooldown
   * period or when the appointment status prohibits reminders).
   */
  @Input() disabled = false;

  /** Emitted when the button is clicked in a non-disabled, non-loading state. */
  @Output() readonly sendClicked = new EventEmitter<void>();

  protected onButtonClick(): void {
    this.sendClicked.emit();
  }
}
