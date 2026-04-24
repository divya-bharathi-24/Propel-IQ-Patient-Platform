import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import {
  DataConflictDto,
  ResolveConflictPayload,
} from '../../../../../core/services/patient-360-view.service';

export interface ConflictServiceError {
  status: number;
  message: string;
}

/**
 * Angular HTTP service for the conflict resolution feature (US_044, task_002).
 *
 * Endpoints:
 *  - GET  /api/patients/{patientId}/conflicts
 *  - POST /api/conflicts/{conflictId}/resolve
 *
 * OWASP A01: access control is server-side; staffGuard + AuthInterceptor handle auth.
 */
@Injectable({ providedIn: 'root' })
export class ConflictService {
  private readonly http = inject(HttpClient);

  /**
   * Fetches all conflict objects for a patient.
   * GET /api/patients/{patientId}/conflicts
   */
  getConflicts(patientId: string): Observable<DataConflictDto[]> {
    return this.http
      .get<DataConflictDto[]>(`/api/patients/${patientId}/conflicts`)
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  /**
   * Submits the staff resolution for a specific conflict.
   * POST /api/conflicts/{conflictId}/resolve
   */
  resolveConflict(
    conflictId: string,
    payload: ResolveConflictPayload,
  ): Observable<void> {
    return this.http
      .post<void>(`/api/conflicts/${conflictId}/resolve`, payload)
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  private mapError(err: HttpErrorResponse): ConflictServiceError {
    return {
      status: err.status,
      message: err.error?.message ?? err.message ?? 'An unexpected error occurred.',
    };
  }
}
