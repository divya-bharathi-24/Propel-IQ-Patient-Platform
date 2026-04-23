import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';

/** Response DTO returned by POST /api/staff/appointments/{id}/reminders/trigger */
export interface TriggerReminderResponseDto {
  /** ISO UTC timestamp of when the reminder was dispatched. */
  sentAt: string;
  /** Display name of the staff member who triggered the reminder. */
  triggeredByStaffName: string;
}

/**
 * Typed error produced by `StaffReminderService` on non-2xx responses.
 *
 * - `CANCELLED_APPOINTMENT` — HTTP 422: reminder cannot be sent for a cancelled appointment.
 * - `COOLDOWN` — HTTP 429: a reminder was sent too recently; `retryAfterSeconds` indicates
 *   when the cooldown expires.
 * - `GENERIC` — any other error (5xx, network, etc.).
 */
export interface ReminderTriggerError {
  type: 'CANCELLED_APPOINTMENT' | 'COOLDOWN' | 'GENERIC';
  message: string;
  retryAfterSeconds?: number;
}

/**
 * HTTP service for the manual ad-hoc reminder trigger feature (US-034).
 *
 * Wraps `POST /api/staff/appointments/{id}/reminders/trigger` with typed
 * error mapping so the store can branch on error type without inspecting
 * raw HTTP status codes.
 *
 * OWASP A01 — access enforcement is server-side; `staffGuard` prevents
 * client-side navigation only.
 * OWASP A03 — raw error bodies are never surfaced to the UI; only safe
 * pre-validated messages are forwarded.
 */
@Injectable({ providedIn: 'root' })
export class StaffReminderService {
  private readonly http = inject(HttpClient);

  /**
   * Dispatches an immediate email and SMS reminder for the given appointment.
   * POST /api/staff/appointments/{appointmentId}/reminders/trigger
   *
   * @param appointmentId UUID of the appointment to remind.
   * @returns Observable of `TriggerReminderResponseDto` on success.
   * @throws `ReminderTriggerError` on 422 / 429 / 5xx.
   */
  triggerManualReminder(
    appointmentId: string,
  ): Observable<TriggerReminderResponseDto> {
    return this.http
      .post<TriggerReminderResponseDto>(
        `/api/staff/appointments/${appointmentId}/reminders/trigger`,
        {},
      )
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  private mapError(err: HttpErrorResponse): ReminderTriggerError {
    if (err.status === 422) {
      return {
        type: 'CANCELLED_APPOINTMENT',
        message: 'Cannot send reminders for cancelled appointments.',
      };
    }

    if (err.status === 429) {
      const retryAfterSeconds =
        (err.error?.retryAfterSeconds as number | undefined) ?? 300;
      return {
        type: 'COOLDOWN',
        message: '',
        retryAfterSeconds,
      };
    }

    return {
      type: 'GENERIC',
      message:
        (err.error?.message as string | undefined) ??
        'Failed to send reminder. Please try again.',
    };
  }
}
