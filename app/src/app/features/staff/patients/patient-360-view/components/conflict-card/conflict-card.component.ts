import {
  ChangeDetectionStrategy,
  Component,
  Input,
  inject,
} from '@angular/core';
import { NgClass } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import {
  DataConflictDto,
} from '../../../../../../core/services/patient-360-view.service';
import { ClinicalConflictStore } from '../../store/clinical-conflict.store';
import {
  ConflictResolutionEvent,
  ConflictResolutionFormComponent,
} from '../conflict-resolution-form/conflict-resolution-form.component';

/**
 * ConflictCardComponent — US_044 AC-2 / AC-3
 *
 * Displays a single data conflict side-by-side with:
 *  - Severity badge: red "Critical" or amber "Warning" chip.
 *  - Left column: value1 + sourceDoc1.
 *  - Right column: value2 + sourceDoc2.
 *  - Embedded ConflictResolutionFormComponent (only shown while Unresolved).
 *  - Resolved state: shows a green confirmation banner with the chosen value.
 *
 * Resolution events are dispatched directly to ClinicalConflictStore,
 * avoiding prop-drilling through parent components.
 *
 * WCAG 2.2 AA:
 *  - Card has aria-label describing the conflict field and severity (4.1.2).
 *  - Severity chip is backed by colour + text (1.4.1).
 *  - Resolved state announced via role="status" (4.1.3).
 */
@Component({
  selector: 'app-conflict-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgClass,
    MatCardModule,
    MatChipsModule,
    MatIconModule,
    ConflictResolutionFormComponent,
  ],
  template: `
    <mat-card
      class="conflict-card"
      [ngClass]="cardClass"
      [attr.aria-label]="
        conflict.fieldName + ' conflict — ' + conflict.severity
      "
    >
      <!-- Card header: field name + severity badge -->
      <mat-card-header class="card-header">
        <mat-card-title class="field-title">
          <mat-icon aria-hidden="true" class="field-icon">warning_amber</mat-icon>
          {{ conflict.fieldName }}
        </mat-card-title>
        <mat-chip
          class="severity-chip"
          [ngClass]="chipClass"
          [attr.aria-label]="conflict.severity + ' conflict'"
          disableRipple
        >
          {{ conflict.severity }}
        </mat-chip>
      </mat-card-header>

      <mat-card-content class="card-content">
        @if (conflict.resolutionStatus === 'Resolved') {
          <!-- Resolved state (AC-3) -->
          <div class="resolved-banner" role="status" aria-live="polite">
            <mat-icon aria-hidden="true" class="resolved-icon">check_circle</mat-icon>
            <span>
              Resolved — authoritative value:
              <strong>{{ conflict.resolvedValue }}</strong>
            </span>
          </div>
        } @else {
          <!-- Side-by-side conflict values (AC-2) -->
          <div class="values-grid">
            <div class="value-col">
              <span class="source-label">{{ conflict.sourceDoc1 }}</span>
              <span class="value-text">{{ conflict.value1 }}</span>
            </div>

            <div class="vs-divider" aria-hidden="true">vs</div>

            <div class="value-col">
              <span class="source-label">{{ conflict.sourceDoc2 }}</span>
              <span class="value-text">{{ conflict.value2 }}</span>
            </div>
          </div>

          <!-- Resolution form (AC-3) -->
          <app-conflict-resolution-form
            [conflict]="conflict"
            (resolved)="onResolved($event)"
          />
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: [
    `
      .conflict-card {
        margin: 4px 0 8px;
        border-left: 4px solid transparent;
        transition: background-color 0.2s ease;
      }

      .conflict-card.critical-card {
        border-left-color: #d32f2f;
      }

      .conflict-card.warning-card {
        border-left-color: #f57c00;
      }

      .card-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding-bottom: 4px;
      }

      .field-title {
        display: flex;
        align-items: center;
        gap: 6px;
        font-size: 0.9rem;
        font-weight: 600;
      }

      .field-icon {
        font-size: 1rem;
        height: 1rem;
        width: 1rem;
        color: #888;
      }

      .severity-chip {
        font-size: 0.72rem;
        font-weight: 700;
        height: 22px;
        min-height: 22px;
      }

      .severity-chip.chip-critical {
        background-color: #b71c1c !important;
        color: #fff !important;
      }

      .severity-chip.chip-warning {
        background-color: #e65100 !important;
        color: #fff !important;
      }

      .card-content {
        padding-top: 8px;
      }

      .values-grid {
        display: grid;
        grid-template-columns: 1fr auto 1fr;
        gap: 12px;
        align-items: start;
        padding: 8px 0;
      }

      .value-col {
        display: flex;
        flex-direction: column;
        gap: 4px;
      }

      .source-label {
        font-size: 0.72rem;
        font-weight: 700;
        color: #666;
        text-transform: uppercase;
        letter-spacing: 0.04em;
      }

      .value-text {
        font-size: 0.875rem;
        color: #212121;
        word-break: break-word;
      }

      .vs-divider {
        align-self: center;
        font-size: 0.75rem;
        font-weight: 700;
        color: #aaa;
        text-transform: uppercase;
      }

      .resolved-banner {
        display: flex;
        align-items: center;
        gap: 8px;
        padding: 8px 12px;
        background-color: #e8f5e9;
        border-radius: 4px;
        font-size: 0.875rem;
        color: #1b5e20;
      }

      .resolved-icon {
        color: #2e7d32;
        font-size: 1.1rem;
        height: 1.1rem;
        width: 1.1rem;
      }
    `,
  ],
})
export class ConflictCardComponent {
  @Input({ required: true }) conflict!: DataConflictDto;

  private readonly conflictStore = inject(ClinicalConflictStore);

  protected get cardClass(): string {
    return this.conflict.severity === 'Critical' ? 'critical-card' : 'warning-card';
  }

  protected get chipClass(): string {
    return this.conflict.severity === 'Critical' ? 'chip-critical' : 'chip-warning';
  }

  protected onResolved(event: ConflictResolutionEvent): void {
    this.conflictStore.resolveConflict({
      conflictId: event.conflictId,
      payload: { resolvedValue: event.resolvedValue },
    });
  }
}
