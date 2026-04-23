import { inject } from '@angular/core';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, catchError, pipe, switchMap, tap } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { RescheduleRequestDto } from '../models/appointment-management.models';
import { AppointmentManagementService } from '../services/appointment-management.service';

export type ActionState =
  | 'idle'
  | 'cancelling'
  | 'rescheduling'
  | 'success'
  | 'error';

export interface AppointmentManagementState {
  actionState: ActionState;
  errorMessage: string | null;
  conflictMessage: string | null;
}

const initialState: AppointmentManagementState = {
  actionState: 'idle',
  errorMessage: null,
  conflictMessage: null,
};

export const AppointmentManagementStore = signalStore(
  { providedIn: 'root' },
  withState<AppointmentManagementState>(initialState),
  withMethods((store, service = inject(AppointmentManagementService)) => ({
    /**
     * Calls POST /api/appointments/{id}/cancel.
     * Sets actionState to 'cancelling' while in-flight, then 'success' or 'error'.
     */
    cancelAppointment: rxMethod<string>(
      pipe(
        tap(() =>
          patchState(store, {
            actionState: 'cancelling',
            errorMessage: null,
            conflictMessage: null,
          }),
        ),
        switchMap((id) =>
          service.cancelAppointment(id).pipe(
            tap(() => patchState(store, { actionState: 'success' })),
            catchError((err: HttpErrorResponse) => {
              const message =
                err.status === 400
                  ? 'Cannot cancel a past appointment'
                  : 'Failed to cancel appointment. Please try again.';
              patchState(store, {
                actionState: 'error',
                errorMessage: message,
              });
              return EMPTY;
            }),
          ),
        ),
      ),
    ),

    /**
     * Calls POST /api/appointments/{id}/reschedule.
     * Sets actionState to 'rescheduling', then 'success', 'error', or handles 409 conflict.
     */
    rescheduleAppointment: rxMethod<{ id: string; dto: RescheduleRequestDto }>(
      pipe(
        tap(() =>
          patchState(store, {
            actionState: 'rescheduling',
            errorMessage: null,
            conflictMessage: null,
          }),
        ),
        switchMap(({ id, dto }) =>
          service.rescheduleAppointment(id, dto).pipe(
            tap(() => patchState(store, { actionState: 'success' })),
            catchError((err: HttpErrorResponse) => {
              if (err.status === 409) {
                patchState(store, {
                  actionState: 'error',
                  conflictMessage:
                    'Slot no longer available — please choose another',
                });
              } else {
                patchState(store, {
                  actionState: 'error',
                  errorMessage:
                    'Failed to reschedule appointment. Please try again.',
                });
              }
              return EMPTY;
            }),
          ),
        ),
      ),
    ),

    /** Clears error and conflict messages, resets to idle. */
    clearMessages(): void {
      patchState(store, {
        actionState: 'idle',
        errorMessage: null,
        conflictMessage: null,
      });
    },
  })),
);
