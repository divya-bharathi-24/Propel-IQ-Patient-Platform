import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
} from '@angular/core';
import { NgClass } from '@angular/common';
import { DocumentProcessingStatus } from '../../../features/patient/dashboard/patient-dashboard.model';

const STATUS_CLASS_MAP: Record<DocumentProcessingStatus, string> = {
  Pending: 'chip-pending',
  Processing: 'chip-processing',
  Completed: 'chip-completed',
  Failed: 'chip-failed',
};

@Component({
  selector: 'app-document-status-chip',
  standalone: true,
  imports: [NgClass],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="status-chip"
      [ngClass]="chipClass"
      [attr.aria-label]="'Document status: ' + processingStatus"
    >
      @if (processingStatus === 'Failed') {
        Processing Failed
      } @else {
        {{ processingStatus }}
      }
    </span>
    @if (processingStatus === 'Failed') {
      <button
        type="button"
        class="retry-link"
        (click)="retryClicked.emit()"
        aria-label="Retry upload"
      >
        Retry Upload
      </button>
    }
  `,
  styles: [
    `
      :host {
        display: inline-flex;
        align-items: center;
        gap: 8px;
      }

      .status-chip {
        display: inline-block;
        padding: 2px 10px;
        border-radius: 12px;
        font-size: 0.75rem;
        font-weight: 600;
        letter-spacing: 0.03em;
      }

      .chip-pending {
        background-color: #fff8e1;
        color: #f57f17;
      }

      .chip-processing {
        background-color: #e3f2fd;
        color: #1565c0;
      }

      .chip-completed {
        background-color: #e8f5e9;
        color: #2e7d32;
      }

      .chip-failed {
        background-color: #fce4ec;
        color: #b71c1c;
      }

      .retry-link {
        background: none;
        border: none;
        padding: 0;
        font-size: 0.75rem;
        font-weight: 600;
        color: #1565c0;
        cursor: pointer;
        text-decoration: underline;
      }

      .retry-link:hover {
        color: #0d47a1;
      }
    `,
  ],
})
export class DocumentStatusChipComponent {
  @Input({ required: true }) processingStatus!: DocumentProcessingStatus;
  @Output() retryClicked = new EventEmitter<void>();

  get chipClass(): string {
    return STATUS_CLASS_MAP[this.processingStatus] ?? 'chip-pending';
  }
}
