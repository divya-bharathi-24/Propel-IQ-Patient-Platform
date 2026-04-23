import { inject } from '@angular/core';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, catchError, pipe, switchMap, tap } from 'rxjs';
import { SlotDto } from '../models/slot.models';
import { SlotAvailabilityService } from '../services/slot-availability.service';

export type SlotLoadingState = 'idle' | 'loading' | 'loaded' | 'error';

export interface SlotAvailabilityState {
  slots: SlotDto[];
  selectedSlot: SlotDto | null;
  loadingState: SlotLoadingState;
  conflictMessage: string | null;
}

const initialState: SlotAvailabilityState = {
  slots: [],
  selectedSlot: null,
  loadingState: 'idle',
  conflictMessage: null,
};

export const SlotAvailabilityStore = signalStore(
  { providedIn: 'root' },
  withState<SlotAvailabilityState>(initialState),
  withMethods((store, slotService = inject(SlotAvailabilityService)) => ({
    /**
     * Loads available slots for the given specialty and date.
     * Replaces any previously loaded slots and clears conflict messages.
     */
    loadSlots: rxMethod<{ specialtyId: string; date: string }>(
      pipe(
        tap(() =>
          patchState(store, {
            loadingState: 'loading',
            conflictMessage: null,
          }),
        ),
        switchMap(({ specialtyId, date }) =>
          slotService.getAvailableSlots(specialtyId, date).pipe(
            tap((slots) =>
              patchState(store, { slots, loadingState: 'loaded' }),
            ),
            catchError(() => {
              patchState(store, { slots: [], loadingState: 'error' });
              return EMPTY;
            }),
          ),
        ),
      ),
    ),

    /** Selects a slot and clears any active conflict message. */
    selectSlot(slot: SlotDto): void {
      patchState(store, { selectedSlot: slot, conflictMessage: null });
    },

    /** Displays a concurrency-conflict message above the slot grid. */
    setConflict(message: string): void {
      patchState(store, { conflictMessage: message, selectedSlot: null });
    },

    /** Clears the conflict message (e.g. after the user acknowledges it). */
    clearConflict(): void {
      patchState(store, { conflictMessage: null });
    },

    /** Resets the store to its initial state (e.g. when leaving the booking wizard). */
    reset(): void {
      patchState(store, initialState);
    },
  })),
);
