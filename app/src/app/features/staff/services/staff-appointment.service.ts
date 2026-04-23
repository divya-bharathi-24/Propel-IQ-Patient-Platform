import {
  HttpClient,
  HttpErrorResponse,
  HttpParams,
} from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import {
  StaffAppointmentDetailDto,
  StaffAppointmentDto,
} from '../models/staff-appointment.models';

export interface StaffAppointmentServiceError {
  status: number;
  message: string;
}

/**
 * HTTP service for the staff appointment management feature.
 *
 * All requests require a Bearer token attached automatically by AuthInterceptor.
 * OWASP A01 — access control is enforced server-side; staffGuard prevents
 * unauthorised client-side navigation only.
 */
@Injectable({ providedIn: 'root' })
export class StaffAppointmentService {
  private readonly http = inject(HttpClient);

  /**
   * Returns all appointments for the given date including embedded no-show risk data.
   * GET /api/staff/appointments?date={date}
   *
   * @param date ISO date string, e.g. "2026-04-22"
   */
  getAppointments(date: string): Observable<StaffAppointmentDto[]> {
    const params = new HttpParams().set('date', date);
    return this.http
      .get<StaffAppointmentDto[]>('/api/staff/appointments', { params })
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  /**
   * Returns full appointment details including patient contact info and
   * the last manually triggered reminder metadata.
   * GET /api/staff/appointments/{id}
   *
   * @param id UUID of the appointment
   */
  getAppointmentById(id: string): Observable<StaffAppointmentDetailDto> {
    return this.http
      .get<StaffAppointmentDetailDto>(`/api/staff/appointments/${id}`)
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  private mapError(err: HttpErrorResponse): StaffAppointmentServiceError {
    return {
      status: err.status,
      message: err.error?.message ?? 'An unexpected error occurred.',
    };
  }
}
