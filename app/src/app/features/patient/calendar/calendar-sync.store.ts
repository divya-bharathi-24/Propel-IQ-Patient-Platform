import { inject } from '@angular/core';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, catchError, pipe, switchMap, tap } from 'rxjs';
import { CalendarSyncService } from '../../../core/services/calendar-sync.service';
import { CalendarSyncStatus } from './calendar-sync.models';

export interface CalendarSyncState {
  /** Current sync status for the loaded appointment. */
  syncStatus: CalendarSyncStatus;
  /** Google Calendar event URL — populated only when syncStatus is 'synced'. */
  eventLink: string | null;
  /** True while an HTTP status poll is in-flight. */
  isSyncing: boolean;
}

const initialState: CalendarSyncState = {
  syncStatus: 'none',
  eventLink: null,
  isSyncing: false,
};

export const CalendarSyncStore = signalStore(
  { providedIn: 'root' },
  withState<CalendarSyncState>(initialState),
  withMethods((store, service = inject(CalendarSyncService)) => ({
    /**
     * Polls GET /api/calendar/google/status/{appointmentId} and updates
     * syncStatus + eventLink from the server response.
     */
    loadSyncStatus: rxMethod<string>(
      pipe(
        tap(() => patchState(store, { isSyncing: true })),
        switchMap((appointmentId) =>
          service.getSyncStatus(appointmentId).pipe(
            tap((dto) =>
              patchState(store, {
                syncStatus: dto.syncStatus,
                eventLink: dto.eventLink ?? null,
                isSyncing: false,
              }),
            ),
            catchError(() => {
              // Network failure on status poll — treat as failed so the
              // ICS fallback is offered (AC-4).
              patchState(store, { syncStatus: 'failed', isSyncing: false });
              return EMPTY;
            }),
          ),
        ),
      ),
    ),

    /**
     * Applies an OAuth callback result (success | failed | declined) received
     * via the `calendarResult` query param after the BE redirect (AC-1 – AC-4).
     *
     * 'success' intentionally does not set syncStatus here; the caller is
     * expected to follow with `loadSyncStatus()` to fetch the real event link.
     */
    handleOAuthResult(result: 'success' | 'failed' | 'declined'): void {
      if (result === 'success') {
        // Keep isSyncing true; loadSyncStatus() will resolve it.
        patchState(store, { isSyncing: true });
        return;
      }

      if (result === 'declined') {
        patchState(store, { syncStatus: 'declined', isSyncing: false });
        return;
      }

      // 'failed'
      patchState(store, { syncStatus: 'failed', isSyncing: false });
    },

    /**
     * Marks the sync status as 'expired' — used when the patient's stored
     * Google token can no longer refresh (edge case: token expiry during re-auth).
     */
    markTokenExpired(): void {
      patchState(store, { syncStatus: 'expired', isSyncing: false });
    },

    /** Resets the store to the initial state. Call when navigating away. */
    reset(): void {
      patchState(store, initialState);
    },
  })),
);
