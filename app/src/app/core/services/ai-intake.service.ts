import { HttpClient, HttpStatusCode } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, throwError } from 'rxjs';
import { ExtractedField } from '../../features/patient/intake/intake-chat.store';

// ── Request / Response Models ─────────────────────────────────────────────────

export interface StartSessionResponse {
  sessionId: string;
  openingQuestion: string;
}

export interface AiTurnResponse {
  aiResponse: string;
  extractedFields: ExtractedField[];
  isFallback: boolean;
  isSessionComplete: boolean;
}

export interface ConfirmedIntakeFields {
  demographics: Record<string, string>;
  medicalHistory: Array<Record<string, string>>;
  symptoms: Array<Record<string, string>>;
  medications: Array<Record<string, string>>;
}

export interface SubmitIntakeResponse {
  intakeRecordId: string;
}

// ── Service ───────────────────────────────────────────────────────────────────

@Injectable({ providedIn: 'root' })
export class AiIntakeService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = '/api/intake/ai';

  /**
   * Starts a new AI intake session for the given appointment.
   * Returns the sessionId and the AI's opening contextual question.
   */
  startSession(appointmentId: string): Observable<StartSessionResponse> {
    return this.http
      .post<StartSessionResponse>(`${this.apiBase}/session`, { appointmentId })
      .pipe(catchError((err) => this.handleError(err)));
  }

  /**
   * Sends the patient's free-text message and receives the AI's next response
   * along with any newly extracted fields.
   *
   * On HTTP 503 or `isFallback: true`, callers must activate fallback mode.
   */
  sendMessage(
    sessionId: string,
    userMessage: string,
  ): Observable<AiTurnResponse> {
    return this.http
      .post<AiTurnResponse>(`${this.apiBase}/message`, {
        sessionId,
        userMessage,
      })
      .pipe(
        map((response) => {
          // Surface isFallback flag upstream even on HTTP 200
          return response;
        }),
        catchError((err) => {
          if (err?.status === HttpStatusCode.ServiceUnavailable) {
            // Return a synthetic fallback response so the component can
            // call store.activateFallbackMode() without additional branching.
            const fallbackResponse: AiTurnResponse = {
              aiResponse: '',
              extractedFields: [],
              isFallback: true,
              isSessionComplete: false,
            };
            return [fallbackResponse];
          }
          return this.handleError(err);
        }),
      );
  }

  /**
   * Submits the confirmed intake fields and persists an IntakeRecord
   * (source = AI) on the backend.
   */
  submitIntake(
    sessionId: string,
    confirmedFields: ConfirmedIntakeFields,
  ): Observable<SubmitIntakeResponse> {
    return this.http
      .post<SubmitIntakeResponse>(`${this.apiBase}/submit`, {
        sessionId,
        confirmedFields,
      })
      .pipe(catchError((err) => this.handleError(err)));
  }

  // ── Private ────────────────────────────────────────────────────────────────

  /**
   * Normalises API errors before propagating.
   * OWASP A05: raw server error detail is never forwarded to the UI layer.
   */
  private handleError(err: unknown): Observable<never> {
    return throwError(() => err);
  }
}
