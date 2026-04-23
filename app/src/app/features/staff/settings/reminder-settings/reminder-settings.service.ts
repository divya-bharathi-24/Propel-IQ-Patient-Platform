import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';

export interface ReminderIntervalsDto {
  intervalHours: number[];
}

export interface ReminderSettingsServiceError {
  status: number;
  message: string;
}

/**
 * HTTP service for reading and updating the system-wide reminder interval
 * settings.
 *
 * Endpoints:
 *   GET  /api/settings/reminders  → { intervalHours: number[] }
 *   PUT  /api/settings/reminders  → { intervalHours: number[] }
 *
 * OWASP A01 — access is enforced server-side; client guard is defence-in-depth.
 */
@Injectable({ providedIn: 'root' })
export class ReminderSettingsService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = '/api/settings/reminders';

  getIntervals(): Observable<ReminderIntervalsDto> {
    return this.http
      .get<ReminderIntervalsDto>(this.apiBase)
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  updateIntervals(intervalHours: number[]): Observable<ReminderIntervalsDto> {
    return this.http
      .put<ReminderIntervalsDto>(this.apiBase, { intervalHours })
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  private mapError(err: HttpErrorResponse): ReminderSettingsServiceError {
    return {
      status: err.status,
      message: err.error?.message ?? 'An unexpected error occurred.',
    };
  }
}
