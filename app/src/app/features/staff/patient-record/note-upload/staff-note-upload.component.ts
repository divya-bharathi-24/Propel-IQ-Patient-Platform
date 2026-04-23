import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  Input,
  inject,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpEventType } from '@angular/common/http';
import { StaffDocumentService } from '../staff-document.service';
import { StaffNoteUploadState } from '../staff-document.models';

/**
 * StaffNoteUploadComponent — US_039 / TASK_001
 *
 * Allows authenticated Staff or Admin users to upload a single post-visit
 * clinical note PDF for a given patient.
 *
 * Responsibilities:
 * - Client-side validation: MIME type must be `application/pdf`; size ≤ 25 MB.
 * - Tracks upload progress via HttpEventType.UploadProgress → uploadProgress signal.
 * - Displays a dismissible amber warning banner when the encounter reference
 *   cannot be resolved server-side (edge case — encounter ref not found).
 * - Emits `uploadComplete` after a successful upload so the parent can reload
 *   the document history list.
 *
 * Accessibility (WCAG 2.2 AA):
 * - File input `aria-describedby` links to the error span when an error exists.
 * - Warning banner and server error use `role="alert"` so screen readers
 *   announce them immediately.
 * - Progress bar uses `aria-label="Upload progress"`.
 *
 * OWASP A01 — Role enforcement is handled at route level by `staffGuard`;
 * the component itself does not duplicate that check.
 * OWASP A04 — File size and MIME-type validation prevent insecure large/binary
 * payloads from reaching the server (defence in depth; BE validates too).
 */
@Component({
  selector: 'app-staff-note-upload',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './staff-note-upload.component.html',
  styleUrl: './staff-note-upload.component.scss',
})
export class StaffNoteUploadComponent {
  /** UUID of the patient for whom the note is being uploaded. */
  @Input({ required: true }) patientId!: string;

  /** Emitted after a successful upload so parent can refresh document history. */
  readonly uploadComplete = output<void>();

  private readonly svc = inject(StaffDocumentService);
  private readonly destroyRef = inject(DestroyRef);

  /** Maximum permitted file size in bytes (25 MB). */
  protected readonly MAX_FILE_SIZE = 25 * 1024 * 1024;

  /** Reactive state for the upload form. */
  protected readonly uploadState = signal<StaffNoteUploadState>({
    isUploading: false,
    uploadProgress: 0,
    encounterWarning: false,
    serverError: null,
    validationErrors: {},
  });

  /** The validated PDF file ready for upload; null when no valid file is selected. */
  protected readonly selectedFile = signal<File | null>(null);

  /** Optional encounter/appointment reference typed by the staff member. */
  protected readonly encounterRef = signal<string>('');

  /**
   * Validates the selected file for MIME type and size constraints.
   * Updates `validationErrors` and stores the file only when valid.
   */
  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;

    if (!file) {
      return;
    }

    const errors: { file?: string } = {};

    if (file.type !== 'application/pdf') {
      errors.file = 'Only PDF files are accepted';
    } else if (file.size > this.MAX_FILE_SIZE) {
      errors.file = 'File too large — maximum 25 MB';
    }

    this.uploadState.update((s) => ({
      ...s,
      validationErrors: errors,
      serverError: null,
    }));

    if (!errors.file) {
      this.selectedFile.set(file);
    } else {
      this.selectedFile.set(null);
    }
  }

  /**
   * Initiates the multipart upload via `StaffDocumentService.uploadNote()`.
   * Tracks `HttpEventType.UploadProgress` to update `uploadProgress`.
   * On completion, emits `uploadComplete` and resets the form.
   */
  protected upload(): void {
    const file = this.selectedFile();
    if (!file) {
      return;
    }

    this.uploadState.update((s) => ({
      ...s,
      isUploading: true,
      uploadProgress: 0,
      encounterWarning: false,
      serverError: null,
    }));

    this.svc
      .uploadNote(this.patientId, file, this.encounterRef() || null)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (event) => {
          if (
            event.type === HttpEventType.UploadProgress &&
            event.total != null
          ) {
            const progress = Math.round((100 * event.loaded) / event.total);
            this.uploadState.update((s) => ({
              ...s,
              uploadProgress: progress,
            }));
            return;
          }

          if (event.type === HttpEventType.Response && event.body != null) {
            this.uploadState.update((s) => ({
              ...s,
              isUploading: false,
              uploadProgress: 100,
              encounterWarning: event.body!.encounterWarning,
            }));
            this.selectedFile.set(null);
            this.encounterRef.set('');
            this.uploadComplete.emit();
          }
        },
        error: (err) => {
          const serverError =
            err.status === 403
              ? 'Access denied.'
              : 'Upload failed. Please try again.';
          this.uploadState.update((s) => ({
            ...s,
            isUploading: false,
            serverError,
          }));
        },
      });
  }

  /** Dismisses the encounter-reference-not-found warning banner. */
  protected dismissEncounterWarning(): void {
    this.uploadState.update((s) => ({ ...s, encounterWarning: false }));
  }
}
