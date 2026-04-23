/**
 * DTOs for Patient Clinical Document Upload feature.
 * US_038 / task_001_fe_document_upload_ui
 *
 * OWASP A01 — These types are consumed only within `authGuard`-protected routes.
 */

/** Processing status of a clinical document. */
export type DocumentProcessingStatus =
  | 'Pending'
  | 'Processing'
  | 'Completed'
  | 'Failed';

/**
 * Single clinical document record returned by `GET /api/documents`.
 * AC-3 — Upload history table columns: fileName, uploadDate, fileSize, processingStatus.
 */
export interface ClinicalDocumentDto {
  id: string;
  fileName: string;
  fileSize: number;
  uploadedAt: string;
  processingStatus: DocumentProcessingStatus;
}

/**
 * Per-file result from `POST /api/documents/upload`.
 * Used for partial batch failure reporting (edge case — Retry flow).
 */
export interface UploadFileResult {
  fileName: string;
  success: boolean;
  /** Server-side error message when `success` is false. */
  errorMessage?: string;
  /** Populated when `success` is true. */
  document?: ClinicalDocumentDto;
}

/**
 * Response body for `POST /api/documents/upload`.
 * FR-041 — batch upload response envelope.
 */
export interface UploadBatchResultDto {
  files: UploadFileResult[];
}
