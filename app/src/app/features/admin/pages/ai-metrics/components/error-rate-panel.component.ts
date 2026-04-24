import { Component, Input } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { AiOperationalMetricsSummary } from '../../../models/admin.models';

type ErrorSeverity = 'healthy' | 'warning' | 'critical';

/**
 * Displays the AI error rate percentage with colour-coded severity chip (US_050 / AC-4).
 *
 * Thresholds:
 * - green  (healthy)  : errorRate < 2%
 * - amber  (warning)  : 2% ≤ errorRate ≤ 5%
 * - red    (critical) : errorRate > 5%
 */
@Component({
  selector: 'app-error-rate-panel',
  standalone: true,
  imports: [DecimalPipe, MatCardModule, MatChipsModule],
  template: `
    <mat-card class="metric-card">
      <mat-card-header>
        <mat-card-title>Error Rate</mat-card-title>
        <mat-card-subtitle>Percentage of failed AI requests</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        @if (metrics) {
          <div class="error-rate-value">
            {{ metrics.errorRate | number: '1.1-2' }}%
          </div>
          <span
            class="severity-chip"
            [class.chip-healthy]="severity === 'healthy'"
            [class.chip-warning]="severity === 'warning'"
            [class.chip-critical]="severity === 'critical'"
            [attr.aria-label]="'Error rate severity: ' + severityLabel"
          >
            {{ severityLabel }}
          </span>
        } @else {
          <p class="muted">No data available</p>
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: [
    `
      .metric-card {
        height: 100%;
      }

      mat-card-content {
        padding-top: 16px;
      }

      .error-rate-value {
        font-size: 1.75rem;
        font-weight: 700;
        margin-bottom: 12px;
      }

      .severity-chip {
        display: inline-block;
        border-radius: 16px;
        padding: 4px 14px;
        font-size: 0.8125rem;
        font-weight: 600;
      }

      .chip-healthy {
        background: #e8f5e9;
        color: #1b5e20;
      }
      .chip-warning {
        background: #fff8e1;
        color: #e65100;
      }
      .chip-critical {
        background: #fdecea;
        color: #b71c1c;
      }

      .muted {
        color: var(--mat-sys-on-surface-variant, #555);
        font-style: italic;
        margin: 16px 0 0;
      }
    `,
  ],
})
export class ErrorRatePanelComponent {
  @Input() set metrics(value: AiOperationalMetricsSummary | null) {
    this._metrics = value;
    if (value == null) {
      this.severity = 'healthy';
      this.severityLabel = 'Healthy';
      return;
    }
    if (value.errorRate < 2) {
      this.severity = 'healthy';
      this.severityLabel = 'Healthy';
    } else if (value.errorRate <= 5) {
      this.severity = 'warning';
      this.severityLabel = 'Elevated';
    } else {
      this.severity = 'critical';
      this.severityLabel = 'Critical';
    }
  }
  get metrics(): AiOperationalMetricsSummary | null {
    return this._metrics;
  }

  private _metrics: AiOperationalMetricsSummary | null = null;
  severity: ErrorSeverity = 'healthy';
  severityLabel = 'Healthy';
}
