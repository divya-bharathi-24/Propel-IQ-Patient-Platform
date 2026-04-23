import {
  HttpClient,
  HttpErrorResponse,
  HttpParams,
} from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import {
  PatientSearchResultDto,
  WalkInBookingDto,
  WalkInResponseDto,
} from '../models/walkin.models';

export interface WalkInServiceError {
  status: number;
  message: string;
  existingPatientId?: string;
  existingPatientName?: string;
}

@Injectable({ providedIn: 'root' })
export class WalkInService {
  private readonly http = inject(HttpClient);

  /**
   * Searches for existing patients by name or date of birth.
   * GET /api/staff/patients/search?query={query}
   */
  searchPatients(query: string): Observable<PatientSearchResultDto[]> {
    const params = new HttpParams().set('query', query);
    return this.http
      .get<PatientSearchResultDto[]>('/api/staff/patients/search', { params })
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  /**
   * Creates a walk-in booking appointment.
   * POST /api/staff/walkin
   * Returns 409 with existingPatientId when email is already registered.
   */
  createWalkIn(payload: WalkInBookingDto): Observable<WalkInResponseDto> {
    return this.http
      .post<WalkInResponseDto>('/api/staff/walkin', payload)
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  private mapError(err: HttpErrorResponse): WalkInServiceError {
    if (err.status === 409) {
      return {
        status: 409,
        message:
          err.error?.message ?? 'A patient with this email already exists.',
        existingPatientId: err.error?.existingPatientId,
        existingPatientName: err.error?.existingPatientName,
      };
    }
    return {
      status: err.status,
      message: err.error?.message ?? 'An unexpected error occurred.',
    };
  }
}
