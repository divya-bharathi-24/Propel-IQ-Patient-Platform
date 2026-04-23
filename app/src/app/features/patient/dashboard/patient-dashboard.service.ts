import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import {
  DashboardLoadError,
  PatientDashboardDto,
} from './patient-dashboard.model';

@Injectable({ providedIn: 'root' })
export class PatientDashboardService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = '/api/patient/dashboard';

  /**
   * Fetches the aggregated patient dashboard data.
   * Always fetches fresh data — no caching — so each navigation reflects
   * the latest server state.
   */
  getDashboard(): Observable<PatientDashboardDto> {
    return this.http.get<PatientDashboardDto>(this.apiUrl).pipe(
      catchError((err: unknown) => {
        const error: DashboardLoadError = {
          message: 'Failed to load dashboard data. Please try again.',
          statusCode: err instanceof HttpErrorResponse ? err.status : undefined,
        };
        return throwError(() => error);
      }),
    );
  }
}
