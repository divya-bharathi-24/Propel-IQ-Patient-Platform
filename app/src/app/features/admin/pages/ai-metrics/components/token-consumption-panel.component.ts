import { Component, Input } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { AiOperationalMetricsSummary } from '../../../models/admin.models';

/**
 * Displays average token consumption per request (US_050 / AC-4).
 *
 * Shows avgPromptTokens, avgResponseTokens, and the combined total.
 */
@Component({
  selector: 'app-token-consumption-panel',
  standalone: true,
  imports: [DecimalPipe, MatCardModule],
  template: `
    <mat-card class="metric-card">
      <mat-card-header>
        <mat-card-title>Token Consumption</mat-card-title>
        <mat-card-subtitle>Average per request</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        @if (metrics) {
          <dl class="token-list">
            <div class="token-row">
              <dt>Prompt tokens</dt>
              <dd>{{ metrics.avgPromptTokens | number: '1.0-0' }}</dd>
            </div>
            <div class="token-row">
              <dt>Response tokens</dt>
              <dd>{{ metrics.avgResponseTokens | number: '1.0-0' }}</dd>
            </div>
            <div class="token-row token-total">
              <dt>Combined total</dt>
              <dd>{{ combined | number: '1.0-0' }}</dd>
            </div>
          </dl>
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

      .token-list {
        margin: 0;
        display: flex;
        flex-direction: column;
        gap: 8px;
      }

      .token-row {
        display: flex;
        justify-content: space-between;
        font-size: 0.9375rem;
      }

      .token-total {
        border-top: 1px solid var(--mat-sys-outline-variant, #e0e0e0);
        padding-top: 8px;
        font-weight: 600;
      }

      dt {
        color: var(--mat-sys-on-surface-variant, #555);
      }

      .muted {
        color: var(--mat-sys-on-surface-variant, #555);
        font-style: italic;
        margin: 16px 0 0;
      }
    `,
  ],
})
export class TokenConsumptionPanelComponent {
  @Input() set metrics(value: AiOperationalMetricsSummary | null) {
    this._metrics = value;
    this.combined = value ? value.avgPromptTokens + value.avgResponseTokens : 0;
  }
  get metrics(): AiOperationalMetricsSummary | null {
    return this._metrics;
  }

  private _metrics: AiOperationalMetricsSummary | null = null;
  combined = 0;
}
