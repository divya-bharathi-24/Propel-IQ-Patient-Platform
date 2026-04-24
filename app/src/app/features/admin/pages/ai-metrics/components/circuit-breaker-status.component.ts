import { Component, Input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { AiOperationalMetricsSummary } from '../../../models/admin.models';

/**
 * Displays the AI circuit breaker status (US_050 / AC-4).
 *
 * - `cbOpen = true`  → prominent warn banner with trip count and "OPEN — Manual Review Required".
 * - `cbOpen = false` → green "CLOSED — Healthy" badge.
 */
@Component({
  selector: 'app-circuit-breaker-status',
  standalone: true,
  imports: [MatCardModule, MatIconModule],
  template: `
    @if (metrics?.cbOpen) {
      <div class="cb-banner cb-open" role="alert" aria-live="assertive">
        <mat-icon class="cb-icon">warning</mat-icon>
        <div class="cb-text">
          <strong
            >CIRCUIT BREAKER OPEN — AI provider unavailable. Manual Review
            Required.</strong
          >
          <span class="cb-trips"
            >{{ metrics?.cbTrips24h }} trip(s) in the last 24 hours</span
          >
        </div>
      </div>
    } @else {
      <div class="cb-banner cb-closed" role="status">
        <mat-icon class="cb-icon">check_circle</mat-icon>
        <strong>CIRCUIT BREAKER CLOSED — Healthy</strong>
        @if (metrics?.cbTrips24h !== undefined) {
          <span class="cb-trips"
            >{{ metrics?.cbTrips24h }} trip(s) in the last 24 hours</span
          >
        }
      </div>
    }
  `,
  styles: [
    `
      .cb-banner {
        display: flex;
        align-items: center;
        gap: 12px;
        border-radius: 6px;
        padding: 16px 20px;
        margin-bottom: 24px;
        font-size: 0.9375rem;
      }

      .cb-open {
        background: #fdecea;
        border: 2px solid #e53935;
        color: #b71c1c;

        .cb-icon {
          color: #e53935;
          font-size: 28px;
        }
      }

      .cb-closed {
        background: #e8f5e9;
        border: 2px solid #43a047;
        color: #1b5e20;

        .cb-icon {
          color: #43a047;
          font-size: 28px;
        }
      }

      .cb-text {
        display: flex;
        flex-direction: column;
        gap: 4px;
      }

      .cb-trips {
        font-size: 0.8125rem;
        opacity: 0.85;
      }
    `,
  ],
})
export class CircuitBreakerStatusComponent {
  @Input() metrics: AiOperationalMetricsSummary | null = null;
}
