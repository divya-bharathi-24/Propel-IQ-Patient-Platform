import { computed, inject } from '@angular/core';
import {
  patchState,
  signalStore,
  withComputed,
  withMethods,
  withState,
} from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { Subject } from 'rxjs';
import { EMPTY, catchError, pipe, switchMap, tap } from 'rxjs';
import {
  RequiresAttentionItemDto,
  RiskFlagLoadingState,
  RiskInterventionDto,
} from '../models/risk-flag.models';
import {
  RiskFlagService,
  RiskFlagServiceError,
} from '../services/risk-flag.service';

export interface RiskFlagState {
  /** Keyed by appointmentId — holds the pending intervention rows per appointment. */
  interventionsByAppointment: Record<string, RiskInterventionDto[]>;
  /** Loading state per appointmentId for the intervention rows. */
  interventionLoadingByAppointment: Record<string, RiskFlagLoadingState>;
  /** Unacknowledged high-risk items for the Requires Attention dashboard section. */
  requiresAttentionItems: RequiresAttentionItemDto[];
  requiresAttentionLoadingState: RiskFlagLoadingState;
  requiresAttentionError: string | null;
}

const initialState: RiskFlagState = {
  interventionsByAppointment: {},
  interventionLoadingByAppointment: {},
  requiresAttentionItems: [],
  requiresAttentionLoadingState: 'idle',
  requiresAttentionError: null,
};

/**
 * NgRx Signals store for the high-risk appointment flag feature (US_032).
 *
 * - `loadInterventions(appointmentId)` fetches pending rows for one appointment card.
 * - `acceptIntervention(id, appointmentId)` applies an optimistic remove, then
 *   rolls back via `refresh$.next()` on server error.
 * - `dismissIntervention(id, reason, appointmentId)` same optimistic pattern.
 * - `loadRequiresAttention()` populates the dashboard "Requires Attention" section.
 */
export const RiskFlagStore = signalStore(
  { providedIn: 'root' },
  withState<RiskFlagState>(initialState),
  withComputed((store) => ({
    /**
     * Returns the count of unacknowledged High-risk items.
     * Used by `aria-live="polite"` count announcement in RequiresAttentionSectionComponent.
     */
    requiresAttentionCount: computed(
      () => store.requiresAttentionItems().length,
    ),
  })),
  withMethods((store, service = inject(RiskFlagService)) => {
    /**
     * Internal subject used to trigger a server re-fetch after an optimistic
     * update fails. Emitting `appointmentId` re-fetches only the affected appointment.
     */
    const refresh$ = new Subject<string>();

    return {
      /**
       * Loads all pending intervention rows for a single appointment.
       * GET /api/risk/{appointmentId}/interventions
       */
      loadInterventions: rxMethod<string>(
        pipe(
          tap((appointmentId) =>
            patchState(store, (s) => ({
              interventionLoadingByAppointment: {
                ...s.interventionLoadingByAppointment,
                [appointmentId]: 'loading' as RiskFlagLoadingState,
              } as Record<string, RiskFlagLoadingState>,
            })),
          ),
          switchMap((appointmentId) =>
            service.getInterventions(appointmentId).pipe(
              tap((interventions) =>
                patchState(store, (s) => ({
                  interventionsByAppointment: {
                    ...s.interventionsByAppointment,
                    [appointmentId]: interventions.filter(
                      (i) => i.status === 'Pending',
                    ),
                  } as Record<string, RiskInterventionDto[]>,
                  interventionLoadingByAppointment: {
                    ...s.interventionLoadingByAppointment,
                    [appointmentId]: 'loaded' as RiskFlagLoadingState,
                  } as Record<string, RiskFlagLoadingState>,
                })),
              ),
              catchError(() => {
                patchState(store, (s) => ({
                  interventionLoadingByAppointment: {
                    ...s.interventionLoadingByAppointment,
                    [appointmentId]: 'error' as RiskFlagLoadingState,
                  } as Record<string, RiskFlagLoadingState>,
                }));
                return EMPTY;
              }),
            ),
          ),
        ),
      ),

      /**
       * Accepts a recommended intervention with optimistic UI update.
       * On error, re-fetches interventions for the appointment to restore state.
       * PATCH /api/risk/interventions/{id}/accept
       */
      acceptIntervention(interventionId: string, appointmentId: string): void {
        // Optimistic remove
        patchState(store, (s) => ({
          interventionsByAppointment: {
            ...s.interventionsByAppointment,
            [appointmentId]: (
              s.interventionsByAppointment[appointmentId] ?? []
            ).filter((i) => i.id !== interventionId),
          } as Record<string, RiskInterventionDto[]>,
        }));

        service.acceptIntervention(interventionId).subscribe({
          error: () => refresh$.next(appointmentId),
        });
      },

      /**
       * Dismisses a recommended intervention with optimistic UI update.
       * On error, re-fetches interventions for the appointment to restore state.
       * PATCH /api/risk/interventions/{id}/dismiss
       */
      dismissIntervention(
        interventionId: string,
        reason: string | null,
        appointmentId: string,
      ): void {
        // Optimistic remove
        patchState(store, (s) => ({
          interventionsByAppointment: {
            ...s.interventionsByAppointment,
            [appointmentId]: (
              s.interventionsByAppointment[appointmentId] ?? []
            ).filter((i) => i.id !== interventionId),
          } as Record<string, RiskInterventionDto[]>,
        }));

        service.dismissIntervention(interventionId, reason).subscribe({
          error: () => refresh$.next(appointmentId),
        });
      },

      /**
       * Loads unacknowledged High-risk appointments for the dashboard section.
       * GET /api/risk/requires-attention
       * Results are sorted ascending by appointmentTime (AC-4).
       */
      loadRequiresAttention: rxMethod<void>(
        pipe(
          tap(() =>
            patchState(store, {
              requiresAttentionLoadingState: 'loading',
              requiresAttentionError: null,
            }),
          ),
          switchMap(() =>
            service.getRequiresAttention().pipe(
              tap((items) =>
                patchState(store, {
                  requiresAttentionItems: [...items].sort(
                    (a, b) =>
                      new Date(a.appointmentTime).getTime() -
                      new Date(b.appointmentTime).getTime(),
                  ),
                  requiresAttentionLoadingState: 'loaded',
                }),
              ),
              catchError((err: RiskFlagServiceError) => {
                patchState(store, {
                  requiresAttentionLoadingState: 'error',
                  requiresAttentionError: err.message,
                  requiresAttentionItems: [],
                });
                return EMPTY;
              }),
            ),
          ),
        ),
      ),

      /**
       * Exposes the refresh subject as an observable so
       * `loadInterventions` can be wired to re-fetch on rollback.
       */
      getRefresh$: () => refresh$.asObservable(),
    };
  }),
);
