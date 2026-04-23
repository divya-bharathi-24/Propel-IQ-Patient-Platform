import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { StaffAppointmentStore } from '../../state/staff-appointment.store';
import { SendReminderNowButtonComponent } from './send-reminder-now-button/send-reminder-now-button.component';

/**
 * Appointment detail page for staff members (US-034).
 *
 * Displays full appointment metadata and the manual ad-hoc reminder panel
 * with four distinct UI states:
 *  - idle    : "Send Reminder Now" button active
 *  - loading : button disabled with spinner during API call
 *  - success : inline confirmation with timestamp and staff name (AC-3)
 *  - error   : failure reason with Retry button (AC-4)
 *  - cooldown: inline warning with minutes remaining until retry allowed
 *
 * Accessibility (WCAG 2.2 AA):
 * - All reminder feedback panels are wrapped in `aria-live="polite"` so
 *   assistive technologies announce state transitions.
 * - Colour is never the sole indicator; text labels accompany all states.
 * - The back link has a visible focus ring.
 *
 * Route: /staff/appointments/:id — protected by authGuard + staffGuard.
 */
@Component({
  selector: 'app-appointment-detail',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatProgressSpinnerModule,
    SendReminderNowButtonComponent,
  ],
  templateUrl: './appointment-detail.component.html',
  styleUrl: './appointment-detail.component.scss',
})
export class AppointmentDetailComponent implements OnInit {
  protected readonly store = inject(StaffAppointmentStore);
  private readonly route = inject(ActivatedRoute);

  /** The UUID extracted from the route parameter. */
  protected get appointmentId(): string {
    return this.route.snapshot.paramMap.get('id') ?? '';
  }

  ngOnInit(): void {
    const id = this.appointmentId;
    if (id) {
      this.store.loadAppointmentById(id);
    }
  }

  protected onSendReminderClicked(): void {
    this.store.triggerReminder(this.appointmentId);
  }

  protected onRetry(): void {
    this.store.triggerReminder(this.appointmentId);
  }

  /**
   * Converts `cooldownSecondsRemaining` to whole minutes (rounded up)
   * for display in the cooldown panel.
   */
  protected cooldownMinutes(): number {
    return Math.ceil(this.store.cooldownSecondsRemaining() / 60);
  }
}
