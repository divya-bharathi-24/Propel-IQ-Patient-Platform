import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
} from '@angular/core';
import { QueueItem } from '../queue.models';
import { QueueStatusChipComponent } from '../../../../shared/components/queue-status-chip/queue-status-chip.component';
import { BookingTypeBadgeComponent } from '../../../../shared/components/booking-type-badge/booking-type-badge.component';

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
 * - Shows "Mark as Arrived" when arrivalStatus === 'Waiting'.
 * - Shows "Undo Arrived" only when arrivalStatus === 'Arrived'
 *   AND the arrivalTimestamp falls on today (client-side guard;
 *   server enforces same rule for security).
 * - Action buttons carry descriptive aria-labels (WCAG 2.2 AA — 4.1.2).
 */
@Component({
  selector: 'app-queue-row',
  standalone: true,
  imports: [QueueStatusChipComponent, BookingTypeBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <tr class="queue-row">
      <td class="col-time">{{ item.timeSlotStart }}</td>
      <td class="col-name">{{ item.patientName }}</td>
      <td class="col-type">
        <app-booking-type-badge [bookingType]="item.bookingType" />
      </td>
      <td class="col-status">
        <app-queue-status-chip [status]="item.arrivalStatus" />
      </td>
      <td class="col-action">
        @if (item.arrivalStatus === 'Waiting') {
          <button
            type="button"
            class="btn-action btn-arrived"
            [attr.aria-label]="'Mark ' + item.patientName + ' as arrived'"
            (click)="markArrived.emit(item.appointmentId)"
          >
            Mark as Arrived
          </button>
        }
        @if (item.arrivalStatus === 'Arrived' && isArrivedToday) {
          <button
            type="button"
            class="btn-action btn-undo"
            [attr.aria-label]="'Undo arrived for ' + item.patientName"
            (click)="revertArrived.emit(item.appointmentId)"
          >
            Undo Arrived
          </button>
        }
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

      .col-time {
        white-space: nowrap;
        font-variant-numeric: tabular-nums;
        width: 80px;
      }

      .col-name {
        font-weight: 500;
      }

      .col-type,
      .col-status {
        width: 120px;
      }

      .col-action {
        width: 160px;
        text-align: right;
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
