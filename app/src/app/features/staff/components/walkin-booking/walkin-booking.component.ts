import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import {
  PatientSearchResultDto,
  WalkInBookingDto,
} from '../../models/walkin.models';
import { WalkInStore } from '../../state/walkin.store';
import { PatientSearchComponent } from '../patient-search/patient-search.component';
import { QuickCreatePatientFormComponent } from '../quick-create-patient/quick-create-patient-form.component';
import { SpecialtyService } from '../../../appointments/services/specialty.service';
import { SpecialtyDto } from '../../../appointments/models/slot.models';
import { effect } from '@angular/core';
import { AuthService } from '../../../auth/services/auth.service';

/** Wizard step identifiers */
type WalkInStep = 'search' | 'create' | 'confirm';

@Component({
  selector: 'app-walkin-booking',
  standalone: true,
  imports: [
    RouterLink,
    RouterLinkActive,
    ReactiveFormsModule,
    PatientSearchComponent,
    QuickCreatePatientFormComponent,
  ],
  templateUrl: './walkin-booking.component.html',
  styleUrls: ['./walkin-booking.component.scss'],
})
export class WalkInBookingComponent implements OnInit, OnDestroy {
  protected readonly store = inject(WalkInStore);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);
  private readonly specialtyService = inject(SpecialtyService);
  readonly authService = inject(AuthService);

  currentStep: WalkInStep = 'search';
  isAnonymous = false;
  specialties = signal<SpecialtyDto[]>([]);

  /** Today's date in YYYY-MM-DD for the min date attribute */
  readonly todayStr = new Date().toISOString().split('T')[0];

  confirmForm = this.fb.group({
    specialtyId: ['', Validators.required],
    date: [this.todayStr, Validators.required],
  });

  constructor() {
    // Navigate to confirm step on successful submission, then to queue
    effect(() => {
      if (
        this.store.actionState() === 'success' &&
        this.store.confirmedBooking()
      ) {
        if (this.currentStep !== 'confirm') {
          // Always advance to confirm step to show booking result (queue banner or summary)
          this.currentStep = 'confirm';
          return;
        }
        if (this.currentStep === 'confirm') {
          this.router.navigate(['/staff/queue']);
        }
      }
    });
  }

  ngOnInit(): void {
    this.specialtyService.getSpecialties().subscribe({
      next: (list) => this.specialties.set(list),
      error: () => {
        /* non-critical — user can still type the specialty manually */
      },
    });
  }

  ngOnDestroy(): void {
    this.store.clearState();
  }

  logout(): void {
    this.authService.logout();
  }

  // ── Step 1: PatientSearch event handlers ─────────────────────────────────

  onPatientSelected(patient: PatientSearchResultDto): void {
    this.store.selectPatient(patient);
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
      this.store.clearState();
      this.router.navigate(['/staff/queue']);
      return;
    }

    if (this.confirmForm.invalid) {
      this.confirmForm.markAllAsTouched();
      return;
    }

    const specialtyId = this.confirmForm.value.specialtyId!;
    const date = this.confirmForm.value.date!;

    const basePayload: Pick<WalkInBookingDto, 'specialtyId' | 'date'> = {
      specialtyId,
      date,
    };

    // Anonymous path — submit now
    if (this.isAnonymous) {
      this.store.submitWalkIn({ mode: 'anonymous', ...basePayload });
      return;
    }

    // Linked patient path — submit with patientId
    const selected = this.store.selectedPatient();
    if (selected) {
      this.store.submitWalkIn({
        mode: 'link',
        patientId: selected.patientId,
        ...basePayload,
      });
    }
  }

  onCancelWizard(): void {
    this.store.clearState();
    this.router.navigate(['/staff/queue']);
  }
}
