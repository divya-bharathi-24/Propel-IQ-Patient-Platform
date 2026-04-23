import { inject } from '@angular/core';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, catchError, pipe, switchMap, tap } from 'rxjs';
import {
  AppointmentLoadingState,
  LastManualReminderDto,
  StaffAppointmentDetailDto,
  StaffAppointmentDto,
} from '../models/staff-appointment.models';
import {
  StaffAppointmentService,
  StaffAppointmentServiceError,
} from '../services/staff-appointment.service';
import {
  ReminderTriggerError,
  StaffReminderService,
  TriggerReminderResponseDto,
} from '../../../core/services/staff-reminder.service';

/** Lifecycle state for the manual ad-hoc reminder action (US-034). */
export type ReminderState =
  | 'idle'
  | 'loading'
  | 'success'
  | 'error'
  | 'cooldown';

export interface StaffAppointmentState {
  appointments: StaffAppointmentDto[];
  loadingState: AppointmentLoadingState;
  selectedDate: string;
  errorMessage: string | null;

  // ── Appointment detail (US-034) ──────────────────────────────────────────
  selectedAppointment: StaffAppointmentDetailDto | null;
  detailLoadingState: AppointmentLoadingState;
  detailErrorMessage: string | null;

  // ── Ad-hoc reminder state (US-034) ───────────────────────────────────────
  reminderState: ReminderState;
  lastManualReminder: LastManualReminderDto | null;
  cooldownSecondsRemaining: number;
  reminderErrorReason: string | null;
}

const todayIso = (): string => new Date().toISOString().slice(0, 10);

const initialState: StaffAppointmentState = {
  appointments: [],
  loadingState: 'idle',
  selectedDate: todayIso(),
  errorMessage: null,

  selectedAppointment: null,
  detailLoadingState: 'idle',
  detailErrorMessage: null,

  reminderState: 'idle',
  lastManualReminder: null,
  cooldownSecondsRemaining: 0,
  reminderErrorReason: null,
};

/**
 * NgRx Signals store for the staff appointment management page and detail view.
 *
 * List slice:
 * - `loadAppointments(date)` fetches appointments for the given ISO date.
 * - `selectedDate` drives the date-picker header in the list component.
 * - AC-4: after recalculation, calling `loadAppointments` with the same date
 *   refreshes badges via new `noShowRisk` values from the API.
 *
 * Detail slice (US-034):
 * - `loadAppointmentById(id)` fetches a single appointment with full contact
 *   and last manual reminder metadata.
 *
 * Reminder slice (US-034):
 * - `triggerReminder(appointmentId)` dispatches an immediate reminder and
 *   updates `reminderState` / `lastManualReminder` / error signals accordingly.
 */
export const StaffAppointmentStore = signalStore(
  { providedIn: 'root' },
  withState<StaffAppointmentState>(initialState),
  withMethods(
    (
      store,
      service = inject(StaffAppointmentService),
      reminderService = inject(StaffReminderService),
    ) => ({
      /**
       * Loads appointments for the given ISO date string.
       * GET /api/staff/appointments?date={date}
       */
      loadAppointments: rxMethod<string>(
        pipe(
          tap((date) =>
            patchState(store, {
              loadingState: 'loading',
              selectedDate: date,
              errorMessage: null,
            }),
          ),
          switchMap((date) =>
            service.getAppointments(date).pipe(
              tap((appointments) =>
                patchState(store, {
                  appointments,
                  loadingState: 'loaded',
                }),
              ),
              catchError((err: StaffAppointmentServiceError) => {
                patchState(store, {
                  loadingState: 'error',
                  errorMessage: err.message,
                  appointments: [],
                });
                return EMPTY;
              }),
            ),
          ),
        ),
      ),

      /** Updates the selected date without triggering a fetch (e.g. pre-selection). */
      setSelectedDate(date: string): void {
        patchState(store, { selectedDate: date });
      },

      /**
       * Loads the full detail of a single appointment.
       * GET /api/staff/appointments/{id}
       *
       * On success, initialises `reminderState` to 'success' if the appointment
       * already carries a `lastManualReminder` record (AC-3).
       */
      loadAppointmentById: rxMethod<string>(
        pipe(
          tap(() =>
            patchState(store, {
              detailLoadingState: 'loading',
              detailErrorMessage: null,
              selectedAppointment: null,
              reminderState: 'idle',
              lastManualReminder: null,
              cooldownSecondsRemaining: 0,
              reminderErrorReason: null,
            }),
          ),
          switchMap((id) =>
            service.getAppointmentById(id).pipe(
              tap((appointment: StaffAppointmentDetailDto) => {
                const reminderState: ReminderState =
                  appointment.lastManualReminder ? 'success' : 'idle';
                patchState(store, {
                  selectedAppointment: appointment,
                  detailLoadingState: 'loaded',
                  reminderState,
                  lastManualReminder: appointment.lastManualReminder,
                });
              }),
              catchError((err: StaffAppointmentServiceError) => {
                patchState(store, {
                  detailLoadingState: 'error',
                  detailErrorMessage: err.message,
                });
                return EMPTY;
              }),
            ),
          ),
        ),
      ),

      /**
       * Dispatches an immediate reminder for the given appointment (AC-1, US-034).
       * POST /api/staff/appointments/{appointmentId}/reminders/trigger
       *
       * State transitions:
       *  loading → success  : reminder sent; `lastManualReminder` updated (AC-3).
       *  loading → cooldown : 429 received; `cooldownSecondsRemaining` populated.
       *  loading → error    : 422 (cancelled appointment) or 5xx; `reminderErrorReason` set (AC-4).
       */
      triggerReminder: rxMethod<string>(
        pipe(
          tap(() =>
            patchState(store, {
              reminderState: 'loading',
              reminderErrorReason: null,
            }),
          ),
          switchMap((appointmentId) =>
            reminderService.triggerManualReminder(appointmentId).pipe(
              tap((response: TriggerReminderResponseDto) => {
                const lastManualReminder: LastManualReminderDto = {
                  sentAt: response.sentAt,
                  triggeredByStaffName: response.triggeredByStaffName,
                };
                patchState(store, {
                  reminderState: 'success',
                  lastManualReminder,
                });
              }),
              catchError((err: ReminderTriggerError) => {
                if (err.type === 'COOLDOWN') {
                  patchState(store, {
                    reminderState: 'cooldown',
                    cooldownSecondsRemaining: err.retryAfterSeconds ?? 0,
                  });
                } else {
                  patchState(store, {
                    reminderState: 'error',
                    reminderErrorReason: err.message,
                  });
                }
                return EMPTY;
              }),
            ),
          ),
        ),
      ),

      /** Resets reminder feedback state back to idle without reloading the appointment. */
      resetReminderState(): void {
        patchState(store, {
          reminderState: 'idle',
          reminderErrorReason: null,
          cooldownSecondsRemaining: 0,
        });
      },
    }),
  ),
);
