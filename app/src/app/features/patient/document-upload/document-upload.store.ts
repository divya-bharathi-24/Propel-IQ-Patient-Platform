import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import {
  ClinicalDocumentDto,
  UploadFileResult,
} from './models/clinical-document.dto';

/**
 * State shape for the Document Upload feature.
 * US_038 / task_001_fe_document_upload_ui
 */
export interface DocumentUploadState {
  /** Files chosen by the user (valid + invalid both held until validated). */
  selectedFiles: File[];
  /** Per-filename validation error messages. AC-4 / FR-042. */
  validationErrors: Record<string, string>;
  /** Overall batch upload progress 0–100. */
  uploadProgress: number;
  /** True while an upload HTTP request is in flight. */
  isUploading: boolean;
  /** Document list from GET /api/documents for the history table. AC-3. */
  uploadHistory: ClinicalDocumentDto[];
  /** True while the history is being fetched. */
  loadingHistory: boolean;
  /** Per-file result summary populated after a batch completes. */
  uploadResult: UploadFileResult[] | null;
  /** True when the server responds with 503 (storage unavailable). */
  storageUnavailable: boolean;
  /** Global validation error (e.g. >20 files). */
  globalError: string | null;
}

const initialState: DocumentUploadState = {
  selectedFiles: [],
  validationErrors: {},
  uploadProgress: 0,
  isUploading: false,
  uploadHistory: [],
  loadingHistory: false,
  uploadResult: null,
  storageUnavailable: false,
  globalError: null,
};

/** Maximum allowed files per upload batch. FR-042. */
const MAX_FILES = 20;
/** Maximum allowed file size in bytes (25 MB). AC-1 / FR-042. */
const MAX_SIZE_BYTES = 25 * 1024 * 1024;

/**
 * DocumentUploadStore — NgRx Signals store for the patient document upload feature.
 *
 * Signals: selectedFiles, validationErrors, uploadProgress, isUploading,
 *          uploadHistory, loadingHistory, uploadResult, storageUnavailable, globalError.
 *
 * Methods: validateFiles, resetUpload, loadHistoryStart, loadHistorySuccess,
 *          loadHistoryError, setUploadProgress, startUpload, handleUploadResult,
 *          setStorageUnavailable, retryFiles.
 *
 * OWASP A03 — Client-side MIME + extension validation before any network call.
 */
export const DocumentUploadStore = signalStore(
  { providedIn: 'root' },
  withState<DocumentUploadState>(initialState),
  withMethods((store) => ({
    /**
     * Validates a FileList from the file picker / drop zone.
     * Enforces: batch max 20 (global), MIME + .pdf extension, ≤25 MB per file.
     * Populates `validationErrors` with per-file messages; clears previous state.
     * AC-1, AC-4, FR-042.
     */
    validateFiles(fileList: FileList): void {
      const files = Array.from(fileList);

      if (files.length > MAX_FILES) {
        patchState(store, {
          selectedFiles: [],
          validationErrors: {},
          globalError: `Maximum ${MAX_FILES} files per upload`,
        });
        return;
      }

      const errors: Record<string, string> = {};

      for (const file of files) {
        const isPdf =
          file.type === 'application/pdf' &&
          file.name.toLowerCase().endsWith('.pdf');
        if (!isPdf) {
          errors[file.name] = 'Only PDF files are accepted';
          continue;
        }
        if (file.size > MAX_SIZE_BYTES) {
          errors[file.name] = 'File too large';
        }
      }

      patchState(store, {
        selectedFiles: files,
        validationErrors: errors,
        globalError: null,
        uploadResult: null,
        storageUnavailable: false,
      });
    },

    /** Resets upload state to initial; does not clear history. */
    resetUpload(): void {
      patchState(store, {
        selectedFiles: [],
        validationErrors: {},
        uploadProgress: 0,
        isUploading: false,
        uploadResult: null,
        storageUnavailable: false,
        globalError: null,
      });
    },

    /** Called before fetching upload history from the server. */
    loadHistoryStart(): void {
      patchState(store, { loadingHistory: true });
    },

    /** Called when history is successfully fetched. */
    loadHistorySuccess(history: ClinicalDocumentDto[]): void {
      patchState(store, { uploadHistory: history, loadingHistory: false });
    },

    /** Called when history fetch fails. */
    loadHistoryError(): void {
      patchState(store, { loadingHistory: false });
    },

    /** Updates overall batch progress (0–100). */
    setUploadProgress(progress: number): void {
      patchState(store, { uploadProgress: progress });
    },

    /** Marks upload as in flight. */
    startUpload(): void {
      patchState(store, { isUploading: true, uploadProgress: 0 });
    },

    /**
     * Processes the per-file result array returned by POST /api/documents/upload.
     * Appends successfully uploaded documents to the history list.
     * AC-3 — partial batch failure: already-uploaded files remain in history.
     */
    handleUploadResult(results: UploadFileResult[]): void {
      const successDocs = results
        .filter((r) => r.success && r.document != null)
        .map((r) => r.document as ClinicalDocumentDto);

      patchState(store, {
        isUploading: false,
        uploadProgress: 100,
        uploadResult: results,
        uploadHistory: [...store.uploadHistory(), ...successDocs],
      });
    },

    /**
     * Called when the upload endpoint responds with 503.
     * Clears selected files; does NOT add partial history entries.
     * Edge case — storage unavailable.
     */
    setStorageUnavailable(): void {
      patchState(store, {
        isUploading: false,
        uploadProgress: 0,
        storageUnavailable: true,
        selectedFiles: [],
      });
    },

    /**
     * Re-queues only the named failed files for retry.
     * Clears the full result summary so the retry shows fresh progress.
     * Edge case — partial batch failure Retry action.
     */
    retryFiles(failedFileNames: string[]): void {
      const filesToRetry = store
        .selectedFiles()
        .filter((f) => failedFileNames.includes(f.name));

      patchState(store, {
        selectedFiles: filesToRetry,
        validationErrors: {},
        uploadResult: null,
        uploadProgress: 0,
        storageUnavailable: false,
        globalError: null,
      });
    },
  })),
);
