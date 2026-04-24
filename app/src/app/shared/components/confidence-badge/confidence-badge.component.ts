import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { NgClass } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';

/**
 * Reusable confidence badge for AI-extracted clinical fields (US_041, AC-2).
 *
 * Renders a coloured chip showing the confidence percentage.
 * For confidence below 80% a warning icon is added alongside the colour change
 * so that the indicator is not conveyed by colour alone (WCAG 2.2 AA — 1.4.1 Use of Color).
 *
 * The `aria-label` carries the full text description so screen readers receive
 * the confidence value and the low-confidence warning without relying on visuals.
 */
@Component({
  selector: 'app-confidence-badge',
  standalone: true,
  imports: [NgClass, MatIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="confidence-badge"
      [ngClass]="badgeClass"
      [attr.aria-label]="ariaLabel"
      role="status"
    >
      @if (isLowConfidence) {
        <mat-icon class="badge-icon" aria-hidden="true">warning</mat-icon>
      }
      {{ percentLabel }}
    </span>
  `,
  styles: [
    `
      .confidence-badge {
        display: inline-flex;
        align-items: center;
        gap: 2px;
        padding: 2px 8px;
        border-radius: 12px;
        font-size: 0.72rem;
        font-weight: 600;
        letter-spacing: 0.02em;
        color: #fff;
      }

      .badge-high {
        background-color: #2e7d32;
      }

      .badge-low {
        background-color: #b71c1c;
      }

      .badge-icon {
        font-size: 14px;
        height: 14px;
        width: 14px;
        line-height: 14px;
        vertical-align: middle;
      }
    `,
  ],
})
export class ConfidenceBadgeComponent {
  /** Confidence score from the AI extraction pipeline. Range 0–1. */
  @Input() confidence = 0;

  protected get isLowConfidence(): boolean {
    return this.confidence < 0.8;
  }

  protected get badgeClass(): string {
    return this.isLowConfidence ? 'badge-low' : 'badge-high';
  }

  protected get percentLabel(): string {
    return `${(this.confidence * 100).toFixed(0)}%`;
  }

  protected get ariaLabel(): string {
    const base = `Confidence: ${this.percentLabel}`;
    return this.isLowConfidence
      ? `${base} — low confidence, priority review required`
      : base;
  }
}
