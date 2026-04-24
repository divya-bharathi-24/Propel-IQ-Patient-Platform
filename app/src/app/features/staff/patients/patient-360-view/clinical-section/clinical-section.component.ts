import {
  ChangeDetectionStrategy,
  Component,
  Input,
  signal,
} from '@angular/core';
import { MatExpansionModule } from '@angular/material/expansion';
import { ClinicalDataRowComponent } from '../clinical-data-row/clinical-data-row.component';
import {
  ClinicalSectionDto,
  DataConflictDto,
  ConflictSeverity,
  SectionType,
} from '../../../../../core/services/patient-360-view.service';
import { ConflictHighlightDirective } from '../directives/conflict-highlight.directive';
import { ConflictCardComponent } from '../components/conflict-card/conflict-card.component';

/** Human-readable labels for each section type (AC-1). */
const SECTION_LABELS: Record<SectionType, string> = {
  Vitals: 'Vitals',
  Medications: 'Medications',
  Diagnoses: 'Diagnoses',
  Allergies: 'Allergies',
  Immunizations: 'Immunizations',
  SurgicalHistory: 'Surgical History',
};

/**
 * Expandable panel for a single clinical data section (US_041, AC-1).
 *
 * Wraps a `<mat-expansion-panel>` with:
 *  - Section title and item count in the panel header.
 *  - One `<app-clinical-data-row>` per item — showing field, value, confidence, and citation.
 *  - `panelOpen` signal tracks open/closed state for accessibility handoff.
 *
 * Items with `isLowConfidence = true` are rendered first so priority-review fields
 * are immediately visible when the panel opens (AC-2).
 */
@Component({
  selector: 'app-clinical-section',
  standalone: true,
  imports: [
    MatExpansionModule,
    ClinicalDataRowComponent,
    ConflictHighlightDirective,
    ConflictCardComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <mat-expansion-panel
      class="section-panel"
      (opened)="panelOpen.set(true)"
      (closed)="panelOpen.set(false)"
      [attr.aria-label]="
        sectionLabel + ' section, ' + section.items.length + ' items'
      "
    >
      <mat-expansion-panel-header>
        <mat-panel-title class="section-title">
          {{ sectionLabel }}
        </mat-panel-title>
        <mat-panel-description>
          {{ section.items.length }}
          {{ section.items.length === 1 ? 'item' : 'items' }}
          @if (lowConfidenceCount > 0) {
            <span
              class="low-confidence-chip"
              [attr.aria-label]="lowConfidenceCount + ' require priority review'"
            >
              {{ lowConfidenceCount }} flagged
            </span>
          }
        </mat-panel-description>
      </mat-expansion-panel-header>

      <div
        class="section-body"
        role="list"
        [attr.aria-label]="sectionLabel + ' data'"
      >
        @for (item of sortedItems; track item.fieldName) {
          <div
            role="listitem"
            [appConflictHighlight]="getConflictSeverity(item.fieldName)"
          >
            <app-clinical-data-row [item]="item" />
            @if (getConflict(item.fieldName); as conflict) {
              <app-conflict-card [conflict]="conflict" />
            }
          </div>
        }
        @if (section.items.length === 0) {
          <p class="empty-message" aria-live="polite">
            No data extracted for this section.
          </p>
        }
      </div>
    </mat-expansion-panel>
  `,
  styles: [
    `
      .section-panel {
        margin-bottom: 8px;
      }

      .section-title {
        font-weight: 600;
        font-size: 0.95rem;
      }

      .low-confidence-chip {
        display: inline-flex;
        align-items: center;
        margin-left: 8px;
        padding: 1px 8px;
        border-radius: 10px;
        background-color: #fff3e0;
        color: #e65100;
        font-size: 0.7rem;
        font-weight: 700;
        border: 1px solid #ffcc80;
      }

      .section-body {
        padding: 0;
      }

      .empty-message {
        padding: 12px 16px;
        color: #757575;
        font-style: italic;
        font-size: 0.875rem;
      }
    `,
  ],
})
export class ClinicalSectionComponent {
  @Input({ required: true }) section!: ClinicalSectionDto;
  /** Conflicts from ClinicalConflictStore, filtered to fields in this section. */
  @Input() conflicts: DataConflictDto[] = [];

  /** Tracks panel open/closed state for parent accessibility announcements. */
  protected readonly panelOpen = signal(false);

  protected get sectionLabel(): string {
    return SECTION_LABELS[this.section.sectionType] ?? this.section.sectionType;
  }

  protected get lowConfidenceCount(): number {
    return this.section.items.filter((i) => i.isLowConfidence).length;
  }

  /** Low-confidence items rendered first so priority flags are immediately visible (AC-2). */
  protected get sortedItems(): typeof this.section.items {
    return [...this.section.items].sort((a, b) =>
      a.isLowConfidence === b.isLowConfidence ? 0 : a.isLowConfidence ? -1 : 1,
    );
  }

  /** Returns the DataConflictDto for a field, or undefined if no conflict exists. */
  protected getConflict(fieldName: string): DataConflictDto | undefined {
    return this.conflicts.find((c) => c.fieldName === fieldName);
  }

  /** Returns the severity of a conflict for a field, or null if none. */
  protected getConflictSeverity(fieldName: string): ConflictSeverity | null {
    return this.getConflict(fieldName)?.severity ?? null;
  }
}
