import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  ViewChild,
  inject,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { HttpEventType } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';

import { DocumentUploadStore } from './document-upload.store';
import { DocumentUploadService } from './document-upload.service';
import { UploadFileResult } from './models/clinical-document.dto';

/**
 * DocumentUploadComponent — US_038 / task_001_fe_document_upload_ui
 *
 * Patient-facing document upload page.
 *
 * Features:
 * - Drag-and-drop / click-to-open file picker (keyboard accessible, WCAG 2.2 AA).
 * - Client-side PDF validation: MIME + extension, ≤25 MB, ≤20 files batch. AC-1/FR-042.
 * - Per-file error messages. AC-4.
 * - Real-time upload progress via `reportProgress`. FR-041.
 * - Upload history `mat-table`. AC-3.
 * - Status chips: Pending/Processing/Completed/Failed. WCAG 2.2 AA — colour + text.
 * - 503 storage-unavailable banner (edge case).
 * - Partial batch failure Retry flow (edge case).
 *
 * OWASP A01 — Component is rendered only under the `authGuard`-protected
 *             `/documents` route.
 * OWASP A03 — Only pre-validated File objects are included in the FormData payload.
 */
@Component({
  selector: 'app-document-upload',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatTableModule,
    MatIconModule,
    MatTooltipModule,
  ],
  providers: [DocumentUploadStore],
  templateUrl: './document-upload.component.html',
  styleUrl: './document-upload.component.scss',
})
export class DocumentUploadComponent implements OnInit {
  @ViewChild('fileInput')
  private readonly fileInputRef!: ElementRef<HTMLInputElement>;

  protected readonly store = inject(DocumentUploadStore);
  private readonly svc = inject(DocumentUploadService);
  private readonly destroyRef = inject(DestroyRef);

  /** Columns displayed in the upload history mat-table. AC-3. */
  protected readonly historyColumns = [
    'fileName',
    'uploadedAt',
    'fileSize',
    'processingStatus',
  ] as const;

  /** Whether drag-over visual state is active. */
  protected isDragOver = false;

  ngOnInit(): void {
    this.loadHistory();
  }

  /** Opens the hidden file picker on drop-zone click. */
  protected openFilePicker(): void {
    this.fileInputRef.nativeElement.click();
  }

  /** Handles native `<input type="file">` change event. */
  protected onFileInputChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.store.validateFiles(input.files);
    }
    // Reset input value so the same file can be selected again after error.
    input.value = '';
  }

  /** Prevents the default browser navigation on drag-over. */
  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver = true;
  }

  /** Resets drag-over state when the cursor leaves the zone. */
  protected onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.isDragOver = false;
  }

  /**
   * Handles drop — extracts the FileList and delegates validation to the store.
   * OWASP A03 — validation occurs before any network call.
   */
  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver = false;

    const files = event.dataTransfer?.files;
    if (files && files.length > 0) {
      this.store.validateFiles(files);
    }
  }

  /**
   * Returns the subset of `selectedFiles` that have no validation error.
   * Only these files are included in the upload payload. AC-1.
   */
  protected get validFiles(): File[] {
    const errors = this.store.validationErrors();
    return this.store.selectedFiles().filter((f) => !errors[f.name]);
  }

  /** Count of successfully uploaded files in the current batch result. */
  protected get successUploadCount(): number {
    return this.store.uploadResult()?.filter((r) => r.success).length ?? 0;
  }

  /** True when there are valid (error-free) files and no upload is in progress. */
  protected get canUpload(): boolean {
    return this.validFiles.length > 0 && !this.store.isUploading();
  }

  /**
   * Initiates the upload of all valid files in the current selection.
   * Subscribes to the HttpEvent stream to track progress and handle 503.
   * FR-041 — batch upload orchestration.
   */
  protected upload(): void {
    const files = this.validFiles;
    if (files.length === 0) return;

    this.store.startUpload();

    this.svc
      .uploadDocuments(files)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (event) => {
          if (event.type === HttpEventType.UploadProgress) {
            const pct = DocumentUploadService.uploadProgressPercent(event);
            if (pct !== null) {
              this.store.setUploadProgress(pct);
            }
          } else if (event.type === HttpEventType.Response && event.body) {
            this.store.handleUploadResult(event.body.files);
          }
        },
        error: (err: unknown) => {
          const status =
            err != null &&
            typeof err === 'object' &&
            'status' in err &&
            (err as { status: number }).status === 503;

          if (status) {
            this.store.setStorageUnavailable();
          } else {
            // Non-503 error: mark all as failed locally.
            const failedResults: UploadFileResult[] = files.map((f) => ({
              fileName: f.name,
              success: false,
              errorMessage: 'Upload failed. Please try again.',
            }));
            this.store.handleUploadResult(failedResults);
          }
        },
      });
  }

  /**
   * Re-queues only the failed files identified by name for retry.
   * Edge case — partial batch failure Retry action.
   */
  protected retryFile(fileName: string): void {
    this.store.retryFiles([fileName]);
  }

  /** Clears all upload state and resets the form. */
  protected reset(): void {
    this.store.resetUpload();
  }

  /**
   * Formats a byte count as a human-readable string (KB or MB).
   * Used in upload history table and file list.
   */
  protected formatFileSize(bytes: number): string {
    if (bytes >= 1024 * 1024) {
      return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    }
    return `${Math.round(bytes / 1024)} KB`;
  }

  private loadHistory(): void {
    this.store.loadHistoryStart();
    this.svc
      .getUploadHistory()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (history) => this.store.loadHistorySuccess(history),
        error: () => this.store.loadHistoryError(),
      });
  }
}
