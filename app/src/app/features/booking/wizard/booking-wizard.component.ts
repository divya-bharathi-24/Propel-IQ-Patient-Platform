import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  inject,
} from '@angular/core';
import { BookingWizardStore } from './booking-wizard.store';
import { SlotSelectionStepComponent } from './steps/slot-selection-step.component';
import {
  PreferredSlotStepComponent,
  PreferredSlotDesignation,
} from './steps/preferred-slot-step.component';
import { IntakeModeStepComponent } from './steps/intake-mode-step.component';
import { InsuranceStepComponent } from './steps/insurance-step.component';
import { BookingConfirmationStepComponent } from './steps/booking-confirmation-step.component';
import { InsuranceCheckResult } from '../../../shared/models/insurance.models';

@Component({
  selector: 'app-booking-wizard',
  standalone: true,
  imports: [
    SlotSelectionStepComponent,
    PreferredSlotStepComponent,
    IntakeModeStepComponent,
    InsuranceStepComponent,
    BookingConfirmationStepComponent,
  ],
  providers: [BookingWizardStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="booking-wizard" aria-label="Appointment booking wizard">
      <!-- Step indicators -->
      <nav class="wizard-steps" aria-label="Booking progress">
        @for (indicator of stepIndicators; track indicator.step) {
          <div
            class="wizard-step-indicator"
            [class.active]="store.step() === indicator.step"
            [class.completed]="store.step() > indicator.step"
            [attr.aria-current]="
              store.step() === indicator.step ? 'step' : null
            "
            [attr.aria-label]="
              indicator.label +
              (store.step() > indicator.step
                ? ' (completed)'
                : store.step() === indicator.step
                  ? ' (current)'
                  : '')
            "
          >
            <span class="step-number" aria-hidden="true">{{
              indicator.step
            }}</span>
            <span class="step-label">{{ indicator.label }}</span>
          </div>
        }
      </nav>

      <!-- Global error banner -->
      @if (store.errorMessage()) {
        <div
          class="error-banner"
          role="status"
          aria-live="polite"
          aria-atomic="true"
        >
          {{ store.errorMessage() }}
        </div>
      }

      <!-- Step panels -->
      @if (store.step() === 1) {
        <app-slot-selection-step />
      }
      @if (store.step() === 2) {
        <app-preferred-slot-step
          [specialtyId]="store.selectedSlot()!.specialtyId"
          (slotDesignated)="onSlotDesignated($event)"
        />
      }
      @if (store.step() === 3) {
        <app-intake-mode-step />
      }
      @if (store.step() === 4) {
        <app-insurance-step (insuranceChecked)="onInsuranceChecked($event)" />
      }
      @if (store.step() === 5) {
        <app-booking-confirmation-step />
      }
    </div>
  `,
  styles: [
    `
      .booking-wizard {
        max-width: 720px;
        margin: 2rem auto;
        padding: 0 1rem;
        display: flex;
        flex-direction: column;
        gap: 1.5rem;
      }

      .wizard-steps {
        display: flex;
        gap: 0.5rem;
        align-items: center;
      }

      .wizard-step-indicator {
        display: flex;
        align-items: center;
        gap: 0.5rem;
        padding: 0.5rem 0.75rem;
        border-radius: 4px;
        color: #767676;
        font-size: 0.875rem;
        flex: 1;
        border-bottom: 3px solid #e0e0e0;
        transition:
          border-color 0.2s ease,
          color 0.2s ease;

        &.active {
          color: #1565c0;
          border-color: #1565c0;
          font-weight: 600;
        }

        &.completed {
          color: #2e7d32;
          border-color: #2e7d32;
        }
      }

      .step-number {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 24px;
        height: 24px;
        border-radius: 50%;
        background: currentColor;
        color: #fff;
        font-size: 0.75rem;
        font-weight: 700;
        flex-shrink: 0;
      }

      .step-label {
        white-space: nowrap;
      }

      .error-banner {
        padding: 0.75rem 1rem;
        background-color: #fff3f3;
        border-left: 4px solid #b00020;
        color: #b00020;
        font-weight: 500;
        border-radius: 4px;
        font-size: 0.9rem;
      }

      /* Responsive: collapse step labels on narrow viewports */
      @media (max-width: 480px) {
        .step-label {
          display: none;
        }
      }
    `,
  ],
})
export class BookingWizardComponent implements OnDestroy {
  protected readonly store = inject(BookingWizardStore);

  protected readonly stepIndicators: {
    step: 1 | 2 | 3 | 4 | 5;
    label: string;
  }[] = [
    { step: 1, label: 'Select Slot' },
    { step: 2, label: 'Preferred Slot' },
    { step: 3, label: 'Intake Mode' },
    { step: 4, label: 'Insurance' },
    { step: 5, label: 'Confirmation' },
  ];

  /**
   * Handles the `slotDesignated` output from the preferred slot step.
   * Records the designation (or null for skip) and advances to Step 3.
   */
  protected onSlotDesignated(
    designation: PreferredSlotDesignation | null,
  ): void {
    this.store.setPreferredSlot(designation);
  }

  /**
   * Handles the `insuranceChecked` output from Step 4.
   * Records the pre-check result on the store then advances to Step 5.
   * Navigation occurs regardless of insurance status (FR-040 — non-blocking).
   */
  protected onInsuranceChecked(result: InsuranceCheckResult | null): void {
    this.store.setInsuranceResult(result);
    this.store.advanceToConfirmation();
  }

  ngOnDestroy(): void {
    this.store.resetWizard();
  }
}
