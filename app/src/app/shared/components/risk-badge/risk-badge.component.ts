import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { NgClass } from '@angular/common';

/** Risk severity levels returned by the no-show risk engine. */
export type RiskSeverity = 'Low' | 'Medium' | 'High';

const SEVERITY_CLASS_MAP: Partial<Record<RiskSeverity, string>> = {
  Low: 'risk-low',
  Medium: 'risk-medium',
  High: 'risk-high',
};

/**
 * Displays a colour-coded risk badge chip for no-show risk severity.
 *
 * - `severity = null` renders a neutral grey "Pending" chip (calculation job not yet run).
 * - `score` appears in the tooltip as a percentage value.
 * - WCAG 2.2 AA: colour is supplementary; the text label is always visible,
 *   and `aria-label` carries the full risk description for screen readers (role="status").
 *
 * AC-2: colour-coded badge — green Low / amber Medium / red High.
 * AC-4: purely data-driven; re-renders automatically when `severity` input changes.
 */
@Component({
  selector: 'app-risk-badge',
  standalone: true,
  imports: [NgClass],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="risk-badge"
      role="status"
      [ngClass]="badgeClass"
      [title]="tooltipText"
      [attr.aria-label]="ariaLabel"
    >
      {{ severity ?? 'Pending' }}
    </span>
  `,
  styles: [
    `
      .risk-badge {
        display: inline-block;
        padding: 2px 10px;
        border-radius: 12px;
        font-size: 0.75rem;
        font-weight: 600;
        letter-spacing: 0.03em;
        color: #fff;
      }

      .risk-low {
        background-color: #2e7d32;
      }

      .risk-medium {
        background-color: #f57c00;
      }

      .risk-high {
        background-color: #c62828;
      }

      .risk-pending {
        background-color: #9e9e9e;
      }
    `,
  ],
})
export class RiskBadgeComponent {
  /** Severity from API — null when the calculation job has not yet run. */
  @Input() severity: RiskSeverity | null = null;

  /** Raw 0-1 score from API — displayed as a percentage in the tooltip. */
  @Input() score: number | null = null;

  get badgeClass(): string {
    return (
      (this.severity ? SEVERITY_CLASS_MAP[this.severity] : undefined) ??
      'risk-pending'
    );
  }

  get tooltipText(): string {
    if (this.severity === null) {
      return 'Risk score not yet calculated';
    }
    const pct =
      this.score !== null ? `${Math.round(this.score * 100)}%` : 'N/A';
    return `Risk score: ${pct}`;
  }

  get ariaLabel(): string {
    if (this.severity === null) {
      return 'No-show risk: Pending, score not yet calculated';
    }
    const pct =
      this.score !== null ? `${Math.round(this.score * 100)}%` : 'N/A';
    return `No-show risk: ${this.severity}, score ${pct}`;
  }
}
