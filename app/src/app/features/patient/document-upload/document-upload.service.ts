import {
  HttpClient,
  HttpErrorResponse,
  HttpEvent,
  HttpEventType,
} from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import {
  ClinicalDocumentDto,
  UploadBatchResultDto,
} from './models/clinical-document.dto';

/**
 * DocumentUploadService — HTTP wrapper for the Patient Document Upload feature.
 * US_038 / task_001_fe_document_upload_ui
 *
 * Endpoints:
 *  - POST /api/documents/upload  — batch file upload with progress tracking
 *  - GET  /api/documents         — fetch upload history for the authenticated patient
 *
 * OWASP A03 — FormData constructed from pre-validated File objects only;
 *             file names are not sanitised here (server is authoritative).
 * OWASP A01 — Routes are protected by authGuard; bearer token attached by interceptor.
 */
@Injectable({ providedIn: 'root' })
export class DocumentUploadService {
  private readonly http = inject(HttpClient);

  private readonly uploadUrl = '/api/documents/upload';
  private readonly historyUrl = '/api/documents';

  /**
   * Uploads a batch of pre-validated PDF files.
   *
   * Returns a raw `HttpEvent` stream so the caller can observe upload progress
   * (`HttpEventType.UploadProgress`) as well as the final response body.
   *
   * @param validFiles — files that have already passed client-side validation.
   */
  uploadDocuments(
    validFiles: File[],
  ): Observable<HttpEvent<UploadBatchResultDto>> {
    const formData = new FormData();
    validFiles.forEach((f) => formData.append('files', f, f.name));

    return this.http.post<UploadBatchResultDto>(this.uploadUrl, formData, {
      reportProgress: true,
      observe: 'events',
    });
  }

  /**
   * Fetches the authenticated patient's document upload history.
   * AC-3 — history table data source.
   */
  getUploadHistory(): Observable<ClinicalDocumentDto[]> {
    return this.http
      .get<ClinicalDocumentDto[]>(this.historyUrl)
      .pipe(catchError((err: unknown) => throwError(() => this.mapError(err))));
  }

  private mapError(err: unknown): Error {
    if (err instanceof HttpErrorResponse) {
      return new Error(
        `Document history request failed: ${err.status} ${err.statusText}`,
      );
    }
    return new Error('An unexpected error occurred');
  }

  /** Convenience helper — extracts the integer percentage from an UploadProgress event. */
  static uploadProgressPercent(event: HttpEvent<unknown>): number | null {
    if (
      event.type === HttpEventType.UploadProgress &&
      event.total != null &&
      event.total > 0
    ) {
      return Math.round((100 * event.loaded) / event.total);
    }
    return null;
  }
}
