import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import { QueueItem } from './queue.models';

export interface QueueServiceError {
  status: number;
  message: string;
}

/**
 * HTTP service for the same-day queue feature.
 *
 * All endpoints require a Bearer token (attached automatically by AuthInterceptor — US_011).
 * OWASP A01 — access control is enforced server-side; staffGuard prevents
 * unauthorised client-side navigation only.
 */
@Injectable({ providedIn: 'root' })
export class QueueService {
  private readonly http = inject(HttpClient);

  /**
   * Returns all appointments for the current calendar day ordered by timeSlotStart ASC.
   * GET /api/queue/today
   */
  getQueue(): Observable<QueueItem[]> {
    return this.http
      .get<QueueItem[]>('/api/queue/today')
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  /**
   * Marks the given appointment as Arrived.
   * PATCH /api/queue/{appointmentId}/arrived
   */
  markArrived(appointmentId: string): Observable<void> {
    return this.http
      .patch<void>(`/api/queue/${appointmentId}/arrived`, {})
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  /**
   * Reverts an Arrived appointment back to Waiting (same-session day only).
   * PATCH /api/queue/{appointmentId}/revert-arrived
   */
  revertArrived(appointmentId: string): Observable<void> {
    return this.http
      .patch<void>(`/api/queue/${appointmentId}/revert-arrived`, {})
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  private mapError(err: HttpErrorResponse): QueueServiceError {
    return {
      status: err.status,
      message: err.error?.message ?? 'An unexpected error occurred.',
    };
  }
}
