import { HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import {
  AvailableSlot,
  BookingResult,
  CreateBookingRequest,
  InsuranceInfo,
  IntakeMode,
} from '../booking.models';
import { BookingService } from '../booking.service';
import { InsuranceCheckResult } from '../../../shared/models/insurance.models';
import { PreferredSlotDesignation } from './steps/preferred-slot-step.component';

export interface BookingWizardState {
  /** 1=Slot Selection, 2=Preferred Slot (optional), 3=Intake Mode, 4=Insurance, 5=Confirmation */
  step: 1 | 2 | 3 | 4 | 5;
  selectedSlot: AvailableSlot | null;
  intakeMode: IntakeMode | null;
  insuranceInfo: InsuranceInfo;
  /** Holds the insurance pre-check result from Step 4 (FR-038, FR-039). */
  insuranceResult: InsuranceCheckResult | null;
  /** Preferred waitlist date chosen in Step 2; null when patient skipped. */
  preferredDate: string | null;
  /** Preferred waitlist slot start time (ISO 8601); null when patient skipped. */
  preferredTimeSlot: string | null;
  isSubmitting: boolean;
  bookingResult: BookingResult | null;
  errorMessage: string | null;
  /** Set when the patient clicks "Book This Date" in Step 2 to return to Step 1 with a pre-selected date. */
  preselectedDate: string | null;
  /** Set together with preselectedDate so Step 1 can restore the specialty selection. */
  preselectedSpecialtyId: string | null;
}

const initialState: BookingWizardState = {
  step: 1,
  selectedSlot: null,
  intakeMode: null,
  insuranceInfo: { insurerName: null, memberId: null },
  insuranceResult: null,
  preferredDate: null,
  preferredTimeSlot: null,
  isSubmitting: false,
  bookingResult: null,
  errorMessage: null,
  preselectedDate: null,
  preselectedSpecialtyId: null,
};

export const BookingWizardStore = signalStore(
  withState<BookingWizardState>(initialState),
  withMethods((store, bookingService = inject(BookingService)) => ({
    /** Advances from Step 1 after slot hold is placed. Moves to Step 2 (preferred slot). */
    selectSlot(slot: AvailableSlot): void {
      patchState(store, {
        selectedSlot: slot,
        step: 2,
        errorMessage: null,
        preselectedDate: null,
        preselectedSpecialtyId: null,
      });
    },

    /**
     * Returns to Step 1 (slot selection) when the patient clicks "Book This Date"
     * in Step 2. Stores the date and specialty so Step 1 can restore the selection.
     */
    goBackToSlotSelection(date: string): void {
      const specialtyId = store.selectedSlot()?.specialtyId ?? null;
      patchState(store, {
        step: 1,
        selectedSlot: null,
        preferredDate: null,
        preferredTimeSlot: null,
        errorMessage: null,
        preselectedDate: date,
        preselectedSpecialtyId: specialtyId,
      });
    },

    /**
     * Records preferred slot designation from Step 2 and advances to Step 3 (intake mode).
     * Pass null when the patient skips the preferred slot step.
     */
    setPreferredSlot(designation: PreferredSlotDesignation | null): void {
      patchState(store, {
        preferredDate: designation?.preferredDate ?? null,
        preferredTimeSlot: designation?.preferredTimeSlot ?? null,
        step: 3,
      });
    },

    /** Advances from Step 3 with the chosen intake mode. */
    setIntakeMode(mode: IntakeMode): void {
      patchState(store, { intakeMode: mode, step: 4 });
    },

    /** Updates insurance info without advancing step. */
    setInsuranceInfo(info: InsuranceInfo): void {
      patchState(store, { insuranceInfo: info });
    },

    /**
     * Records the insurance pre-check result from Step 3 (FR-038, FR-039).
     * Pass null when the step is bypassed without any interaction.
     */
    setInsuranceResult(result: InsuranceCheckResult | null): void {
      patchState(store, { insuranceResult: result });
    },

    /** Advances from Step 4 to Step 5 (before API call). */
    advanceToConfirmation(): void {
      patchState(store, { step: 5 });
    },

    /**
     * Submits the booking to the API.
     * On 409: resets to Step 1 with conflict message.
     * On success: stores result and stays at Step 4 (already set).
     */
    async confirmBooking(): Promise<void> {
      const slot = store.selectedSlot();
      const mode = store.intakeMode();
      const insurance = store.insuranceInfo();

      if (!slot || !mode) return;

      patchState(store, { isSubmitting: true, errorMessage: null });

      const request: CreateBookingRequest = {
        slotSpecialtyId: slot.specialtyId,
        slotDate: slot.date,
        slotTimeStart: slot.timeSlotStart,
        slotTimeEnd: slot.timeSlotEnd,
        intakeMode: mode,
        insuranceName: insurance.insurerName,
        insuranceId: insurance.memberId,
        preferredDate: store.preferredDate(),
        preferredTimeSlot: store.preferredTimeSlot(),
      };

      try {
        const result = await bookingService.confirmBooking(request).toPromise();
        patchState(store, {
          bookingResult: result ?? null,
          isSubmitting: false,
          step: 5,
        });
      } catch (err: unknown) {
        if (err instanceof HttpErrorResponse && err.status === 409) {
          patchState(store, {
            isSubmitting: false,
            errorMessage: 'Slot no longer available. Please select another.',
            step: 1,
            selectedSlot: null,
            intakeMode: null,
            preferredDate: null,
            preferredTimeSlot: null,
            bookingResult: null,
          });
        } else {
          patchState(store, {
            isSubmitting: false,
            errorMessage: 'An unexpected error occurred. Please try again.',
          });
        }
      }
    },

    /** Resets the entire wizard to its initial state. */
    resetWizard(): void {
      patchState(store, initialState);
    },
  })),
);
