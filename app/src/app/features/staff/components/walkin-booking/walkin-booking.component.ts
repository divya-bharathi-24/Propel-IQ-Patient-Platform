import { Component, OnDestroy, inject } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { PatientSearchResultDto } from '../../models/walkin.models';
import { WalkInStore } from '../../state/walkin.store';
import { PatientSearchComponent } from '../patient-search/patient-search.component';
import { QuickCreatePatientFormComponent } from '../quick-create-patient/quick-create-patient-form.component';
import { effect } from '@angular/core';

/** Wizard step identifiers */
type WalkInStep = 'search' | 'create' | 'confirm';

@Component({
  selector: 'app-walkin-booking',
  standalone: true,
  imports: [
    MatButtonModule,
    MatIconModule,
    MatSnackBarModule,
    PatientSearchComponent,
    QuickCreatePatientFormComponent,
  ],
  templateUrl: './walkin-booking.component.html',
})
export class WalkInBookingComponent implements OnDestroy {
  protected readonly store = inject(WalkInStore);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  currentStep: WalkInStep = 'search';
  isAnonymous = false;

  constructor() {
    // Navigate to queue on successful submission
    effect(() => {
      if (
        this.store.actionState() === 'success' &&
        this.store.confirmedBooking()
      ) {
        const booking = this.store.confirmedBooking()!;
        if (booking.queuedOnly && this.currentStep !== 'confirm') {
          // Slot full — move to confirm step to show the queue banner
          this.currentStep = 'confirm';
          return;
        }
        if (this.currentStep === 'confirm') {
          this.snackBar.open('Walk-in registered', 'Dismiss', {
            duration: 4000,
          });
          this.store.clearState();
          this.router.navigate(['/staff/queue']);
        }
      }
    });
  }

  ngOnDestroy(): void {
    this.store.clearState();
  }

  // ── Step 1: PatientSearch event handlers ─────────────────────────────────

  onPatientSelected(patient: PatientSearchResultDto): void {
    this.isAnonymous = false;
    this.currentStep = 'confirm';
  }

  onCreateNewRequested(): void {
    this.isAnonymous = false;
    this.currentStep = 'create';
  }

  onAnonymousRequested(): void {
    this.isAnonymous = true;
    this.currentStep = 'confirm';
  }

  // ── Step 2: QuickCreateForm event handler ─────────────────────────────────

  onBackToSearch(): void {
    this.store.clearDuplicate();
    this.currentStep = 'search';
  }

  // ── Step 3: Confirm ────────────────────────────────────────────────────────

  onConfirm(): void {
    const booking = this.store.confirmedBooking();

    // If the walk-in was already submitted (e.g. 'create'/'link' mode), navigate
    if (booking && this.store.actionState() === 'success') {
      this.snackBar.open('Walk-in registered', 'Dismiss', { duration: 4000 });
      this.store.clearState();
      this.router.navigate(['/staff/queue']);
      return;
    }

    // Anonymous path — submit now
    if (this.isAnonymous) {
      this.store.submitWalkIn({ mode: 'anonymous' });
      return;
    }

    // Linked patient path — submit with patientId
    const selected = this.store.selectedPatient();
    if (selected) {
      this.store.submitWalkIn({ mode: 'link', patientId: selected.patientId });
    }
  }

  onCancelWizard(): void {
    this.store.clearState();
    this.router.navigate(['/staff/queue']);
  }
}
