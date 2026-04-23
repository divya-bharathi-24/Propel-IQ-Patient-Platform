import {
  ChangeDetectionStrategy,
  Component,
  Inject,
  OnDestroy,
  effect,
  inject,
} from '@angular/core';
import {
  MAT_DIALOG_DATA,
  MatDialogModule,
  MatDialogRef,
} from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { DatePipe } from '@angular/common';
import { AppointmentManagementStore } from '../../state/appointment-management.store';
import { AppointmentSummary } from '../../models/appointment-management.models';

export interface CancelDialogData {
  appointment: AppointmentSummary;
}

@Component({
  selector: 'app-cancel-confirmation-dialog',
  standalone: true,
  imports: [
    DatePipe,
    MatButtonModule,
    MatDialogModule,
    MatProgressSpinnerModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './cancel-confirmation-dialog.component.html',
})
export class CancelConfirmationDialogComponent implements OnDestroy {
  protected readonly store = inject(AppointmentManagementStore);
  private readonly snackBar = inject(MatSnackBar);
  private readonly dialogRef = inject(
    MatDialogRef<CancelConfirmationDialogComponent>,
  );

  constructor(@Inject(MAT_DIALOG_DATA) public readonly data: CancelDialogData) {
    // React to success state: close dialog and show snackbar.
    effect(() => {
      if (this.store.actionState() === 'success') {
        this.snackBar.open(
          'Appointment cancelled. Confirmation email will be suppressed.',
          'Dismiss',
          { duration: 5000, panelClass: ['snack-success'] },
        );
        this.dialogRef.close(true);
      }
    });
  }

  protected onConfirm(): void {
    this.store.cancelAppointment(this.data.appointment.id);
  }

  protected onKeep(): void {
    this.dialogRef.close(false);
  }

  ngOnDestroy(): void {
    this.store.clearMessages();
  }
}
