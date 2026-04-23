/**
 * Staff document models for US_039 — Staff Post-Visit Clinical Note Upload.
 *
 * These types are shared between StaffNoteUploadComponent and
 * DocumentHistoryListComponent.
 *
 * FR-044 — Staff uploads post-visit clinical notes.
 * FR-058 — Clinical data modification events are logged in the audit trail.
 */

/** Indicates whether a document was uploaded by a patient or a staff member. */
export type DocumentSourceType = 'PatientUpload' | 'StaffUpload';

/** Server-side processing lifecycle for an uploaded clinical document. */
export type DocumentProcessingStatus =
  | 'Pending'
  | 'Processing'
  | 'Completed'
  | 'Failed';

/**
 * DTO returned by `GET /api/staff/patients/{patientId}/documents`.
 * Each entry represents one document in the patient's document history.
 */
export interface DocumentHistoryItemDto {
  /** UUID of the clinical document record. */
  id: string;
  /** Original file name as supplied at upload time. */
  fileName: string;
  /** File size in bytes. */
  fileSize: number;
  /** Whether the document was uploaded by a patient or staff member. */
  sourceType: DocumentSourceType;
  /** Full name of the staff member who uploaded the document; null for patient uploads. */
  uploadedByName: string | null;
  /** Optional appointment/encounter reference string; null when not provided. */
  encounterReference: string | null;
  /** Current server-side processing status. */
  processingStatus: DocumentProcessingStatus;
  /** ISO 8601 upload timestamp. */
  uploadedAt: string;
  /**
   * Computed by the backend: true when sourceType is 'StaffUpload'
   * and the document was uploaded within the last 24 hours.
   */
  isDeletable: boolean;
}

/**
 * Response body returned by `POST /api/staff/documents/upload` on success.
 */
export interface UploadNoteResponse {
  /** UUID of the newly created ClinicalDocument record. */
  id: string;
  /**
   * True when the supplied encounter reference could not be resolved.
   * The document is still created (linked to the patient without an
   * appointment reference) and the UI renders a dismissible amber warning.
   */
  encounterWarning: boolean;
  /** Human-readable warning detail; non-null when encounterWarning is true. */
  warningMessage: string | null;
}

/**
 * Signal state for the StaffNoteUploadComponent upload form.
 */
export interface StaffNoteUploadState {
  isUploading: boolean;
  uploadProgress: number;
  encounterWarning: boolean;
  serverError: string | null;
  validationErrors: { file?: string; encounterRef?: string };
}
