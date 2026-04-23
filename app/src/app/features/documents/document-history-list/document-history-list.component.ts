import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  Input,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { catchError, of } from 'rxjs';
import { StaffDocumentService } from '../staff-document.service';
import { DocumentHistoryItemDto } from '../staff-document.models';

/**
 * DocumentHistoryListComponent — US_039 / TASK_001
 *
 * Displays the patient's full document history, loaded from
 * `GET /api/staff/patients/{patientId}/documents`.
 *
 * Features:
 * - Renders source-type badges: "Staff Upload" (amber) / "Patient Upload" (blue).
 * - Shows staff member name, encounter reference, upload timestamp, and
 *   processing status chip per document row (AC-2).
 * - Soft-delete: for `isDeletable = true` rows, a "Delete" button opens an
 *   inline confirmation panel with a required deletion reason textarea
 *   (minimum 10 characters). Deletion is optimistic; reverts on API error.
 *   (Edge case — wrong patient upload; FR-058 audit trail).
 *
 * Accessibility (WCAG 2.2 AA):
 * - Source type badges carry `aria-label` for screen readers.
 * - Delete error uses `role="alert"`.
 * - Confirmation dialog uses `role="dialog"` with descriptive `aria-label`.
 *
 * OWASP A01 — Component is only rendered within `staffGuard`-protected routes.
 */
@Component({
  selector: 'app-document-history-list',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe],
  templateUrl: './document-history-list.component.html',
  styleUrl: './document-history-list.component.scss',
})
export class DocumentHistoryListComponent implements OnInit {
  /** UUID of the patient whose document history is displayed. */
  @Input({ required: true }) patientId!: string;

  private readonly svc = inject(StaffDocumentService);
  private readonly destroyRef = inject(DestroyRef);

  /** Current document list; updated optimistically on delete. */
  protected readonly documents = signal<DocumentHistoryItemDto[]>([]);

  /** ID of the document awaiting deletion confirmation; null when none. */
  protected readonly deletingId = signal<string | null>(null);

  /** Deletion reason text entered in the confirmation panel. */
  protected readonly deletionReason = signal<string>('');

  /** Per-operation delete error message; null when no error. */
  protected readonly deleteError = signal<string | null>(null);

  /** Whether the initial document list is loading. */
  protected readonly isLoading = signal<boolean>(true);

  /** Error message from the initial load; null when none. */
  protected readonly loadError = signal<string | null>(null);

  ngOnInit(): void {
    this.svc
      .getDocumentHistory(this.patientId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        catchError(() => {
          this.isLoading.set(false);
          this.loadError.set(
            'Failed to load document history. Please refresh.',
          );
          return of([]);
        }),
      )
      .subscribe((docs) => {
        this.documents.set(docs);
        this.isLoading.set(false);
      });
  }

  /** Reloads the document list. Called by the parent after a successful upload. */
  reload(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.svc
      .getDocumentHistory(this.patientId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        catchError(() => {
          this.isLoading.set(false);
          this.loadError.set('Failed to reload document history.');
          return of([]);
        }),
      )
      .subscribe((docs) => {
        this.documents.set(docs);
        this.isLoading.set(false);
      });
  }

  /** Opens the inline confirmation panel for the given document ID. */
  protected openDeleteConfirmation(id: string): void {
    this.deletingId.set(id);
    this.deletionReason.set('');
    this.deleteError.set(null);
  }

  /** Cancels the pending delete and hides the confirmation panel. */
  protected cancelDelete(): void {
    this.deletingId.set(null);
    this.deletionReason.set('');
    this.deleteError.set(null);
  }

  /**
   * Confirms deletion of the document identified by `id`.
   *
   * Validates that the reason is at least 10 characters, performs an
   * optimistic removal from the list, and reverts on API error.
   */
  protected confirmDelete(id: string): void {
    const reason = this.deletionReason().trim();

    if (reason.length < 10) {
      this.deleteError.set('Please provide a reason (minimum 10 characters).');
      return;
    }

    const originalDocs = this.documents();
    this.documents.update((docs) => docs.filter((d) => d.id !== id));
    this.deletingId.set(null);
    this.deletionReason.set('');
    this.deleteError.set(null);

    this.svc
      .deleteDocument(id, reason)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.documents.set(originalDocs);
          this.deleteError.set('Delete failed. Please try again.');
        },
      });
  }

  /** Formats a file size in bytes to a human-readable string. */
  protected formatFileSize(bytes: number): string {
    if (bytes < 1024) {
      return `${bytes} B`;
    }
    if (bytes < 1024 * 1024) {
      return `${(bytes / 1024).toFixed(1)} KB`;
    }
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}
