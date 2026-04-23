import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import {
  RequiresAttentionItemDto,
  RiskInterventionDto,
} from '../models/risk-flag.models';

export interface RiskFlagServiceError {
  status: number;
  message: string;
}

/**
 * HTTP service for high-risk appointment flag operations (US_032).
 *
 * Endpoints consumed:
 *   GET  /api/risk/requires-attention          → unacknowledged High-risk items (AC-4)
 *   GET  /api/risk/{appointmentId}/interventions → intervention rows for one appointment (AC-1)
 *   PATCH /api/risk/interventions/{id}/accept   → mark intervention accepted (AC-2)
 *   PATCH /api/risk/interventions/{id}/dismiss  → mark intervention dismissed (AC-3)
 *
 * OWASP A01: access is enforced server-side; staffGuard prevents client-side
 * navigation for unauthorised roles only.
 * OWASP A03: all payloads are typed DTOs — no dynamic SQL / template injection risk.
 */
@Injectable({ providedIn: 'root' })
export class RiskFlagService {
  private readonly http = inject(HttpClient);

  /**
   * Fetches all unacknowledged High-risk appointments for the "Requires Attention" section.
   * GET /api/risk/requires-attention
   */
  getRequiresAttention(): Observable<RequiresAttentionItemDto[]> {
    return this.http
      .get<RequiresAttentionItemDto[]>('/api/risk/requires-attention')
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  /**
   * Fetches all intervention rows for a specific appointment.
   * GET /api/risk/{appointmentId}/interventions
   *
   * @param appointmentId UUID of the appointment.
   */
  getInterventions(appointmentId: string): Observable<RiskInterventionDto[]> {
    return this.http
      .get<
        RiskInterventionDto[]
      >(`/api/risk/${encodeURIComponent(appointmentId)}/interventions`)
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  /**
   * Accepts a recommended intervention and triggers the relevant staff action.
   * PATCH /api/risk/interventions/{id}/accept
   *
   * @param interventionId UUID of the intervention row.
   */
  acceptIntervention(interventionId: string): Observable<void> {
    return this.http
      .patch<void>(
        `/api/risk/interventions/${encodeURIComponent(interventionId)}/accept`,
        {},
      )
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  /**
   * Dismisses a recommended intervention with an optional reason.
   * PATCH /api/risk/interventions/{id}/dismiss
   *
   * @param interventionId UUID of the intervention row.
   * @param reason         Optional dismissal reason (max 500 chars, validated server-side).
   */
  dismissIntervention(
    interventionId: string,
    reason: string | null,
  ): Observable<void> {
    return this.http
      .patch<void>(
        `/api/risk/interventions/${encodeURIComponent(interventionId)}/dismiss`,
        { reason },
      )
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  private mapError(err: HttpErrorResponse): RiskFlagServiceError {
    return {
      status: err.status,
      message: err.error?.message ?? 'An unexpected error occurred.',
    };
  }
}
