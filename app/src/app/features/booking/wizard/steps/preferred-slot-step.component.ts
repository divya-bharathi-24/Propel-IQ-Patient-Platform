import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnDestroy,
  Output,
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
import { MatRadioModule } from '@angular/material/radio';
import { SlotAvailabilityStore } from '../../../appointments/state/slot-availability.store';
import { SlotDto } from '../../../appointments/models/slot.models';

export interface PreferredSlotDesignation {
  preferredDate: string; // "YYYY-MM-DD"
  preferredTimeSlot: string; // ISO 8601 start time
}

@Component({
  selector: 'app-preferred-slot-step',
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
    MatRadioModule,
  ],
  providers: [SlotAvailabilityStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section
      class="preferred-slot-step"
      aria-label="Step 2b: Designate a preferred slot (optional)"
    >
      <h2 class="step-heading">Prefer a Specific Time?</h2>
      <p class="step-description">
        If a time slot is unavailable today, you can join the waitlist for your
        preferred date and time. Select a date to see unavailable slots.
      </p>

      <mat-form-field appearance="outline" class="date-field">
        <mat-label>Select a date</mat-label>
        <input
          matInput
          [matDatepicker]="datepicker"
          [ngModel]="selectedDate()"
          (ngModelChange)="onDateChange($event)"
          [min]="today"
          aria-label="Preferred appointment date"
          placeholder="DD/MM/YYYY"
        />
        <mat-datepicker-toggle matIconSuffix [for]="datepicker" />
        <mat-datepicker #datepicker />
      </mat-form-field>

      @if (slotsStore.loadingState() === 'loading') {
        <div
          class="loading-container"
          aria-busy="true"
          aria-label="Loading slots"
        >
          <mat-spinner diameter="40" />
        </div>
      }

      @if (slotsStore.loadingState() === 'error') {
        <p class="error-message" role="alert">
          Unable to load slots. Please try again.
        </p>
      }

      @if (slotsStore.loadingState() === 'loaded' && allSlotsAvailable()) {
        <!-- Edge-case: all slots are available — direct the patient to book directly -->
        <div
          class="info-banner"
          role="status"
          aria-live="polite"
          aria-atomic="true"
        >
          <p>
            All slots on this date are available — you can book one directly.
          </p>
          <button
            mat-flat-button
            color="primary"
            (click)="onBookThisDate()"
            aria-label="Book a slot on this date directly"
          >
            Book This Date
          </button>
        </div>
      }

      @if (
        slotsStore.loadingState() === 'loaded' &&
        !allSlotsAvailable() &&
        unavailableSlots().length > 0
      ) {
        <div
          role="radiogroup"
          aria-label="Unavailable slots available for waitlist designation"
          class="slot-radio-group"
        >
          <p class="slot-radio-hint">
            Select an unavailable slot to join the waitlist:
          </p>
          @for (slot of unavailableSlots(); track slot.timeSlotStart) {
            <div
              class="slot-radio-card"
              [class.slot-radio-card--selected]="
                selectedSlot()?.timeSlotStart === slot.timeSlotStart
              "
            >
              <mat-radio-button
                [value]="slot.timeSlotStart"
                [checked]="selectedSlot()?.timeSlotStart === slot.timeSlotStart"
                (change)="onSlotSelect(slot)"
                [attr.aria-label]="'Waitlist for ' + slotLabel(slot)"
              >
                {{ slotLabel(slot) }}
              </mat-radio-button>
            </div>
          }
        </div>
      }

      @if (
        slotsStore.loadingState() === 'loaded' &&
        slotsStore.slots().length === 0
      ) {
        <p class="no-slots-label" aria-live="polite">
          No slots found for the selected date.
        </p>
      }

      <div class="actions" aria-live="polite">
        @if (!allSlotsAvailable()) {
          <button
            mat-flat-button
            color="primary"
            [disabled]="!selectedSlot()"
            (click)="onDesignate()"
            aria-label="Designate selected slot as preferred and join waitlist"
          >
            Designate Preferred Slot
          </button>
        }

        <button
          mat-stroked-button
          (click)="onSkip()"
          aria-label="Skip preferred slot designation"
        >
          Skip — I don't have a preference
        </button>
      </div>
    </section>
  `,
  styles: [
    `
      .preferred-slot-step {
        display: flex;
        flex-direction: column;
        gap: 1.5rem;
      }

      .step-heading {
        margin: 0;
        font-size: 1.25rem;
        font-weight: 600;
        color: #1a1a1a;
      }

      .step-description {
        margin: 0;
        color: #555;
        font-size: 0.9rem;
        line-height: 1.5;
      }

      .date-field {
        width: 100%;
        max-width: 320px;
      }

      .loading-container {
        display: flex;
        justify-content: center;
        padding: 1rem 0;
      }

      .error-message {
        color: #b00020;
        font-size: 0.875rem;
        margin: 0;
      }

      .info-banner {
        padding: 1rem;
        background-color: #e3f2fd;
        border-left: 4px solid #1565c0;
        border-radius: 4px;
        display: flex;
        flex-direction: column;
        gap: 0.75rem;

        p {
          margin: 0;
          color: #1565c0;
          font-weight: 500;
        }
      }

      .slot-radio-group {
        display: flex;
        flex-direction: column;
        gap: 0.5rem;
      }

      .slot-radio-hint {
        margin: 0 0 0.25rem;
        font-size: 0.875rem;
        color: #555;
      }

      .slot-radio-card {
        display: flex;
        align-items: center;
        padding: 0.625rem 0.75rem;
        border: 1px solid #e0e0e0;
        border-radius: 6px;
        cursor: pointer;
        transition: border-color 0.15s ease;

        &:hover {
          border-color: #1565c0;
        }

        &.slot-radio-card--selected {
          border-color: #1565c0;
          background-color: #e8f0fe;
        }
      }

      .no-slots-label {
        margin: 0;
        color: #555;
        font-size: 0.875rem;
      }

      .actions {
        display: flex;
        gap: 0.75rem;
        flex-wrap: wrap;
      }
    `,
  ],
})
export class PreferredSlotStepComponent implements OnDestroy {
  /** Specialty for which slots will be fetched. */
  @Input({ required: true }) specialtyId!: string;

  /**
   * Emits the designated slot, or null when the patient skips.
   */
  @Output() slotDesignated =
    new EventEmitter<PreferredSlotDesignation | null>();

  /**
   * Emits the selected date string ("YYYY-MM-DD") when the patient clicks
   * "Book This Date" (edge case: all slots available). The wizard handles
   * navigation back to Step 1 with the date pre-selected.
   */
  @Output() bookThisDate = new EventEmitter<string>();

  protected readonly slotsStore = inject(SlotAvailabilityStore);

  protected readonly today = new Date();
  protected readonly selectedDate = signal<Date | null>(null);
  protected readonly selectedSlot = signal<SlotDto | null>(null);

  /** Unavailable slots — valid targets for preferred-slot designation. */
  protected readonly unavailableSlots = computed(() =>
    this.slotsStore.slots().filter((s) => !s.isAvailable),
  );

  /**
   * True when every slot on the selected date is available (edge case):
   * no waitlist designation makes sense; prompt the patient to book directly.
   */
  protected readonly allSlotsAvailable = computed(
    () =>
      this.slotsStore.slots().length > 0 &&
      this.slotsStore.slots().every((s) => s.isAvailable),
  );

  ngOnDestroy(): void {
    this.slotsStore.reset();
  }

  protected onDateChange(date: Date | null): void {
    if (!date) return;
    this.selectedDate.set(date);
    this.selectedSlot.set(null);
    this.slotsStore.loadSlots({
      specialtyId: this.specialtyId,
      date: this.toDateString(date),
    });
  }

  protected onSlotSelect(slot: SlotDto): void {
    this.selectedSlot.set(slot);
  }

  protected onDesignate(): void {
    const slot = this.selectedSlot();
    const date = this.selectedDate();
    if (!slot || !date) return;

    this.slotDesignated.emit({
      preferredDate: this.toDateString(date),
      preferredTimeSlot: slot.timeSlotStart,
    });
  }

  protected onSkip(): void {
    this.slotDesignated.emit(null);
  }

  protected onBookThisDate(): void {
    // Edge case: all slots available — emit the selected date so the wizard
    // can navigate back to Step 1 with the date pre-selected.
    this.bookThisDate.emit(this.toDateString(this.selectedDate()!));
  }

  protected slotLabel(slot: SlotDto): string {
    const start = new Date(slot.timeSlotStart);
    const end = new Date(slot.timeSlotEnd);
    return `${this.formatTime(start)} – ${this.formatTime(end)}`;
  }

  private toDateString(date: Date): string {
    const y = date.getFullYear();
    const m = String(date.getMonth() + 1).padStart(2, '0');
    const d = String(date.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
  }

  private formatTime(date: Date): string {
    return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  }
}
