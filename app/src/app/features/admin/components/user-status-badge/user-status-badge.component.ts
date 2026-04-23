import { Component, Input } from '@angular/core';
import { MatChipsModule } from '@angular/material/chips';
import { UserStatus } from '../../models/admin.models';

/**
 * Renders a read-only status chip for a user account.
 * Active → green chip; Deactivated → grey chip.
 */
@Component({
  selector: 'app-user-status-badge',
  standalone: true,
  imports: [MatChipsModule],
  template: `
    <mat-chip-set aria-label="Account status">
      <mat-chip
        [class]="
          status === 'Active'
            ? 'status-chip--active'
            : 'status-chip--deactivated'
        "
        [attr.aria-label]="'Account status: ' + status"
        disableRipple
      >
        {{ status }}
      </mat-chip>
    </mat-chip-set>
  `,
  styles: [
    `
      .status-chip--active {
        background-color: #e8f5e9 !important;
        color: #2e7d32 !important;
        font-weight: 500;
      }

      .status-chip--deactivated {
        background-color: #f5f5f5 !important;
        color: #757575 !important;
        font-weight: 500;
      }
    `,
  ],
})
export class UserStatusBadgeComponent {
  @Input({ required: true }) status!: UserStatus;
}
