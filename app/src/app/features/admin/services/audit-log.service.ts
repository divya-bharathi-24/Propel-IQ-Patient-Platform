import {
  HttpClient,
  HttpErrorResponse,
  HttpParams,
} from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import { AuditLogQueryParams, AuditLogResponse } from '../models/admin.models';

/** Page size is fixed at 50 per FR-057 — not exposed as a query parameter. */
const PAGE_SIZE = 50;

/**
 * Data-access service for the read-only audit log (US_047 / FR-057).
 *
 * Provides a single `getAuditLogs` method that issues a cursor-based GET
 * request to `GET /api/admin/audit-logs`.  Only defined filter params are
 * appended to the query string — undefined/null values are omitted.
 */
@Injectable({ providedIn: 'root' })
export class AuditLogService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = '/api/admin';

  /**
   * Fetches a page of audit events.
   *
   * @param params - Optional filter and cursor parameters.
   * @returns Observable of `AuditLogResponse` containing events, nextCursor,
   *          and the aggregate totalCount.
   */
  getAuditLogs(params: AuditLogQueryParams = {}): Observable<AuditLogResponse> {
    let httpParams = new HttpParams().set('pageSize', PAGE_SIZE.toString());

    if (params.cursor) {
      httpParams = httpParams.set('cursor', params.cursor);
    }
    if (params.dateFrom) {
      httpParams = httpParams.set('dateFrom', params.dateFrom);
    }
    if (params.dateTo) {
      httpParams = httpParams.set('dateTo', params.dateTo);
    }
    if (params.userId) {
      httpParams = httpParams.set('userId', params.userId);
    }
    if (params.actionType) {
      httpParams = httpParams.set('actionType', params.actionType);
    }
    if (params.entityType) {
      httpParams = httpParams.set('entityType', params.entityType);
    }

    return this.http
      .get<AuditLogResponse>(`${this.apiBase}/audit-logs`, {
        params: httpParams,
      })
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
