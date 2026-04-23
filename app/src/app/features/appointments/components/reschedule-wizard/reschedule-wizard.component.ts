import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  ViewChild,
  effect,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { MatStepper, MatStepperModule } from '@angular/material/stepper';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { DatePipe } from '@angular/common';
import { SlotPickerComponent } from '../slot-picker/slot-picker.component';
import { SlotAvailabilityStore } from '../../state/slot-availability.store';
import { AppointmentManagementStore } from '../../state/appointment-management.store';
import { SlotDto } from '../../models/slot.models';
import { AppointmentSummary } from '../../models/appointment-management.models';

@Component({
  selector: 'app-reschedule-wizard',
  standalone: true,
  imports: [
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatProgressSpinnerModule,
    MatStepperModule,
    SlotPickerComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './reschedule-wizard.component.html',
})
export class RescheduleWizardComponent implements OnDestroy {
  @ViewChild('stepper') private readonly stepper!: MatStepper;

  protected readonly store = inject(AppointmentManagementStore);
  protected readonly slotStore = inject(SlotAvailabilityStore);

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  /** ID of the appointment being rescheduled (from route param). */
  protected readonly appointmentId =
    this.route.snapshot.paramMap.get('id') ?? '';

  /**
   * Original appointment summary passed via router state.
   * Fallback to placeholder when navigating directly (e.g. deep-link in tests).
   */
  protected readonly originalAppointment = signal<AppointmentSummary | null>(
    (
      this.router.getCurrentNavigation()?.extras.state as {
        appointment?: AppointmentSummary;
      }
    )?.appointment ?? null,
  );

  /** The slot the user has selected in Step 1. */
  protected readonly selectedSlot = signal<SlotDto | null>(null);

  constructor() {
    // On reschedule success: navigate to /appointments and show snackbar.
    effect(() => {
      if (this.store.actionState() === 'success') {
        this.snackBar.open(
          'Appointment rescheduled. Confirmation email sent.',
          'Dismiss',
          { duration: 5000, panelClass: ['snack-success'] },
        );
        this.router.navigate(['/appointments']);
      }
    });

    // On 409 conflict: push conflict message into SlotAvailabilityStore and go back to step 1.
    effect(() => {
      const conflict = this.store.conflictMessage();
      if (conflict) {
        this.slotStore.setConflict(conflict);
        this.store.clearMessages();
        // Navigate stepper back to step index 0.
        if (this.stepper) {
          this.stepper.selectedIndex = 0;
        }
      }
    });
  }

  protected get specialtyId(): string {
    return this.originalAppointment()?.specialtyId ?? '';
  }

  protected onSlotSelected(slot: SlotDto): void {
    this.selectedSlot.set(slot);
    // Advance to confirmation step.
    if (this.stepper) {
      this.stepper.next();
    }
  }

  protected onConfirmReschedule(): void {
    const slot = this.selectedSlot();
    if (!slot) return;

    this.store.rescheduleAppointment({
      id: this.appointmentId,
      dto: {
        newSlotDate: slot.date,
        newSlotStart: slot.timeSlotStart,
        newSlotEnd: slot.timeSlotEnd,
        specialtyId: slot.specialtyId,
      },
    });
  }

  protected onCancel(): void {
    this.router.navigate(['/appointments']);
  }

  ngOnDestroy(): void {
    this.store.clearMessages();
    this.slotStore.reset();
  }
}
