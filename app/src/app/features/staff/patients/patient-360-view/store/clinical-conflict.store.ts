import { computed, inject } from '@angular/core';
import {
  patchState,
  signalStore,
  withComputed,
  withMethods,
  withState,
} from '@ngrx/signals';
import { EMPTY, catchError, pipe, switchMap, tap } from 'rxjs';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import {
  DataConflictDto,
  ResolveConflictPayload,
} from '../../../../../core/services/patient-360-view.service';
import { ConflictService } from '../services/conflict.service';

// ── State shape ───────────────────────────────────────────────────────────────

export interface ClinicalConflictState {
  conflicts: DataConflictDto[];
  resolveError: string | null;
}

const initialState: ClinicalConflictState = {
  conflicts: [],
  resolveError: null,
};

// ── Store ─────────────────────────────────────────────────────────────────────

/**
 * NgRx Signals store for conflict data within the 360-degree patient view (US_044).
 *
 * State slices:
 *  - `conflicts`            — full conflict objects loaded from API or 360-view payload.
 *  - `resolveError`         — last resolve API error message, if any.
 *
 * Computed signals:
 *  - `unresolvedCriticalCount` — count of Critical/Unresolved conflicts (gates Verify Profile).
 *
 * Methods:
 *  - `loadConflicts(conflicts)` — hydrates store from 360-view response payload.
 *  - `resolveConflict(conflictId, payload)` — optimistic update → calls resolve API.
 */
export const ClinicalConflictStore = signalStore(
  { providedIn: 'root' },
  withState<ClinicalConflictState>(initialState),

  withComputed((store) => ({
    unresolvedCriticalCount: computed(
      () =>
        store
          .conflicts()
          .filter(
            (c) =>
              c.severity === 'Critical' && c.resolutionStatus === 'Unresolved',
          ).length,
    ),
  })),

  withMethods((store, service = inject(ConflictService)) => ({
    /**
     * Hydrates the conflicts signal from the 360-view API response payload.
     * Called by Patient360ViewStore after a successful load.
     */
    loadConflicts(conflicts: DataConflictDto[]): void {
      patchState(store, { conflicts, resolveError: null });
    },

    /**
     * Optimistically marks a conflict as Resolved in local state, then
     * persists the resolution via POST /api/conflicts/{id}/resolve.
     * Reverts the optimistic update on API failure.
     */
    resolveConflict: rxMethod<{ conflictId: string; payload: ResolveConflictPayload }>(
      pipe(
        tap(({ conflictId, payload }) => {
          // Optimistic update — mark Resolved locally before API call
          patchState(store, {
            conflicts: store.conflicts().map((c) =>
              c.conflictId === conflictId
                ? {
                    ...c,
                    resolutionStatus: 'Resolved' as const,
                    resolvedValue: payload.resolvedValue,
                  }
                : c,
            ),
            resolveError: null,
          });
        }),
        switchMap(({ conflictId, payload }) =>
          service.resolveConflict(conflictId, payload).pipe(
            catchError((err) => {
              // Revert optimistic update on failure
              patchState(store, {
                conflicts: store.conflicts().map((c) =>
                  c.conflictId === conflictId
                    ? { ...c, resolutionStatus: 'Unresolved' as const, resolvedValue: undefined }
                    : c,
                ),
                resolveError: err?.message ?? 'Failed to save conflict resolution.',
              });
              return EMPTY;
            }),
          ),
        ),
      ),
    ),
  })),
);
