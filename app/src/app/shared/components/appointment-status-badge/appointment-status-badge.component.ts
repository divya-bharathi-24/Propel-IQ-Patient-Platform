import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { NgClass } from '@angular/common';
import { AppointmentStatus } from '../../../features/patient/dashboard/patient-dashboard.model';

const STATUS_CLASS_MAP: Record<AppointmentStatus, string> = {
  Booked: 'status-booked',
  Arrived: 'status-arrived',
  Completed: 'status-completed',
  Cancelled: 'status-cancelled',
};

@Component({
  selector: 'app-appointment-status-badge',
  standalone: true,
  imports: [NgClass],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="status-badge"
      [ngClass]="statusClass"
      [attr.aria-label]="'Appointment status: ' + status"
    >
      {{ status }}
    </span>
  `,
  styles: [
    `
      .status-badge {
        display: inline-block;
        padding: 2px 10px;
        border-radius: 12px;
        font-size: 0.75rem;
        font-weight: 600;
        letter-spacing: 0.03em;
      }

      .status-booked {
        background-color: #e3f2fd;
        color: #1565c0;
      }

      .status-arrived {
        background-color: #e8f5e9;
        color: #2e7d32;
      }

      .status-completed {
        background-color: #f3e5f5;
        color: #6a1b9a;
      }

      .status-cancelled {
        background-color: #fce4ec;
        color: #b71c1c;
      }
    `,
  ],
})
export class AppointmentStatusBadgeComponent {
  @Input({ required: true }) status!: AppointmentStatus;

  get statusClass(): string {
    return STATUS_CLASS_MAP[this.status] ?? 'status-booked';
  }
}
