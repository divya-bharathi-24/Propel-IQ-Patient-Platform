import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import {
  CreateWalkInPatientRequest,
  CreateWalkInPatientResponse,
  PatientSearchResult,
} from '../models/staff.models';

export interface StaffServiceError {
  status: number;
  message: string;
}

@Injectable({ providedIn: 'root' })
export class StaffService {
  private readonly http = inject(HttpClient);

  createWalkInPatient(
    dto: CreateWalkInPatientRequest,
  ): Observable<CreateWalkInPatientResponse> {
    return this.http
      .post<CreateWalkInPatientResponse>('/api/patients/create', dto)
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  searchPatient(email: string): Observable<PatientSearchResult> {
    return this.http
      .get<PatientSearchResult>('/api/patients/search', { params: { email } })
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  private mapError(err: HttpErrorResponse): StaffServiceError {
    return {
      status: err.status,
      message: err.error?.message ?? 'An unexpected error occurred.',
    };
  }
}
