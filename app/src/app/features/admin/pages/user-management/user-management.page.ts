import { Component, OnInit, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AuthService } from '../../../auth/services/auth.service';
import {
  AdminUser,
  ReAuthModalResult,
  UserRole,
} from '../../models/admin.models';
import { AdminService } from '../../services/admin.service';
import { AdminUserStore } from '../../store/admin-user.store';
import {
  UserFormDialogComponent,
  UserFormDialogResult,
} from '../../components/user-form-dialog/user-form-dialog.component';
import { ReauthenticationModalComponent } from '../../components/reauth-modal/reauth-modal.component';
import { UserTableComponent } from '../../components/user-table/user-table.component';

/**
 * Routed page for the Admin > User Management section (US_045 / US_046).
 *
 * Responsibilities:
 * - Loads user list on init via AdminUserStore.
 * - Opens UserFormDialogComponent in Create or Edit mode.
 * - Delegates deactivation to AdminUserStore after re-authentication via
 *   ReauthenticationModalComponent (FR-062).
 * - Handles role change events from UserTableComponent: triggers
 *   ReauthenticationModalComponent for elevation to Admin; submits directly
 *   for downgrade to Staff.
 * - Calls AdminService.resendCredentialEmail and shows result snackbar.
 * - Displays snackbar warning when createUser API returns emailSent = false.
 */
@Component({
  selector: 'app-user-management-page',
  standalone: true,
  imports: [
    MatButtonModule,
    MatDialogModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    UserTableComponent,
  ],
  templateUrl: './user-management.page.html',
  styleUrl: './user-management.page.scss',
})
export class UserManagementPageComponent implements OnInit {
  protected readonly store = inject(AdminUserStore);
  private readonly adminService = inject(AdminService);
  private readonly authService = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly currentAdminId = this.authService.currentUserId;

  ngOnInit(): void {
    this.store.loadUsers();
  }

  openCreateUserDialog(): void {
    const dialogRef = this.dialog.open<
      UserFormDialogComponent,
      undefined,
      UserFormDialogResult | undefined
    >(UserFormDialogComponent, {
      width: '480px',
      disableClose: true,
      data: {},
      ariaLabelledBy: 'user-form-dialog-title',
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (!result) return;

      this.store
        .createUser({
          name: result.name,
          email: result.email,
          role: result.role,
        })
        .subscribe({
          next: (response) => {
            if (response.emailSent === false) {
              this.snackBar.open(
                'Account created — Email delivery failed. Use Resend on the user row.',
                'Dismiss',
                { duration: 8000, panelClass: 'snack-warn' },
              );
            } else {
              this.snackBar.open(
                'Account created. Credential setup email sent.',
                'Dismiss',
                { duration: 5000 },
              );
            }
          },
          error: (err: { status: number; message: string }) => {
            if (err.status === 403) {
              this.snackBar.open('Insufficient permissions.', 'Dismiss', {
                duration: 5000,
              });
            } else {
              this.snackBar.open(
                'Failed to create user. Please try again.',
                'Dismiss',
                { duration: 5000 },
              );
            }
          },
        });
    });
  }

  onEditUser(user: AdminUser): void {
    const dialogRef = this.dialog.open<
      UserFormDialogComponent,
      { user: AdminUser },
      UserFormDialogResult | undefined
    >(UserFormDialogComponent, {
      width: '480px',
      disableClose: true,
      data: { user },
      ariaLabelledBy: 'user-form-dialog-title',
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (!result) return;

      this.store
        .updateUser(user.id, { name: result.name, role: result.role })
        .subscribe({
          next: () => {
            this.snackBar.open('Account updated.', 'Dismiss', {
              duration: 4000,
            });
          },
          error: () => {
            this.snackBar.open(
              'Failed to update user. Please try again.',
              'Dismiss',
              { duration: 5000 },
            );
          },
        });
    });
  }

  onDeactivateUser(user: AdminUser): void {
    const dialogRef = this.dialog.open<
      ReauthenticationModalComponent,
      { actionLabel: string },
      ReAuthModalResult | undefined
    >(ReauthenticationModalComponent, {
      width: '420px',
      disableClose: true,
      data: { actionLabel: 'deactivate this account' },
      ariaLabelledBy: 'reauth-dialog-title',
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (!result || result.status === 'cancelled') return;

      if (result.status === 'timeout') {
        this.snackBar.open('Action timed out — please try again', 'Dismiss', {
          duration: 5000,
        });
        return;
      }

      this.store.deactivateUser(user.id).subscribe({
        next: () => {
          this.snackBar.open(
            `Account for ${user.name} has been deactivated.`,
            'Dismiss',
            { duration: 5000 },
          );
        },
        error: () => {
          this.snackBar.open(
            'Failed to deactivate account. Please try again.',
            'Dismiss',
            { duration: 5000 },
          );
        },
      });
    });
  }

  onChangeRole(event: { user: AdminUser; newRole: UserRole }): void {
    const { user, newRole } = event;

    if (newRole === 'Admin') {
      const dialogRef = this.dialog.open<
        ReauthenticationModalComponent,
        { actionLabel: string },
        ReAuthModalResult | undefined
      >(ReauthenticationModalComponent, {
        width: '420px',
        disableClose: true,
        data: { actionLabel: 'elevate this account to Admin' },
        ariaLabelledBy: 'reauth-dialog-title',
      });

      dialogRef.afterClosed().subscribe((result) => {
        if (!result || result.status === 'cancelled') {
          this.store.loadUsers();
          return;
        }

        if (result.status === 'timeout') {
          this.snackBar.open('Action timed out — please try again', 'Dismiss', {
            duration: 5000,
          });
          this.store.loadUsers();
          return;
        }

        this.store
          .updateUserRole(user.id, newRole, result.reAuthToken)
          .subscribe({
            next: () => {
              this.snackBar.open(
                'Role updated — change takes effect on next login',
                'Dismiss',
                { duration: 5000 },
              );
            },
            error: () => {
              this.snackBar.open(
                'Failed to update role. Please try again.',
                'Dismiss',
                { duration: 5000 },
              );
              this.store.loadUsers();
            },
          });
      });
    } else {
      this.store.updateUserRole(user.id, newRole).subscribe({
        next: () => {
          this.snackBar.open(
            'Role updated — change takes effect on next login',
            'Dismiss',
            { duration: 5000 },
          );
        },
        error: () => {
          this.snackBar.open(
            'Failed to update role. Please try again.',
            'Dismiss',
            { duration: 5000 },
          );
          this.store.loadUsers();
        },
      });
    }
  }

  onResendEmail(user: AdminUser): void {
    this.adminService.resendCredentialEmail(user.id).subscribe({
      next: () => {
        this.snackBar.open(
          `Credential email resent to ${user.email}.`,
          'Dismiss',
          { duration: 5000 },
        );
      },
      error: () => {
        this.snackBar.open(
          'Failed to resend credential email. Please try again.',
          'Dismiss',
          { duration: 5000 },
        );
      },
    });
  }
}
