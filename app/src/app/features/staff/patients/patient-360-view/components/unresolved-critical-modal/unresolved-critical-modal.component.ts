import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import {
  MAT_DIALOG_DATA,
  MatDialogModule,
  MatDialogRef,
} from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { DataConflictDto } from '../../../../../../core/services/patient-360-view.service';

export interface UnresolvedCriticalBlockerModalData {
  /** All unresolved Critical conflicts to list in the modal. */
  conflicts: DataConflictDto[];
}

/**
 * UnresolvedCriticalBlockerModalComponent — US_044 AC-4
 *
 * MatDialog opened when Staff click "Verify Profile" and unresolved Critical
 * conflicts remain. Lists each blocking conflict so staff know what to resolve.
 *
 * Close action returns staff to the 360-view to resolve conflicts;
 * no navigate-away action is triggered.
 *
 * WCAG 2.2 AA:
 *  - Dialog has aria-labelledby on the title (4.1.2).
 *  - Conflict list uses a semantic <ul> (1.3.1).
 *  - Close button is the sole focus trap exit (2.1.2).
 */
@Component({
  selector: 'app-unresolved-critical-blocker-modal',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatIconModule],
  template: `
    <h2 mat-dialog-title id="blocker-dialog-title" class="dialog-title">
      <mat-icon aria-hidden="true" class="block-icon">block</mat-icon>
      Verification Blocked
    </h2>

    <mat-dialog-content class="dialog-content">
      <p class="dialog-intro">
        The following <strong>Critical</strong> conflicts must be resolved
        before verifying the profile:
      </p>
      <ul class="conflict-list" aria-label="Unresolved critical conflicts">
        @for (conflict of data.conflicts; track conflict.conflictId) {
          <li class="conflict-item">
            <strong>{{ conflict.fieldName }}</strong>
            <span class="conflict-values">
              — {{ conflict.value1 }} ({{ conflict.sourceDoc1 }}) vs
              {{ conflict.value2 }} ({{ conflict.sourceDoc2 }})
            </span>
          </li>
        }
      </ul>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button
        mat-raised-button
        color="primary"
        (click)="onClose()"
        cdkFocusInitial
        aria-label="Close and return to resolve conflicts"
      >
        Go Back and Resolve
      </button>
    </mat-dialog-actions>
  `,
  styles: [
    `
      .dialog-title {
        display: flex;
        align-items: center;
        gap: 8px;
        font-size: 1.1rem;
        color: #b71c1c;
      }

      .block-icon {
        color: #b71c1c;
        font-size: 1.3rem;
        height: 1.3rem;
        width: 1.3rem;
      }

      .dialog-content {
        min-width: 380px;
        max-width: 540px;
      }

      .dialog-intro {
        font-size: 0.875rem;
        color: #424242;
        margin-bottom: 12px;
      }

      .conflict-list {
        margin: 0;
        padding-left: 20px;
        font-size: 0.875rem;
        color: #212121;
      }

      .conflict-item {
        margin-bottom: 8px;
        line-height: 1.5;
      }

      .conflict-values {
        color: #666;
        font-size: 0.8rem;
      }
    `,
  ],
})
export class UnresolvedCriticalBlockerModalComponent {
  protected readonly data =
    inject<UnresolvedCriticalBlockerModalData>(MAT_DIALOG_DATA);
  private readonly dialogRef =
    inject<MatDialogRef<UnresolvedCriticalBlockerModalComponent>>(MatDialogRef);

  protected onClose(): void {
    this.dialogRef.close();
  }
}
