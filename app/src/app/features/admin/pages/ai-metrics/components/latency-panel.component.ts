import { Component, Input, computed, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { AiOperationalMetricsSummary } from '../../../models/admin.models';

const TARGET_LATENCY_MS = 30_000;

/**
 * Displays p95 latency with a progress-bar gauge (US_050 / AC-4).
 *
 * - Shows "Insufficient Data" when `p95LatencyMs` is null.
 * - Progress bar turns red (`color="warn"`) when latency exceeds the 30s target.
 * - Progress bar value is capped at 100 to avoid overflow.
 */
@Component({
  selector: 'app-latency-panel',
  standalone: true,
  imports: [DecimalPipe, MatCardModule, MatProgressBarModule],
  template: `
    <mat-card class="metric-card">
      <mat-card-header>
        <mat-card-title>p95 Latency</mat-card-title>
        <mat-card-subtitle>Target: ≤30 seconds</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        @if (metrics !== null && metrics.p95LatencyMs !== null) {
          <div class="latency-value">
            {{ metrics.p95LatencyMs | number: '1.0-0' }} ms
          </div>
          <mat-progress-bar
            [value]="p95Percent"
            [color]="isOverTarget ? 'warn' : 'primary'"
            aria-label="p95 latency progress"
          />
          <small class="target-label">30,000 ms target</small>
        } @else {
          <p class="muted">Insufficient Data</p>
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

      .latency-value {
        font-size: 1.5rem;
        font-weight: 600;
        margin-bottom: 12px;
      }

      mat-progress-bar {
        margin-bottom: 8px;
      }

      .target-label {
        color: var(--mat-sys-on-surface-variant, #555);
        font-size: 0.75rem;
      }

      .muted {
        color: var(--mat-sys-on-surface-variant, #555);
        font-style: italic;
        margin: 16px 0 0;
      }
    `,
  ],
})
export class LatencyPanelComponent {
  @Input() set metrics(value: AiOperationalMetricsSummary | null) {
    this._metrics = value;
    this._computeValues();
  }
  get metrics(): AiOperationalMetricsSummary | null {
    return this._metrics;
  }

  private _metrics: AiOperationalMetricsSummary | null = null;

  p95Percent = 0;
  isOverTarget = false;

  private _computeValues(): void {
    if (this._metrics?.p95LatencyMs != null) {
      this.p95Percent = Math.min(
        (this._metrics.p95LatencyMs / TARGET_LATENCY_MS) * 100,
        100,
      );
      this.isOverTarget = this._metrics.p95LatencyMs > TARGET_LATENCY_MS;
    } else {
      this.p95Percent = 0;
      this.isOverTarget = false;
    }
  }
}
