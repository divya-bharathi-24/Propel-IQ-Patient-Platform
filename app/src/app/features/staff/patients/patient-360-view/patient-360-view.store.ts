import { computed, inject } from '@angular/core';
import {
  patchState,
  signalStore,
  withComputed,
  withMethods,
  withState,
} from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, catchError, pipe, switchMap, tap } from 'rxjs';
import {
  Patient360ViewDto,
  Patient360ViewService,
  VerifyProfileResponseDto,
} from '../../../../core/services/patient-360-view.service';
import { ClinicalConflictStore } from './store/clinical-conflict.store';

// ── State shape ───────────────────────────────────────────────────────────────

export type LoadingState = 'idle' | 'loading' | 'loaded' | 'error';
export type VerifyState = 'idle' | 'loading' | 'success' | 'error';

export interface Patient360ViewState {
  view360: Patient360ViewDto | null;
  loadingState: LoadingState;
  loadError: string | null;
  verifyState: VerifyState;
  verifyError: string | null;
  verifyResult: VerifyProfileResponseDto | null;
}

const initialState: Patient360ViewState = {
  view360: null,
  loadingState: 'idle',
  loadError: null,
  verifyState: 'idle',
  verifyError: null,
  verifyResult: null,
};

// ── Store ─────────────────────────────────────────────────────────────────────

/**
 * NgRx Signals store for the 360-degree patient view page (US_041, task_001).
 *
 * Slices:
 *  - `view360` — the full aggregated DTO returned by the API.
 *  - `loadingState` — 'idle' | 'loading' | 'loaded' | 'error'.
 *  - `verifyState`  — 'idle' | 'loading' | 'success' | 'error'.
 *
 * Methods:
 *  - `load360View(patientId)` — fetches the aggregated 360 data.
 *  - `verifyProfile(patientId)` — submits the staff verification request.
 *  - `retryDocument(patientId, documentId)` — re-triggers failed document extraction.
 */
export const Patient360ViewStore = signalStore(
  { providedIn: 'root' },
  withState<Patient360ViewState>(initialState),

  withComputed(() => {
    const conflictStore = inject(ClinicalConflictStore);
    return {
      /**
       * True when there are zero unresolved Critical conflicts.
       * Guards the "Verify Profile" action (US_044 AC-4).
       */
      canVerify: computed(() => conflictStore.unresolvedCriticalCount() === 0),
    };
  }),

  withMethods((store, service = inject(Patient360ViewService), conflictStore = inject(ClinicalConflictStore)) => ({
    /**
     * Loads the 360-degree patient view.
     * GET /api/staff/patients/{patientId}/360-view
     */
    load360View: rxMethod<string>(
      pipe(
        tap(() =>
          patchState(store, { loadingState: 'loading', loadError: null }),
        ),
        switchMap((patientId) =>
          service.get360View(patientId).pipe(
            tap((view360) => {
              patchState(store, { view360, loadingState: 'loaded' });
              // Hydrate the conflict store from the 360-view payload (US_044)
              conflictStore.loadConflicts(view360.conflicts ?? []);
            }),
            catchError((err) => {
              patchState(store, {
                loadingState: 'error',
                loadError: err?.message ?? 'Failed to load patient 360 view.',
              });
              return EMPTY;
            }),
          ),
        ),
      ),
    ),

    /**
     * Submits staff verification for the patient's aggregated profile.
     * POST /api/staff/patients/{patientId}/360-view/verify
     */
    verifyProfile: rxMethod<string>(
      pipe(
        tap(() =>
          patchState(store, { verifyState: 'loading', verifyError: null }),
        ),
        switchMap((patientId) =>
          service.verifyProfile(patientId).pipe(
            tap((verifyResult) => {
              patchState(store, {
                verifyState: 'success',
                verifyResult,
                // Mirror the updated status into the loaded view snapshot
                view360: store.view360()
                  ? {
                      ...store.view360()!,
                      verificationStatus: verifyResult.verificationStatus,
                      verifiedAt: verifyResult.verifiedAt,
                      verifiedByStaffName: verifyResult.verifiedByStaffName,
                    }
                  : null,
              });
            }),
            catchError((err) => {
              patchState(store, {
                verifyState: 'error',
                verifyError:
                  err?.message ?? 'Verification failed. Please try again.',
              });
              return EMPTY;
            }),
          ),
        ),
      ),
    ),

    /**
     * Retries extraction for a failed document.
     * POST /api/staff/patients/{patientId}/360-view/retry/{documentId}
     */
    retryDocument: rxMethod<{ patientId: string; documentId: string }>(
      pipe(
        switchMap(({ patientId, documentId }) =>
          service.retryDocument(patientId, documentId).pipe(
            tap(() => {
              // Optimistically mark the document as Processing in local state
              const current = store.view360();
              if (!current) return;
              patchState(store, {
                view360: {
                  ...current,
                  documents: current.documents.map((d) =>
                    d.documentId === documentId
                      ? { ...d, status: 'Completed' as const }
                      : d,
                  ),
                },
              });
            }),
            catchError(() => EMPTY),
          ),
        ),
      ),
    ),
  })),
);
