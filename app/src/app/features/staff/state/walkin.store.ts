import { inject } from '@angular/core';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, catchError, debounceTime, pipe, switchMap, tap } from 'rxjs';
import {
  PatientSearchResultDto,
  WalkInBookingDto,
  WalkInResponseDto,
} from '../models/walkin.models';
import { WalkInService, WalkInServiceError } from '../services/walkin.service';

export type WalkInActionState =
  | 'idle'
  | 'searching'
  | 'submitting'
  | 'success'
  | 'error';

export interface WalkInState {
  searchResults: PatientSearchResultDto[];
  selectedPatient: PatientSearchResultDto | null;
  actionState: WalkInActionState;
  errorMessage: string | null;
  duplicatePatient: { patientId: string; name: string } | null;
  slotFullWarning: boolean;
  confirmedBooking: WalkInResponseDto | null;
}

const initialState: WalkInState = {
  searchResults: [],
  selectedPatient: null,
  actionState: 'idle',
  errorMessage: null,
  duplicatePatient: null,
  slotFullWarning: false,
  confirmedBooking: null,
};

export const WalkInStore = signalStore(
  { providedIn: 'root' },
  withState<WalkInState>(initialState),
  withMethods((store, service = inject(WalkInService)) => ({
    /**
     * Debounced patient search by name or date of birth.
     * GET /api/staff/patients/search?query={query}
     */
    searchPatients: rxMethod<string>(
      pipe(
        debounceTime(400),
        tap(() =>
          patchState(store, {
            actionState: 'searching',
            errorMessage: null,
          }),
        ),
        switchMap((query) =>
          service.searchPatients(query).pipe(
            tap((results) =>
              patchState(store, {
                searchResults: results,
                actionState: 'idle',
              }),
            ),
            catchError((err: WalkInServiceError) => {
              patchState(store, {
                actionState: 'error',
                errorMessage: err.message,
                searchResults: [],
              });
              return EMPTY;
            }),
          ),
        ),
      ),
    ),

    /** Selects a patient from search results. */
    selectPatient(patient: PatientSearchResultDto): void {
      patchState(store, { selectedPatient: patient });
    },

    /**
     * Submits a walk-in booking (link, create, or anonymous).
     * POST /api/staff/walkin
     * Handles 409 duplicate email by setting duplicatePatient.
     * Handles slot-full response by setting slotFullWarning.
     */
    submitWalkIn: rxMethod<WalkInBookingDto>(
      pipe(
        tap(() =>
          patchState(store, {
            actionState: 'submitting',
            errorMessage: null,
            duplicatePatient: null,
            slotFullWarning: false,
          }),
        ),
        switchMap((dto) =>
          service.createWalkIn(dto).pipe(
            tap((response) => {
              patchState(store, {
                actionState: 'success',
                confirmedBooking: response,
                slotFullWarning: response.queuedOnly,
              });
            }),
            catchError((err: WalkInServiceError) => {
              if (err.status === 409 && err.existingPatientId) {
                patchState(store, {
                  actionState: 'error',
                  duplicatePatient: {
                    patientId: err.existingPatientId,
                    name: err.existingPatientName ?? 'Existing Patient',
                  },
                });
              } else {
                patchState(store, {
                  actionState: 'error',
                  errorMessage: err.message,
                });
              }
              return EMPTY;
            }),
          ),
        ),
      ),
    ),

    /** Clears the duplicate patient state after resolving. */
    clearDuplicate(): void {
      patchState(store, { duplicatePatient: null });
    },

    /** Resets the entire store to its initial state. */
    clearState(): void {
      patchState(store, initialState);
    },
  })),
);
