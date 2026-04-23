import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import {
  CalendarProvider,
  CalendarSyncStatusDto,
  CalendarSyncStatusResponse,
  InitiateCalendarSyncResponse,
} from '../../features/patient/calendar/calendar-sync.models';

/**
 * HTTP service for Google and Outlook Calendar sync operations (EP-007 / us_035, us_036).
 *
 * OWASP A01 — all endpoints are protected by JWT auth interceptor.
 * OWASP A05 — external links are rendered with rel="noopener noreferrer"
 *             by the consuming CalendarSyncStatusComponent.
 */
@Injectable({ providedIn: 'root' })
export class CalendarSyncService {
  private readonly http = inject(HttpClient);

  /**
   * Initiates the Google Calendar OAuth 2.0 flow by performing a full
   * browser-level redirect to the BE-generated authorization URL.
   *
   * This MUST be a window.location.href redirect, NOT Angular Router navigation,
   * because OAuth requires the browser to navigate fully to Google's consent screen.
   *
   * The BE will redirect back to `/appointments/confirmation?calendarResult=success|failed|declined&appointmentId={id}`.
   *
   * @param appointmentId UUID of the appointment to sync.
   */
  initiateGoogleSync(appointmentId: string): void {
    window.location.href = `/api/calendar/google/auth?appointmentId=${encodeURIComponent(appointmentId)}`;
  }

  /**
   * Fetches the current Google Calendar sync status for an appointment.
   * GET /api/calendar/google/status/{appointmentId}
   *
   * @param appointmentId UUID of the appointment.
   * @returns Observable of `CalendarSyncStatusDto`.
   */
  getSyncStatus(appointmentId: string): Observable<CalendarSyncStatusDto> {
    return this.http
      .get<CalendarSyncStatusDto>(
        `/api/calendar/google/status/${encodeURIComponent(appointmentId)}`,
      )
      .pipe(catchError((err: HttpErrorResponse) => throwError(() => err)));
  }

  /**
   * Triggers a browser download of the appointment ICS file by creating a
   * temporary anchor element with the `download` attribute.
   *
   * Uses a direct browser download rather than Angular HttpClient to allow
   * the browser to handle Content-Disposition: attachment natively (AC-4).
   *
   * @param appointmentId UUID of the appointment.
   */
  downloadIcs(appointmentId: string): void {
    const anchor = document.createElement('a');
    anchor.href = `/api/appointments/${encodeURIComponent(appointmentId)}/ics`;
    anchor.download = `appointment-${appointmentId}.ics`;
    anchor.click();
  }

  /**
   * Re-initiates the OAuth flow to retry sync after a failure or reconnect
   * after token expiry.
   *
   * Delegates to `initiateGoogleSync` — the BE will upsert the event if one
   * already exists, preventing duplicates (edge case: duplicate sync).
   *
   * @param appointmentId UUID of the appointment.
   */
  retrySyncRelink(appointmentId: string): void {
    this.initiateGoogleSync(appointmentId);
  }

  // ── Outlook Calendar methods (EP-007 / US_036) ────────────────────────────

  /**
   * Starts the Microsoft Outlook OAuth 2.0 PKCE flow.
   * POST /api/calendar/outlook/initiate
   *
   * Returns an `authorizationUrl` the component uses for `window.location.href`
   * to redirect the patient to Microsoft's consent screen.
   *
   * OWASP A01 — Bearer token attached by AuthInterceptor.
   *
   * @param appointmentId UUID of the appointment to sync.
   */
  initiateOutlookSync(
    appointmentId: string,
  ): Observable<InitiateCalendarSyncResponse> {
    return this.http
      .post<InitiateCalendarSyncResponse>('/api/calendar/outlook/initiate', {
        appointmentId,
      })
      .pipe(catchError((err: HttpErrorResponse) => throwError(() => err)));
  }

  /**
   * Completes the Microsoft OAuth 2.0 code exchange and creates the Graph event.
   * GET /api/calendar/outlook/callback?code=&state=
   *
   * Called by `OutlookCallbackComponent` after Microsoft redirects back.
   *
   * @param code  Authorization code returned by Microsoft.
   * @param state PKCE state parameter for CSRF validation.
   */
  exchangeOutlookCode(code: string, state: string): Observable<void> {
    return this.http
      .get<void>('/api/calendar/outlook/callback', { params: { code, state } })
      .pipe(catchError((err: HttpErrorResponse) => throwError(() => err)));
  }

  /**
   * Retrieves the current Outlook sync status for an appointment.
   * GET /api/calendar/sync-status?appointmentId=&provider=Outlook
   *
   * @param appointmentId UUID of the appointment.
   * @param provider      Calendar provider discriminator.
   */
  getOutlookSyncStatus(
    appointmentId: string,
    provider: CalendarProvider,
  ): Observable<CalendarSyncStatusResponse> {
    return this.http
      .get<CalendarSyncStatusResponse>('/api/calendar/sync-status', {
        params: { appointmentId, provider },
      })
      .pipe(catchError((err: HttpErrorResponse) => throwError(() => err)));
  }

  /**
   * Downloads the appointment ICS file as a `Blob` via `HttpClient`.
   * GET /api/calendar/ics?appointmentId={id}
   *
   * The component creates a transient anchor with `URL.createObjectURL(blob)`
   * and programmatically clicks it; `URL.revokeObjectURL()` is called after
   * to prevent memory leaks (AC-3).
   *
   * @param appointmentId UUID of the appointment.
   */
  downloadIcsBlob(appointmentId: string): Observable<Blob> {
    return this.http
      .get('/api/calendar/ics', {
        params: { appointmentId },
        responseType: 'blob',
      })
      .pipe(catchError((err: HttpErrorResponse) => throwError(() => err)));
  }
}
