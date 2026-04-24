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
  CodeDecision,
  CodeType,
  ConfirmCodesPayload,
  DecisionStatus,
  ManualCodeItem,
  MedicalCodeSuggestionDto,
} from '../models/medical-code.models';
import { MedicalCodeService } from '../services/medical-code.service';

// ── State shape ───────────────────────────────────────────────────────────────

export type LoadingState = 'idle' | 'loading' | 'loaded' | 'error';
export type SubmitState = 'idle' | 'loading' | 'success' | 'error';

export interface MedicalCodeReviewState {
  /** All AI-suggested codes fetched from the API. */
  suggestions: MedicalCodeSuggestionDto[];
  /** Optional backend message when suggestions array is empty. */
  emptyMessage: string | null;
  /** Manually entered codes validated and added by staff. */
  manualCodes: ManualCodeItem[];
  /**
   * Per-code decision map keyed by `codeId`.
   * Uses a plain object (Record) instead of Map for NgRx Signals compatibility.
   */
  decisions: Record<string, CodeDecision>;
  loadingState: LoadingState;
  loadError: string | null;
  submitState: SubmitState;
  submitError: string | null;
}

const initialState: MedicalCodeReviewState = {
  suggestions: [],
  emptyMessage: null,
  manualCodes: [],
  decisions: {},
  loadingState: 'idle',
  loadError: null,
  submitState: 'idle',
  submitError: null,
};

// ── Store ─────────────────────────────────────────────────────────────────────

/**
 * NgRx Signals store for the medical code review page (US_043, task_001_fe).
 *
 * State slices:
 *  - `suggestions`    — AI-suggested codes loaded from the API on page init.
 *  - `decisions`      — Record<codeId, {status, rejectionReason?}> updated as
 *                       staff confirms or rejects each code.
 *  - `manualCodes`    — Staff-entered codes validated before being added.
 *
 * Computed signals:
 *  - `icdCodes`       — Subset of suggestions with codeType === 'ICD10'.
 *  - `cptCodes`       — Subset of suggestions with codeType === 'CPT'.
 *  - `pendingCount`   — Number of suggestions that are still in 'Pending' state.
 *  - `totalCount`     — Total suggestions + manual codes (denominator for progress).
 *
 * Methods:
 *  - `loadSuggestions(patientId)` — calls GET /api/patients/{id}/medical-codes.
 *  - `confirmCode(codeId)`        — marks a code Accepted.
 *  - `rejectCode(codeId, reason)` — marks a code Rejected with a reason.
 *  - `addManualCode(entry)`       — appends a validated manual code.
 *  - `submitReview(patientId)`    — calls POST /api/medical-codes/confirm.
 */
export const MedicalCodeReviewStore = signalStore(
  { providedIn: 'root' },
  withState<MedicalCodeReviewState>(initialState),

  withComputed((store) => ({
    icdCodes: computed(() =>
      store.suggestions().filter((s) => s.codeType === 'ICD10'),
    ),
    cptCodes: computed(() =>
      store.suggestions().filter((s) => s.codeType === 'CPT'),
    ),
    pendingCount: computed(() => {
      const decisions = store.decisions();
      return store
        .suggestions()
        .filter((s) => (decisions[s.codeId]?.status ?? 'Pending') === 'Pending')
        .length;
    }),
    totalCount: computed(
      () => store.suggestions().length + store.manualCodes().length,
    ),
  })),

  withMethods((store, service = inject(MedicalCodeService)) => ({
    /**
     * Loads AI-suggested codes for a patient encounter.
     * Initialises the `decisions` map with all suggestions set to 'Pending'.
     */
    loadSuggestions: rxMethod<string>(
      pipe(
        tap(() =>
          patchState(store, {
            loadingState: 'loading',
            loadError: null,
            suggestions: [],
            decisions: {},
            manualCodes: [],
            emptyMessage: null,
          }),
        ),
        switchMap((patientId) =>
          service.getSuggestions(patientId).pipe(
            tap((response) => {
              const initialDecisions: Record<string, CodeDecision> =
                response.suggestions.reduce<Record<string, CodeDecision>>(
                  (acc, s) => {
                    acc[s.codeId] = { status: 'Pending' };
                    return acc;
                  },
                  {},
                );

              patchState(store, {
                suggestions: response.suggestions,
                decisions: initialDecisions,
                emptyMessage: response.message ?? null,
                loadingState: 'loaded',
              });
            }),
            catchError((err) => {
              patchState(store, {
                loadingState: 'error',
                loadError:
                  err?.message ?? 'Failed to load medical code suggestions.',
              });
              return EMPTY;
            }),
          ),
        ),
      ),
    ),

    /** Marks a code as Accepted. Idempotent — safe to call multiple times. */
    confirmCode(codeId: string): void {
      patchState(store, {
        decisions: {
          ...store.decisions(),
          [codeId]: { status: 'Accepted' },
        },
      });
    },

    /**
     * Marks a code as Rejected with a mandatory reason.
     * The component enforces a non-empty reason before calling this method.
     */
    rejectCode(codeId: string, rejectionReason: string): void {
      patchState(store, {
        decisions: {
          ...store.decisions(),
          [codeId]: { status: 'Rejected', rejectionReason },
        },
      });
    },

    /** Appends a manually validated code to the review panel. */
    addManualCode(entry: ManualCodeItem): void {
      patchState(store, {
        manualCodes: [...store.manualCodes(), entry],
      });
    },

    /**
     * Builds the confirmation payload and submits it to the backend.
     * POST /api/medical-codes/confirm
     */
    submitReview: rxMethod<string>(
      pipe(
        tap(() =>
          patchState(store, { submitState: 'loading', submitError: null }),
        ),
        switchMap((patientId) => {
          const decisions = store.decisions();
          const decisionItems = store.suggestions().map((s) => ({
            codeId: s.codeId,
            status:
              decisions[s.codeId]?.status ?? ('Pending' as DecisionStatus),
            rejectionReason: decisions[s.codeId]?.rejectionReason,
          }));

          const payload: ConfirmCodesPayload = {
            patientId,
            decisions: decisionItems,
            manualCodes: store.manualCodes(),
          };

          return service.confirmCodes(payload).pipe(
            tap(() => patchState(store, { submitState: 'success' })),
            catchError((err) => {
              patchState(store, {
                submitState: 'error',
                submitError: err?.message ?? 'Failed to submit code review.',
              });
              return EMPTY;
            }),
          );
        }),
      ),
    ),

    /** Resets the store to its initial state (e.g. on component destroy). */
    reset(): void {
      patchState(store, initialState);
    },
  })),
);
