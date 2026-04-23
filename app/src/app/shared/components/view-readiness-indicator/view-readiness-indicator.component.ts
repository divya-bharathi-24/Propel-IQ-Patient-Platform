import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { NgClass } from '@angular/common';

@Component({
  selector: 'app-view-readiness-indicator',
  standalone: true,
  imports: [NgClass],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="readiness-badge"
      [ngClass]="verified ? 'badge-ready' : 'badge-pending'"
      [attr.aria-label]="
        verified
          ? '360 degree view is ready'
          : '360 degree view is pending staff verification'
      "
      role="status"
    >
      @if (verified) {
        <span class="badge-icon" aria-hidden="true">✓</span>
        360° View Ready
      } @else {
        <span class="badge-icon" aria-hidden="true">⏳</span>
        Pending Staff Verification
      }
    </span>
  `,
  styles: [
    `
      .readiness-badge {
        display: inline-flex;
        align-items: center;
        gap: 6px;
        padding: 6px 14px;
        border-radius: 16px;
        font-size: 0.875rem;
        font-weight: 600;
      }

      .badge-ready {
        background-color: #e8f5e9;
        color: #2e7d32;
      }

      .badge-pending {
        background-color: #fff8e1;
        color: #f57f17;
      }

      .badge-icon {
        font-size: 1rem;
      }
    `,
  ],
})
export class ViewReadinessIndicatorComponent {
  @Input({ required: true }) verified!: boolean;
}
