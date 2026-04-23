import { inject } from '@angular/core';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, catchError, pipe, switchMap, tap } from 'rxjs';
import { WaitlistEntryDto } from '../models/waitlist.models';
import { WaitlistService } from '../services/waitlist.service';

export type WaitlistLoadingState = 'idle' | 'loading' | 'loaded' | 'error';
export type WaitlistCancelState = 'idle' | 'cancelling' | 'cancelled' | 'error';

export interface WaitlistState {
  entries: WaitlistEntryDto[];
  loadingState: WaitlistLoadingState;
  cancelState: WaitlistCancelState;
}

const initialState: WaitlistState = {
  entries: [],
  loadingState: 'idle',
  cancelState: 'idle',
};

export const WaitlistStore = signalStore(
  { providedIn: 'root' },
  withState<WaitlistState>(initialState),
  withMethods((store, service = inject(WaitlistService)) => ({
    /**
     * Loads all waitlist entries for the authenticated patient.
     * No-ops when a load is already in-flight to prevent duplicate requests.
     */
    loadEntries: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loadingState: 'loading' })),
        switchMap(() =>
          service.getMyWaitlistEntries().pipe(
            tap((entries) =>
              patchState(store, { entries, loadingState: 'loaded' }),
            ),
            catchError(() => {
              patchState(store, { entries: [], loadingState: 'error' });
              return EMPTY;
            }),
          ),
        ),
      ),
    ),

    /**
     * Cancels the preferred slot designation for the given waitlist entry.
     * On success, removes the entry from the local state to update the UI.
     */
    cancelPreference: rxMethod<string>(
      pipe(
        tap(() => patchState(store, { cancelState: 'cancelling' })),
        switchMap((waitlistId) =>
          service.cancelPreference(waitlistId).pipe(
            tap(() => {
              const updated = store
                .entries()
                .filter((e) => e.id !== waitlistId);
              patchState(store, { entries: updated, cancelState: 'cancelled' });
            }),
            catchError(() => {
              patchState(store, { cancelState: 'error' });
              return EMPTY;
            }),
          ),
        ),
      ),
    ),

    /** Resets cancel state (e.g. after snackbar is shown). */
    clearCancelState(): void {
      patchState(store, { cancelState: 'idle' });
    },
  })),
);
