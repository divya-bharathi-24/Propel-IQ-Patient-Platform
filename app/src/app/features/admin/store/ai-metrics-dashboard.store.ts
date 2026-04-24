import { inject } from '@angular/core';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, catchError, pipe, switchMap, tap } from 'rxjs';
import { AiOperationalMetricsSummary } from '../models/admin.models';
import { AiMetricsDashboardService } from '../services/ai-metrics-dashboard.service';

export interface AiMetricsDashboardState {
  operationalMetrics: AiOperationalMetricsSummary | null;
  isLoading: boolean;
  error: string | null;
  lastRefreshed: Date | null;
}

const initialState: AiMetricsDashboardState = {
  operationalMetrics: null,
  isLoading: false,
  error: null,
  lastRefreshed: null,
};

/**
 * NgRx Signals store for the AI Metrics Dashboard (US_050 / AC-4 / AIR-O04).
 *
 * - `loadOperationalMetrics()` — fetches operational metrics from the backend.
 * - `updateModelVersion(version)` — posts a model version change, then reloads metrics.
 */
export const AiMetricsDashboardStore = signalStore(
  { providedIn: 'root' },
  withState<AiMetricsDashboardState>(initialState),
  withMethods((store, service = inject(AiMetricsDashboardService)) => ({
    loadOperationalMetrics: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { isLoading: true, error: null })),
        switchMap(() =>
          service.getOperationalMetrics().pipe(
            tap((metrics) =>
              patchState(store, {
                operationalMetrics: metrics,
                isLoading: false,
                lastRefreshed: new Date(),
              }),
            ),
            catchError((err: { message: string }) => {
              patchState(store, {
                isLoading: false,
                error: err.message ?? 'Failed to load AI metrics.',
              });
              return EMPTY;
            }),
          ),
        ),
      ),
    ),

    updateModelVersion: rxMethod<string>(
      pipe(
        tap(() => patchState(store, { isLoading: true, error: null })),
        switchMap((version) =>
          service.updateModelVersion(version).pipe(
            switchMap(() =>
              service.getOperationalMetrics().pipe(
                tap((metrics) =>
                  patchState(store, {
                    operationalMetrics: metrics,
                    isLoading: false,
                    lastRefreshed: new Date(),
                  }),
                ),
                catchError((err: { message: string }) => {
                  patchState(store, {
                    isLoading: false,
                    error:
                      err.message ??
                      'Failed to reload metrics after model version update.',
                  });
                  return EMPTY;
                }),
              ),
            ),
            catchError((err: { message: string }) => {
              patchState(store, {
                isLoading: false,
                error: err.message ?? 'Failed to update model version.',
              });
              return EMPTY;
            }),
          ),
        ),
      ),
    ),
  })),
);
