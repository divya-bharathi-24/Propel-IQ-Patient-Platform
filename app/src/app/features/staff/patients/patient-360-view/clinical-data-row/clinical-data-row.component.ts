import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { NgClass, DatePipe } from '@angular/common';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatIconModule } from '@angular/material/icon';
import { ConfidenceBadgeComponent } from '../../../../../shared/components/confidence-badge/confidence-badge.component';
import { ClinicalItemDto } from '../../../../../core/services/patient-360-view.service';

/**
 * Renders a single extracted clinical field with:
 *  - Field name and value
 *  - Confidence badge (colour + icon for <80%, WCAG AA)
 *  - Citation icon that opens a tooltip with document name, page, and upload date
 *  - Low-confidence row highlight class (CSS visual flag, non-colour indicator via icon)
 *
 * US_041 AC-2: "each data element shows a source citation … and a confidence indicator;
 * fields below 80% confidence are visually flagged."
 */
@Component({
  selector: 'app-clinical-data-row',
  standalone: true,
  imports: [
    NgClass,
    DatePipe,
    MatTooltipModule,
    MatIconModule,
    ConfidenceBadgeComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="data-row"
      [ngClass]="{ 'low-confidence-row': item.isLowConfidence }"
      [attr.aria-label]="rowAriaLabel"
    >
      <span class="field-name">{{ item.fieldName }}</span>
      <span class="field-value">{{ item.value }}</span>

      <app-confidence-badge [confidence]="item.confidence" />

      @if (item.sources.length > 0) {
        <button
          type="button"
          class="citation-btn"
          [matTooltip]="citationTooltip"
          matTooltipPosition="above"
          [attr.aria-label]="'View source citation for ' + item.fieldName"
        >
          <mat-icon aria-hidden="true">info_outline</mat-icon>
        </button>
      }
    </div>
  `,
  styles: [
    `
      .data-row {
        display: grid;
        grid-template-columns: 200px 1fr auto auto;
        align-items: center;
        gap: 12px;
        padding: 10px 16px;
        border-bottom: 1px solid #e0e0e0;
        transition: background-color 0.15s ease;
      }

      .data-row:last-child {
        border-bottom: none;
      }

      .low-confidence-row {
        background-color: #fff8e1;
        border-left: 4px solid #f57c00;
      }

      .field-name {
        font-weight: 600;
        font-size: 0.875rem;
        color: #333;
      }

      .field-value {
        font-size: 0.875rem;
        color: #555;
        overflow-wrap: break-word;
      }

      .citation-btn {
        background: none;
        border: none;
        cursor: pointer;
        padding: 0;
        display: flex;
        align-items: center;
        color: #1976d2;
        flex-shrink: 0;
      }

      .citation-btn mat-icon {
        font-size: 18px;
        height: 18px;
        width: 18px;
      }

      .citation-btn:focus-visible {
        outline: 2px solid #1976d2;
        border-radius: 2px;
      }
    `,
  ],
})
export class ClinicalDataRowComponent {
  @Input({ required: true }) item!: ClinicalItemDto;

  protected get citationTooltip(): string {
    if (!this.item.sources.length) return '';
    return this.item.sources
      .map((s) => {
        const page = s.pageNumber != null ? ` — Page ${s.pageNumber}` : '';
        const date = s.uploadedAt
          ? ` (uploaded ${new Date(s.uploadedAt).toLocaleDateString()})`
          : '';
        return `${s.documentName}${page}${date}`;
      })
      .join('\n');
  }

  protected get rowAriaLabel(): string {
    const confidence = `${(this.item.confidence * 100).toFixed(0)}% confidence`;
    const flag = this.item.isLowConfidence ? ', priority review required' : '';
    return `${this.item.fieldName}: ${this.item.value} — ${confidence}${flag}`;
  }
}
