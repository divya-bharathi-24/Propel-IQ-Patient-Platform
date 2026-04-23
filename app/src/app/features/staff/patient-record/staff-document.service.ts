import { HttpClient, HttpEvent } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  DocumentHistoryItemDto,
  UploadNoteResponse,
} from './staff-document.models';

/**
 * Service for staff-initiated document operations (US_039).
 *
 * All HTTP calls carry the Bearer token via AuthInterceptor (US_011).
 *
 * FR-044 — Staff uploads post-visit clinical notes via multipart form data.
 * FR-058 — Deletion events are logged server-side; the service supplies the
 *           deletion reason in the request body.
 *
 * OWASP A01 — Role enforcement is handled at both the route guard (staffGuard)
 * and the backend controller; this service does not duplicate that logic.
 */
@Injectable({ providedIn: 'root' })
export class StaffDocumentService {
  private readonly http = inject(HttpClient);

  /**
   * Uploads a clinical note PDF for the given patient.
   *
   * Uses `reportProgress: true` and `observe: 'events'` so the caller
   * can track `HttpEventType.UploadProgress` to drive a progress indicator.
   *
   * @param patientId  UUID of the target patient.
   * @param file       PDF file selected by the staff member (≤ 25 MB).
   * @param encounterRef  Optional appointment/encounter reference string.
   */
  uploadNote(
    patientId: string,
    file: File,
    encounterRef: string | null,
  ): Observable<HttpEvent<UploadNoteResponse>> {
    const form = new FormData();
    form.append('patientId', patientId);
    form.append('file', file, file.name);
    if (encounterRef) {
      form.append('encounterReference', encounterRef);
    }

    return this.http.post<UploadNoteResponse>(
      '/api/staff/documents/upload',
      form,
      { reportProgress: true, observe: 'events' },
    );
  }

  /**
   * Loads the complete document history for a patient.
   *
   * @param patientId  UUID of the patient whose documents are requested.
   */
  getDocumentHistory(patientId: string): Observable<DocumentHistoryItemDto[]> {
    return this.http.get<DocumentHistoryItemDto[]>(
      `/api/staff/patients/${patientId}/documents`,
    );
  }

  /**
   * Soft-deletes a staff-uploaded document within the 24-hour deletion window.
   *
   * The deletion reason is sent in the request body so the backend can
   * write it to the audit trail (FR-058).
   *
   * @param id      UUID of the ClinicalDocument to delete.
   * @param reason  Human-readable explanation (minimum 10 characters).
   */
  deleteDocument(id: string, reason: string): Observable<void> {
    return this.http.delete<void>(`/api/staff/documents/${id}`, {
      body: { reason },
    });
  }
}
