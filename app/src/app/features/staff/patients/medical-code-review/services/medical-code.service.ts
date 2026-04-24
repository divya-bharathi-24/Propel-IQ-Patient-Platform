import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import {
  CodeValidationRequest,
  CodeValidationResult,
  ConfirmCodesPayload,
  MedicalCodeSuggestionsResponse,
} from '../models/medical-code.models';

export interface MedicalCodeServiceError {
  status: number;
  message: string;
}

/**
 * Angular HTTP service for the medical code review feature (US_043).
 *
 * Encapsulates all three API interactions required by the review workflow:
 *   - Loading AI-suggested codes for a patient encounter.
 *   - Validating a manually entered code before adding it to the panel.
 *   - Submitting the staff's bulk confirmation decisions.
 *
 * OWASP A01 — Access control is enforced server-side; this service does not
 * add role headers — the `authGuard` + `staffGuard` protect the route, and
 * the HttpClient interceptor attaches the Bearer token automatically.
 */
@Injectable({ providedIn: 'root' })
export class MedicalCodeService {
  private readonly http = inject(HttpClient);

  /**
   * Fetches AI-suggested ICD-10 and CPT codes for a patient encounter.
   * GET /api/patients/{patientId}/medical-codes
   */
  getSuggestions(
    patientId: string,
  ): Observable<MedicalCodeSuggestionsResponse> {
    return this.http
      .get<MedicalCodeSuggestionsResponse>(
        `/api/patients/${patientId}/medical-codes`,
      )
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  /**
   * Validates a manually entered code against the backend code dictionary.
   * POST /api/medical-codes/validate
   */
  validateCode(
    request: CodeValidationRequest,
  ): Observable<CodeValidationResult> {
    return this.http
      .post<CodeValidationResult>('/api/medical-codes/validate', request)
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  /**
   * Submits the final staff decision set for the encounter.
   * POST /api/medical-codes/confirm
   */
  confirmCodes(payload: ConfirmCodesPayload): Observable<void> {
    return this.http
      .post<void>('/api/medical-codes/confirm', payload)
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  private mapError(err: HttpErrorResponse): MedicalCodeServiceError {
    return {
      status: err.status,
      message: err.error?.message ?? 'An unexpected error occurred.',
    };
  }
}
