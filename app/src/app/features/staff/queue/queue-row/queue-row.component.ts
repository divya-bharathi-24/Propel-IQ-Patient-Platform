import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { QueueItem } from '../queue.models';
import { QueueStatusChipComponent } from '../../../../shared/components/queue-status-chip/queue-status-chip.component';

/** Returns today's date string in YYYY-MM-DD (local time zone). */
function todayDateString(): string {
  const d = new Date();
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  const dd = String(d.getDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
}

/**
 * Presentational row component for a single same-day queue entry.
 *
 * Renders 8 columns aligned with the SCR-014 wireframe table header:
 *   Position | Patient | Chief Complaint | Arrival Time | Wait | Risk | Status | Actions
 *
 * - Shows "Mark as Arrived" when arrivalStatus === 'Waiting'.
 * - Shows "Undo Arrived" only when arrivalStatus === 'Arrived'
 *   AND the arrivalTimestamp falls on today (client-side guard;
 *   server enforces same rule for security).
 * - 360° link navigates to patient chart; hidden for anonymous walk-ins (patientId null).
 * - Action buttons carry descriptive aria-labels (WCAG 2.2 AA — 4.1.2).
 *
 * Bug fix: bug_queue_row_column_mismatch — corrects column mismatch where
 * timeSlotStart was displayed under Position and bookingType badge was displayed
 * under Chief Complaint, with Arrival Time, Wait, and Risk columns absent entirely.
 */
@Component({
  selector: 'app-queue-row',
  standalone: true,
  imports: [QueueStatusChipComponent, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <tr class="queue-row" [class.queue-row--high-risk]="item.riskLevel === 'High'">
      <!-- Position -->
      <td class="col-position">#{{ item.queuePosition }}</td>

      <!-- Patient -->
      <td class="col-patient">
        <span class="patient-name">{{ item.patientName }}</span>
        @if (item.bookingType === 'WalkIn') {
          <span class="walkin-tag" aria-label="Booking type: Walk-In">Walk-In</span>
        }
      </td>

      <!-- Chief Complaint -->
      <td class="col-complaint">{{ item.chiefComplaint ?? '—' }}</td>

      <!-- Arrival Time -->
      <td class="col-arrival">{{ item.timeSlotStart }}</td>

      <!-- Wait Time -->
      <td class="col-wait" [class.wait--overdue]="item.waitTime !== null">
        {{ item.waitTime ?? '—' }}
      </td>

      <!-- Risk -->
      <td class="col-risk">
        @if (item.riskLevel) {
          <span class="risk-badge risk-badge--{{ item.riskLevel.toLowerCase() }}"
                [attr.aria-label]="'Risk level: ' + item.riskLevel">
            {{ item.riskLevel }}
          </span>
        } @else {
          <span class="text-muted">—</span>
        }
      </td>

      <!-- Status -->
      <td class="col-status">
        <app-queue-status-chip [status]="item.arrivalStatus" />
      </td>

      <!-- Actions -->
      <td class="col-action">
        <div class="action-group">
          @if (item.arrivalStatus === 'Waiting') {
            <button
              type="button"
              class="btn-action btn-arrived"
              [attr.aria-label]="'Mark ' + item.patientName + ' as arrived'"
              (click)="markArrived.emit(item.appointmentId)"
            >
              Arrived
            </button>
          }
          @if (item.arrivalStatus === 'Arrived' && isArrivedToday) {
            <button
              type="button"
              class="btn-action btn-undo"
              [attr.aria-label]="'Undo arrived for ' + item.patientName"
              (click)="revertArrived.emit(item.appointmentId)"
            >
              Undo
            </button>
          }
          @if (item.patientId) {
            <a
              class="btn-360"
              [routerLink]="['/staff/patients', item.patientId, '360-view']"
              [attr.aria-label]="'Open 360\u00b0 view for ' + item.patientName"
            >
              360°
            </a>
          }
        </div>
      </td>
    </tr>
  `,
  styles: [
    `
      :host {
        display: contents;
      }

      .queue-row td {
        padding: 12px 16px;
        vertical-align: middle;
        border-bottom: 1px solid #e0e0e0;
        font-size: 0.875rem;
        color: #212121;
      }

      .queue-row--high-risk td {
        background-color: #fff8f8;
      }

      .col-position {
        width: 72px;
        font-weight: 700;
        color: #c62828;
      }

      .col-patient .patient-name {
        font-weight: 500;
        color: #1565c0;
        display: block;
      }

      .walkin-tag {
        display: inline-block;
        margin-top: 2px;
        padding: 1px 6px;
        border-radius: 10px;
        font-size: 0.7rem;
        font-weight: 600;
        background-color: #e3f2fd;
        color: #1565c0;
        letter-spacing: 0.02em;
      }

      .col-complaint {
        max-width: 240px;
      }

      .col-arrival,
      .col-wait {
        white-space: nowrap;
        font-variant-numeric: tabular-nums;
        width: 100px;
      }

      .wait--overdue {
        color: #c62828;
        font-weight: 600;
      }

      .col-risk {
        width: 90px;
      }

      .risk-badge {
        display: inline-block;
        padding: 2px 10px;
        border-radius: 12px;
        font-size: 0.75rem;
        font-weight: 600;
        letter-spacing: 0.03em;
      }

      .risk-badge--high {
        background-color: #ffebee;
        color: #c62828;
      }

      .risk-badge--medium {
        background-color: #fff8e1;
        color: #e65100;
      }

      .risk-badge--low {
        background-color: #e8f5e9;
        color: #2e7d32;
      }

      .text-muted {
        color: #9e9e9e;
      }

      .col-status {
        width: 120px;
      }

      .col-action {
        width: 160px;
      }

      .action-group {
        display: flex;
        align-items: center;
        gap: 8px;
        justify-content: flex-end;
      }

      .btn-action {
        padding: 4px 12px;
        border-radius: 4px;
        border: 1px solid transparent;
        font-size: 0.75rem;
        font-weight: 600;
        cursor: pointer;
        transition: background-color 0.15s ease;
      }

      .btn-arrived {
        background-color: #e8f5e9;
        color: #2e7d32;
        border-color: #a5d6a7;
      }

      .btn-arrived:hover {
        background-color: #c8e6c9;
      }

      .btn-undo {
        background-color: #fff8e1;
        color: #f57f17;
        border-color: #ffe082;
      }

      .btn-undo:hover {
        background-color: #fff3cd;
      }

      .btn-360 {
        padding: 4px 8px;
        border-radius: 4px;
        font-size: 0.75rem;
        font-weight: 600;
        color: #1565c0;
        text-decoration: none;
        border: 1px solid #90caf9;
        background-color: #e3f2fd;
        transition: background-color 0.15s ease;
      }

      .btn-360:hover {
        background-color: #bbdefb;
      }
    `,
  ],
})
export class QueueRowComponent {
  @Input({ required: true }) item!: QueueItem;

  @Output() readonly markArrived = new EventEmitter<string>();
  @Output() readonly revertArrived = new EventEmitter<string>();

  /**
   * Returns true when the arrival timestamp falls on today (local time).
   * Prevents staff from undoing arrivals booked on previous days.
   */
  get isArrivedToday(): boolean {
    if (!this.item.arrivalTimestamp) return false;
    const arrivalDate = new Date(this.item.arrivalTimestamp);
    const today = todayDateString();
    const arrivalDay = `${arrivalDate.getFullYear()}-${String(arrivalDate.getMonth() + 1).padStart(2, '0')}-${String(arrivalDate.getDate()).padStart(2, '0')}`;
    return arrivalDay === today;
  }
}
