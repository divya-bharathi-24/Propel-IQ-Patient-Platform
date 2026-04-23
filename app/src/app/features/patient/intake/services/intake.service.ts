import { HttpClient, HttpHeaders, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import {
  IntakeDraftResponse,
  IntakeFieldMap,
  IntakeFormResponseDto,
  IntakeFormValue,
  IntakeRecordDto,
  ManualIntakeFormValue,
  ResumeAiSessionResponse,
} from '../models/intake-edit-form.model';

@Injectable({ providedIn: 'root' })
export class IntakeService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = '/api/intake';

  /**
   * Fetches the persisted intake record for an appointment.
   * Returns the full response so callers can extract the ETag header for
   * optimistic concurrency on save.
   */
  getRecord(
    appointmentId: string,
  ): Observable<{ record: IntakeRecordDto; eTag: string }> {
    return this.http
      .get<IntakeRecordDto>(`${this.apiBase}/${appointmentId}`, {
        observe: 'response',
      })
      .pipe(
        map((response: HttpResponse<IntakeRecordDto>) => ({
          record: response.body as IntakeRecordDto,
          eTag: response.headers.get('ETag') ?? '',
        })),
      );
  }

  /**
   * Fetches the auto-saved draft for an appointment, if one exists.
   * Returns an `IntakeDraftResponse` where `exists` indicates availability.
   */
  getDraft(appointmentId: string): Observable<IntakeDraftResponse> {
    return this.http.get<IntakeDraftResponse>(
      `${this.apiBase}/${appointmentId}/draft`,
    );
  }

  /**
   * Persists an auto-saved draft to the backend.
   * Used on field-blur to support session-timeout recovery (AC-4).
   */
  saveDraft(
    appointmentId: string,
    formValue: IntakeFormValue,
  ): Observable<void> {
    return this.http.post<void>(
      `${this.apiBase}/${appointmentId}/draft`,
      formValue,
    );
  }

  /**
   * Submits (UPSERT) the full intake record via PUT.
   * Sends `If-Match` header for optimistic concurrency.
   *
   * @returns The full HTTP response so callers can inspect status codes
   *   (200 OK, 409 Conflict, 422 Unprocessable Entity) and response headers.
   */
  saveRecord(
    appointmentId: string,
    formValue: IntakeFormValue,
    eTag: string,
  ): Observable<HttpResponse<IntakeRecordDto>> {
    const headers = new HttpHeaders({ 'If-Match': eTag });
    return this.http.put<IntakeRecordDto>(
      `${this.apiBase}/${appointmentId}`,
      formValue,
      { headers, observe: 'response' },
    );
  }

  // ── Manual intake (US_029) ────────────────────────────────────────────────

  /**
   * Fetches the combined form context for the manual intake patient form.
   * Returns any existing manual draft and any AI-extracted data for the
   * same appointment, enabling seamless mode-switch pre-population (AC-2).
   */
  getForm(appointmentId: string): Observable<IntakeFormResponseDto> {
    return this.http.get<IntakeFormResponseDto>(`${this.apiBase}/form`, {
      params: { appointmentId },
    });
  }

  /**
   * Posts a periodic autosave of the manual intake draft.
   * Called on `merge(interval(30_000), blur$)` emissions.
   */
  autosave(
    appointmentId: string,
    data: ManualIntakeFormValue,
  ): Observable<void> {
    return this.http.post<void>(`${this.apiBase}/autosave`, {
      appointmentId,
      data,
    });
  }

  /**
   * Discards the in-progress manual intake draft ("Start Fresh" action).
   */
  deleteDraft(appointmentId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiBase}/draft`, {
      params: { appointmentId },
    });
  }

  /**
   * Submits the completed manual intake form.
   * Maps to POST /api/intake/submit; back-end persists source = Manual.
   */
  submitManualIntake(
    appointmentId: string,
    data: ManualIntakeFormValue,
  ): Observable<void> {
    return this.http.post<void>(`${this.apiBase}/submit`, {
      appointmentId,
      data,
    });
  }

  // ── US_030: Mode-switch & autosave orchestration ──────────────────────────

  /**
   * Injects the current Manual draft fields into an AI session context so the
   * AI can resume from where the patient left off (Manual → AI, AC-2).
   * Maps to POST /api/intake/session/resume.
   */
  resumeAiSession(
    appointmentId: string,
    draftFields: IntakeFieldMap,
  ): Observable<ResumeAiSessionResponse> {
    return this.http.post<ResumeAiSessionResponse>(
      `${this.apiBase}/session/resume`,
      { appointmentId, draftFields },
    );
  }

  /**
   * Syncs a localStorage-persisted draft to the server after network reconnect.
   * Maps to POST /api/intake/sync-local-draft.
   * Returns 200 on success; 409 when a server-side version conflicts.
   */
  syncLocalDraft(
    appointmentId: string,
    fields: IntakeFieldMap,
    capturedAt: number,
  ): Observable<void> {
    return this.http.post<void>(`${this.apiBase}/sync-local-draft`, {
      appointmentId,
      fields,
      capturedAt,
    });
  }

  /**
   * Saves a draft derived from the full IntakeFieldMap (orchestration layer).
   * Delegates to the existing POST /api/intake/{appointmentId}/draft endpoint
   * delivered in EP-002/US_017 — no new endpoint created.
   * Named distinctly from saveDraft() to preserve the IntakeEditComponent API.
   */
  saveOrchestratedDraft(
    appointmentId: string,
    fields: IntakeFieldMap,
  ): Observable<void> {
    return this.http.post<void>(
      `${this.apiBase}/${appointmentId}/draft`,
      fields,
    );
  }
}
