import {
  ChangeDetectionStrategy,
  Component,
  Input,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { CalendarSyncButtonComponent } from '../../../../shared/components/calendar-sync-button/calendar-sync-button.component';
import {
  CancelConfirmationDialogComponent,
  CancelDialogData,
} from '../cancel-dialog/cancel-confirmation-dialog.component';
import { AppointmentSummary } from '../../models/appointment-management.models';
import { WaitlistStore } from '../../state/waitlist.store';
import { WaitlistEntryDto } from '../../models/waitlist.models';

@Component({
  selector: 'app-upcoming-appointment-card',
  standalone: true,
  imports: [
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    CalendarSyncButtonComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './upcoming-appointment-card.component.html',
})
export class UpcomingAppointmentCardComponent implements OnInit {
  /** The appointment data to display. */
  @Input({ required: true }) appointment!: AppointmentSummary;

  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  protected readonly waitlistStore = inject(WaitlistStore);

  /** Tracks inline confirmation visibility for Remove Preference action. */
  protected readonly showRemoveConfirmation = signal(false);

  /**
   * True when the appointment date is strictly in the future.
   * Cancel and Reschedule buttons are only shown when this is true,
   * preventing unnecessary API calls for past appointments (AC edge case).
   */
  protected readonly isFuture = computed(
    () => new Date(this.appointment.date) > new Date(),
  );

  /** Active WaitlistEntry linked to this appointment, if any. */
  protected readonly waitlistEntry = computed<WaitlistEntryDto | null>(() => {
    const entries = this.waitlistStore.entries();
    return (
      entries.find(
        (e) => e.appointmentId === this.appointment.id && e.status === 'Active',
      ) ?? null
    );
  });

  ngOnInit(): void {
    // Load waitlist entries on first render if not already loaded.
    if (this.waitlistStore.loadingState() === 'idle') {
      this.waitlistStore.loadEntries();
    }
  }

  protected openCancelDialog(): void {
    const data: CancelDialogData = { appointment: this.appointment };
    this.dialog.open(CancelConfirmationDialogComponent, {
      data,
      width: '480px',
      disableClose: true,
      ariaLabel: 'Cancel appointment confirmation',
    });
  }

  protected onReschedule(): void {
    this.router.navigate(['/appointments', this.appointment.id, 'reschedule'], {
      state: { appointment: this.appointment },
    });
  }

  protected onRemovePreference(): void {
    this.showRemoveConfirmation.set(true);
  }

  protected onConfirmRemovePreference(): void {
    const entry = this.waitlistEntry();
    if (!entry) return;
    this.waitlistStore.cancelPreference(entry.id);
    this.showRemoveConfirmation.set(false);
    this.snackBar.open('Waitlist preference removed', 'Dismiss', {
      duration: 4000,
    });
  }

  protected onCancelRemovePreference(): void {
    this.showRemoveConfirmation.set(false);
  }
}
