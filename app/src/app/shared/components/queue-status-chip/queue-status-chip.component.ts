import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { NgClass } from '@angular/common';
import { ArrivalStatus } from '../../../features/staff/queue/queue.models';

const STATUS_CLASS_MAP: Record<ArrivalStatus, string> = {
  Waiting: 'chip-waiting',
  Arrived: 'chip-arrived',
  Cancelled: 'chip-cancelled',
};

const STATUS_LABEL_MAP: Record<ArrivalStatus, string> = {
  Waiting: 'Waiting',
  Arrived: 'Arrived',
  Cancelled: 'Cancelled',
};

/**
 * Displays a coloured pill indicating a queue entry's arrival status.
 * Uses role="status" so screen readers announce state changes (WCAG 2.2 AA — 4.1.3).
 */
@Component({
  selector: 'app-queue-status-chip',
  standalone: true,
  imports: [NgClass],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="status-chip"
      role="status"
      [ngClass]="chipClass"
      [attr.aria-label]="'Arrival status: ' + status"
    >
      {{ label }}
    </span>
  `,
  styles: [
    `
      .status-chip {
        display: inline-block;
        padding: 2px 10px;
        border-radius: 12px;
        font-size: 0.75rem;
        font-weight: 600;
        letter-spacing: 0.03em;
      }

      .chip-waiting {
        background-color: #f5f5f5;
        color: #616161;
      }

      .chip-arrived {
        background-color: #e8f5e9;
        color: #2e7d32;
      }

      .chip-cancelled {
        background-color: #f5f5f5;
        color: #9e9e9e;
        text-decoration: line-through;
      }
    `,
  ],
})
export class QueueStatusChipComponent {
  @Input({ required: true }) status!: ArrivalStatus;

  get chipClass(): string {
    return STATUS_CLASS_MAP[this.status] ?? 'chip-waiting';
  }

  get label(): string {
    return STATUS_LABEL_MAP[this.status] ?? this.status;
  }
}
