import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MedicalCodeCardComponent } from '../medical-code-card/medical-code-card.component';
import { MedicalCodeReviewStore } from '../../store/medical-code-review.store';

/**
 * CptCodesPanelComponent — US_043 AC-1
 *
 * Right panel in the side-by-side code review layout.
 * Renders a `MedicalCodeCardComponent` for each CPT suggestion from the store.
 * Dispatches confirm/reject actions directly to the store on card output events.
 *
 * WCAG 2.2 AA:
 *  - Section has an ARIA `region` landmark with a descriptive label.
 *  - Empty state uses a non-colour indicator (icon + text).
 */
@Component({
  selector: 'app-cpt-codes-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDividerModule, MatIconModule, MedicalCodeCardComponent],
  template: `
    <section class="codes-panel" aria-label="CPT procedure codes" role="region">
      <header class="panel-header">
        <mat-icon aria-hidden="true">medical_services</mat-icon>
        <h2 class="panel-title">CPT Codes</h2>
        <span
          class="panel-count"
          [attr.aria-label]="store.cptCodes().length + ' CPT codes'"
        >
          ({{ store.cptCodes().length }})
        </span>
      </header>

      <mat-divider />

      @if (store.cptCodes().length === 0) {
        <p class="empty-state" role="status">
          <mat-icon aria-hidden="true">info_outline</mat-icon>
          No CPT codes suggested for this encounter.
        </p>
      }

      @for (suggestion of store.cptCodes(); track suggestion.codeId) {
        <app-medical-code-card
          [suggestion]="suggestion"
          [decision]="
            store.decisions()[suggestion.codeId] ?? { status: 'Pending' }
          "
          (confirmed)="store.confirmCode(suggestion.codeId)"
          (rejected)="store.rejectCode(suggestion.codeId, $event)"
        />
      }
    </section>
  `,
  styles: [
    `
      .codes-panel {
        display: flex;
        flex-direction: column;
        gap: 4px;
      }

      .panel-header {
        display: flex;
        align-items: center;
        gap: 8px;
        padding: 12px 0 8px;
      }

      .panel-title {
        margin: 0;
        font-size: 1.1rem;
        font-weight: 600;
      }

      .panel-count {
        font-size: 0.875rem;
        color: rgba(0, 0, 0, 0.6);
      }

      .empty-state {
        display: flex;
        align-items: center;
        gap: 6px;
        padding: 16px 0;
        color: rgba(0, 0, 0, 0.6);
        font-size: 0.875rem;
      }
    `,
  ],
})
export class CptCodesPanelComponent {
  protected readonly store = inject(MedicalCodeReviewStore);
}
