import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { NgClass } from '@angular/common';
import { BookingType } from '../../../features/staff/queue/queue.models';

const TYPE_CLASS_MAP: Record<BookingType, string> = {
  SelfBooked: 'badge-self-booked',
  WalkIn: 'badge-walk-in',
};

const TYPE_LABEL_MAP: Record<BookingType, string> = {
  SelfBooked: 'Self-Booked',
  WalkIn: 'Walk-In',
};

/**
 * Displays a coloured pill indicating whether an appointment was self-booked
 * or a walk-in registration.
 */
@Component({
  selector: 'app-booking-type-badge',
  standalone: true,
  imports: [NgClass],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="booking-badge"
      [ngClass]="badgeClass"
      [attr.aria-label]="'Booking type: ' + label"
    >
      {{ label }}
    </span>
  `,
  styles: [
    `
      .booking-badge {
        display: inline-block;
        padding: 2px 10px;
        border-radius: 12px;
        font-size: 0.75rem;
        font-weight: 600;
        letter-spacing: 0.03em;
      }

      .badge-self-booked {
        background-color: #e3f2fd;
        color: #1565c0;
      }

      .badge-walk-in {
        background-color: #fff8e1;
        color: #f57f17;
      }
    `,
  ],
})
export class BookingTypeBadgeComponent {
  @Input({ required: true }) bookingType!: BookingType;

  get badgeClass(): string {
    return TYPE_CLASS_MAP[this.bookingType] ?? 'badge-self-booked';
  }

  get label(): string {
    return TYPE_LABEL_MAP[this.bookingType] ?? this.bookingType;
  }
}
