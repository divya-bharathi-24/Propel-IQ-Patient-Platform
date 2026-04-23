import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, of } from 'rxjs';
import {
  InsuranceCheckResult,
  InsurancePreCheckRequest,
  InsurancePreCheckResponse,
} from '../../shared/models/insurance.models';

/** Fallback result used when the pre-check endpoint is unreachable (NFR-018). */
const CHECK_PENDING_FALLBACK: InsuranceCheckResult = {
  status: 'CheckPending',
  guidance:
    'Insurance check is temporarily unavailable. Your booking will proceed.',
};

/**
 * Wraps POST /api/insurance/pre-check.
 * On any HTTP error the observable completes with a `CheckPending` result
 * so the booking flow is never blocked (FR-040, NFR-018 graceful degradation).
 */
@Injectable({ providedIn: 'root' })
export class InsuranceService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = '/api/insurance';

  check(request: InsurancePreCheckRequest): Observable<InsuranceCheckResult> {
    return this.http
      .post<InsurancePreCheckResponse>(`${this.apiBase}/pre-check`, request)
      .pipe(
        catchError((err: unknown) => {
          if (err instanceof HttpErrorResponse) {
            // Any HTTP error → degrade gracefully rather than surfacing a failure
            return of(CHECK_PENDING_FALLBACK);
          }
          return of(CHECK_PENDING_FALLBACK);
        }),
      );
  }
}
