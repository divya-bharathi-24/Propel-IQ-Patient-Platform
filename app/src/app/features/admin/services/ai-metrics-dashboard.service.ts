import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import {
  AiOperationalMetricsSummary,
  UpdateModelVersionRequest,
} from '../models/admin.models';

/**
 * Data-access service for AI Operational Metrics (US_050 / AC-4 / AIR-O04).
 *
 * - `getOperationalMetrics()` — GET /api/admin/ai-metrics/operational
 * - `updateModelVersion(version)` — POST /api/admin/ai-config/model-version
 */
@Injectable({ providedIn: 'root' })
export class AiMetricsDashboardService {
  private readonly http = inject(HttpClient);
  private readonly metricsBase = '/api/admin/ai-metrics';
  private readonly configBase = '/api/admin/ai-config';

  getOperationalMetrics(): Observable<AiOperationalMetricsSummary> {
    return this.http
      .get<AiOperationalMetricsSummary>(`${this.metricsBase}/operational`)
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  updateModelVersion(modelVersion: string): Observable<void> {
    const body: UpdateModelVersionRequest = { modelVersion };
    return this.http
      .post<void>(`${this.configBase}/model-version`, body)
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  private mapError(err: HttpErrorResponse): {
    status: number;
    message: string;
  } {
    return {
      status: err.status,
      message:
        err.error?.message ?? err.message ?? 'An unexpected error occurred.',
    };
  }
}
