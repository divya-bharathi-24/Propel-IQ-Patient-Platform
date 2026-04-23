import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { NgClass } from '@angular/common';
import {
  InsuranceCheckResult,
  InsuranceStatus,
} from '../../models/insurance.models';

/** CSS modifier classes keyed by InsuranceStatus. */
const STATUS_CLASS_MAP: Record<InsuranceStatus, string> = {
  Verified: 'badge--verified',
  NotRecognized: 'badge--not-recognized',
  Incomplete: 'badge--incomplete',
  CheckPending: 'badge--check-pending',
};

/** Icon prefix keyed by InsuranceStatus. */
const STATUS_ICON_MAP: Record<InsuranceStatus, string> = {
  Verified: '✓',
  NotRecognized: '⚠',
  Incomplete: '⚠',
  CheckPending: 'ℹ',
};

/**
 * Reusable insurance pre-check status badge (FR-039).
 * Used in the booking wizard Step 3 and on the patient dashboard (AC-4).
 *
 * Colour coding:
 *   Verified       → green
 *   NotRecognized  → amber
 *   Incomplete     → amber
 *   CheckPending   → blue/info
 */
@Component({
  selector: 'app-insurance-status-badge',
  standalone: true,
  imports: [NgClass],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="insurance-badge"
      [ngClass]="badgeClass"
      role="status"
      aria-live="polite"
      aria-atomic="true"
      [attr.aria-label]="'Insurance status: ' + result.status"
    >
      <span class="badge-icon" aria-hidden="true">{{ icon }}</span>
      <span class="badge-label">{{ label }}</span>
    </div>
    @if (result.guidance) {
      <p
        class="badge-guidance"
        aria-describedby="insurance-badge-guidance"
        id="insurance-badge-guidance"
      >
        {{ result.guidance }}
      </p>
    }
  `,
  styles: [
    `
      .insurance-badge {
        display: inline-flex;
        align-items: center;
        gap: 0.375rem;
        padding: 0.25rem 0.75rem;
        border-radius: 12px;
        font-size: 0.8rem;
        font-weight: 600;
        max-width: fit-content;

        &.badge--verified {
          background-color: #e8f5e9;
          color: #1b5e20;
        }

        &.badge--not-recognized,
        &.badge--incomplete {
          background-color: #fff8e1;
          color: #7c5700;
        }

        &.badge--check-pending {
          background-color: #e3f2fd;
          color: #0d47a1;
        }
      }

      .badge-icon {
        font-size: 0.9rem;
      }

      .badge-label {
        white-space: nowrap;
      }

      .badge-guidance {
        margin: 0.5rem 0 0;
        font-size: 0.8125rem;
        color: #5f6368;
      }
    `,
  ],
})
export class InsuranceStatusBadgeComponent {
  @Input({ required: true }) result!: InsuranceCheckResult;

  get badgeClass(): string {
    return STATUS_CLASS_MAP[this.result.status] ?? '';
  }

  get icon(): string {
    return STATUS_ICON_MAP[this.result.status] ?? '';
  }

  get label(): string {
    switch (this.result.status) {
      case 'Verified':
        return 'Verified';
      case 'NotRecognized':
        return 'Not Recognized';
      case 'Incomplete':
        return 'Incomplete';
      case 'CheckPending':
        return 'Insurance Check Pending';
    }
  }
}
