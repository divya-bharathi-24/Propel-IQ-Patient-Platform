import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatNativeDateModule } from '@angular/material/core';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute } from '@angular/router';
import { SlotAvailabilityStore } from '../../../appointments/state/slot-availability.store';
import { SlotDto } from '../../../appointments/models/slot.models';
import { AvailableSlot } from '../../booking.models';
import { BookingService } from '../../booking.service';
import { BookingWizardStore } from '../booking-wizard.store';

@Component({
  selector: 'app-slot-selection-step',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatInputModule,
    MatNativeDateModule,
    MatProgressSpinnerModule,
  ],
  providers: [SlotAvailabilityStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section
      class="slot-selection-step"
      aria-label="Step 1: Select an appointment slot"
    >
      <h2 class="step-heading">Select a Slot</h2>

      <mat-form-field appearance="outline" class="date-field">
        <mat-label>Select a date</mat-label>
        <input
          matInput
          [matDatepicker]="datepicker"
          [ngModel]="selectedDate()"
          (ngModelChange)="onDateChange($event)"
          [min]="today"
          aria-label="Appointment date"
          placeholder="DD/MM/YYYY"
        />
        <mat-datepicker-toggle matIconSuffix [for]="datepicker" />
        <mat-datepicker #datepicker />
      </mat-form-field>

      @if (slotsStore.loadingState() === 'loading') {
        <div
          class="loading-container"
          aria-busy="true"
          aria-label="Loading available slots"
        >
          <mat-spinner diameter="40" />
        </div>
      }

      @if (slotsStore.loadingState() === 'error') {
        <p class="error-message" role="alert">
          Unable to load slots. Please try again.
        </p>
      }

      @if (
        slotsStore.loadingState() === 'loaded' &&
        slotsStore.slots().length === 0
      ) {
        <p class="no-slots-label" aria-live="polite">
          No slots found for the selected date.
        </p>
      }

      @if (
        slotsStore.loadingState() === 'loaded' && slotsStore.slots().length > 0
      ) {
        @if (isFullyBooked()) {
          <p class="no-slots-label" aria-live="polite">
            All slots are fully booked for this date. Please try another date.
          </p>
        }

        @if (!isFullyBooked()) {
          <div
            class="slot-grid"
            role="grid"
            aria-label="Available appointment slots"
          >
            @for (slot of availableSlots(); track slot.timeSlotStart) {
              <button
                mat-stroked-button
                class="slot-btn slot-btn--available"
                [class.slot-btn--loading]="
                  holdingSlotId() === slot.timeSlotStart
                "
                (click)="onSlotSelect(slot)"
                [disabled]="isHolding()"
                [attr.aria-label]="'Book slot ' + slotLabel(slot)"
                role="gridcell"
              >
                @if (holdingSlotId() === slot.timeSlotStart) {
                  <mat-spinner diameter="16" class="inline-spinner" />
                } @else {
                  {{ slotLabel(slot) }}
                }
              </button>
            }

            @for (slot of unavailableSlots(); track slot.timeSlotStart) {
              <button
                mat-stroked-button
                class="slot-btn slot-btn--unavailable"
                [disabled]="true"
                aria-disabled="true"
                [attr.aria-label]="'Unavailable slot ' + slotLabel(slot)"
                role="gridcell"
              >
                {{ slotLabel(slot) }}
              </button>
            }
          </div>
        }
      }
    </section>
  `,
  styles: [
    `
      .slot-selection-step {
        display: flex;
        flex-direction: column;
        gap: 1rem;
      }

      .step-heading {
        font-size: 1.25rem;
        font-weight: 600;
        margin: 0;
      }

      .date-field {
        width: 100%;
        max-width: 280px;
      }

      .slot-grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
        gap: 0.5rem;
      }

      .slot-btn {
        min-width: 0;
        width: 100%;
        font-size: 0.875rem;
        transition:
          background-color 0.15s ease,
          border-color 0.15s ease;

        &--unavailable {
          color: #767676;
          border-color: #b0b0b0;
          text-decoration: line-through;
          opacity: 1;
        }

        &--loading {
          opacity: 0.7;
        }
      }

      .inline-spinner {
        display: inline-block;
        vertical-align: middle;
      }

      .loading-container {
        display: flex;
        justify-content: center;
        padding: 2rem 0;
      }

      .no-slots-label,
      .error-message {
        margin: 0;
        color: #767676;
        font-size: 0.875rem;
      }

      .error-message {
        color: #b00020;
      }
    `,
  ],
})
export class SlotSelectionStepComponent implements OnInit, OnDestroy {
  protected readonly slotsStore = inject(SlotAvailabilityStore);
  protected readonly wizardStore = inject(BookingWizardStore);
  private readonly bookingService = inject(BookingService);
  private readonly route = inject(ActivatedRoute);

  protected readonly today = new Date();
  protected readonly selectedDate = signal<Date | null>(null);
  /** ID of the slot currently being held (shows inline spinner). */
  protected readonly holdingSlotId = signal<string | null>(null);

  protected readonly isHolding = computed(() => this.holdingSlotId() !== null);

  protected readonly isFullyBooked = computed(
    () =>
      this.slotsStore.slots().length > 0 &&
      this.slotsStore.slots().every((s) => !s.isAvailable),
  );

  protected readonly availableSlots = computed(() =>
    this.slotsStore.slots().filter((s) => s.isAvailable),
  );

  protected readonly unavailableSlots = computed(() =>
    this.slotsStore.slots().filter((s) => !s.isAvailable),
  );

  /** SpecialtyId from query param — falls back to empty string. */
  private get specialtyId(): string {
    return this.route.snapshot.queryParamMap.get('specialtyId') ?? '';
  }

  ngOnInit(): void {
    const today = new Date();
    this.selectedDate.set(today);
    this.fetchSlots(today);
  }

  ngOnDestroy(): void {
    this.slotsStore.reset();
  }

  protected onDateChange(date: Date | null): void {
    if (!date) return;
    this.selectedDate.set(date);
    this.fetchSlots(date);
  }

  protected onSlotSelect(slot: SlotDto): void {
    this.holdingSlotId.set(slot.timeSlotStart);

    const availableSlot: AvailableSlot = {
      slotId: slot.timeSlotStart, // Use timeSlotStart as slotId until backend provides UUID
      specialtyId: slot.specialtyId,
      specialtyName: '', // Will be filled by backend response once TASK_002 is available
      date: slot.date,
      timeSlotStart: this.toTimeString(new Date(slot.timeSlotStart)),
      timeSlotEnd: this.toTimeString(new Date(slot.timeSlotEnd)),
    };

    this.bookingService.holdSlot(availableSlot).subscribe({
      next: () => {
        this.holdingSlotId.set(null);
        this.wizardStore.selectSlot(availableSlot);
      },
      error: () => {
        // Hold failed — surface conflict via wizard store error message and stay on Step 1
        this.holdingSlotId.set(null);
        // Refresh slots so the UI reflects current availability
        const date = this.selectedDate();
        if (date) this.fetchSlots(date);
      },
    });
  }

  protected slotLabel(slot: SlotDto): string {
    const start = new Date(slot.timeSlotStart);
    const end = new Date(slot.timeSlotEnd);
    return `${this.formatTime(start)} – ${this.formatTime(end)}`;
  }

  private fetchSlots(date: Date): void {
    this.slotsStore.loadSlots({
      specialtyId: this.specialtyId,
      date: this.toDateString(date),
    });
  }

  private toDateString(date: Date): string {
    const y = date.getFullYear();
    const m = String(date.getMonth() + 1).padStart(2, '0');
    const d = String(date.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
  }

  private toTimeString(date: Date): string {
    const h = String(date.getUTCHours()).padStart(2, '0');
    const m = String(date.getUTCMinutes()).padStart(2, '0');
    return `${h}:${m}`;
  }

  private formatTime(date: Date): string {
    return date.toLocaleTimeString('en-AU', {
      hour: '2-digit',
      minute: '2-digit',
      hour12: true,
    });
  }
}
