import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MedicalCodeReviewStore } from '../../store/medical-code-review.store';

/**
 * CodeReviewProgressComponent — US_043 (Edge Case: Partial Submission)
 *
 * Persistent progress indicator displayed at the top of the review page.
 * Shows "X of N codes reviewed" and a determinate progress bar that updates
 * reactively as staff confirm or reject codes.
 *
 * A code is considered "reviewed" when its decision status is Accepted or Rejected.
 * Codes that remain in the Pending state are counted as not yet reviewed.
 *
 * Partial submission is allowed — this component informs staff of remaining
 * decisions without blocking the Submit action.
 *
 * WCAG 2.2 AA:
 *  - `mat-progress-bar` has an `aria-label` for screen readers.
 *  - The textual label duplicates the progress value in text form (not colour-only).
 *  - `role="status"` ensures screen readers announce updates politely.
 */
@Component({
  selector: 'app-code-review-progress',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatProgressBarModule],
  template: `
    <div class="progress-container" role="status" aria-live="polite">
      <p class="progress-label" [attr.aria-label]="progressAriaLabel">
        {{ reviewedCount }} of {{ store.totalCount() }} codes reviewed
        @if (store.pendingCount() > 0) {
          <span class="pending-note">
            — {{ store.pendingCount() }} still pending
          </span>
        }
      </p>

      <mat-progress-bar
        mode="determinate"
        [value]="progressPercent"
        [attr.aria-label]="progressAriaLabel"
        [attr.aria-valuenow]="progressPercent"
        aria-valuemin="0"
        aria-valuemax="100"
      />
    </div>
  `,
  styles: [
    `
      .progress-container {
        padding: 12px 0;
      }

      .progress-label {
        margin: 0 0 6px;
        font-size: 0.875rem;
        font-weight: 500;
        color: rgba(0, 0, 0, 0.7);
      }

      .pending-note {
        font-weight: 400;
        color: #f57c00;
      }
    `,
  ],
})
export class CodeReviewProgressComponent {
  protected readonly store = inject(MedicalCodeReviewStore);

  protected get reviewedCount(): number {
    return this.store.totalCount() - this.store.pendingCount();
  }

  protected get progressPercent(): number {
    const total = this.store.totalCount();
    if (total === 0) return 0;
    return Math.round((this.reviewedCount / total) * 100);
  }

  protected get progressAriaLabel(): string {
    return `${this.reviewedCount} of ${this.store.totalCount()} codes reviewed, ${this.progressPercent}%`;
  }
}
