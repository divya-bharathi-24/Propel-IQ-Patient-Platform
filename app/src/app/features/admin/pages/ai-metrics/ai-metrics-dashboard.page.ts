import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Subscription, interval } from 'rxjs';
import { AiMetricsDashboardStore } from '../../store/ai-metrics-dashboard.store';
import { CircuitBreakerStatusComponent } from './components/circuit-breaker-status.component';
import { LatencyPanelComponent } from './components/latency-panel.component';
import { TokenConsumptionPanelComponent } from './components/token-consumption-panel.component';
import { ErrorRatePanelComponent } from './components/error-rate-panel.component';
import { ModelVersionPanelComponent } from './components/model-version-panel.component';

const REFRESH_INTERVAL_MS = 60_000;

/**
 * Routed container page for the AI Metrics Dashboard (US_050 / AC-4 / AIR-O04).
 *
 * Route: /admin/ai-metrics (protected by adminGuard)
 *
 * Responsibilities:
 * - Loads operational metrics on `ngOnInit` via `AiMetricsDashboardStore.loadOperationalMetrics()`.
 * - Auto-refreshes every 60 seconds via an RxJS `interval` subscription.
 * - Renders five metric panels: circuit breaker, p95 latency, token consumption, error rate, model version.
 * - Cleans up the refresh subscription on `ngOnDestroy`.
 */
@Component({
  selector: 'app-ai-metrics-dashboard',
  standalone: true,
  imports: [
    DatePipe,
    MatProgressSpinnerModule,
    CircuitBreakerStatusComponent,
    LatencyPanelComponent,
    TokenConsumptionPanelComponent,
    ErrorRatePanelComponent,
    ModelVersionPanelComponent,
  ],
  templateUrl: './ai-metrics-dashboard.page.html',
  styleUrl: './ai-metrics-dashboard.page.scss',
})
export class AiMetricsDashboardPageComponent implements OnInit, OnDestroy {
  protected readonly store = inject(AiMetricsDashboardStore);

  private refreshSub?: Subscription;

  ngOnInit(): void {
    this.store.loadOperationalMetrics();
    this.refreshSub = interval(REFRESH_INTERVAL_MS).subscribe(() =>
      this.store.loadOperationalMetrics(),
    );
  }

  ngOnDestroy(): void {
    this.refreshSub?.unsubscribe();
  }

  onModelVersionChange(version: string): void {
    this.store.updateModelVersion(version);
  }
}
