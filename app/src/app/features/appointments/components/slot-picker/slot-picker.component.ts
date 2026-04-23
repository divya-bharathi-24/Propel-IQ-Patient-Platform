import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnDestroy,
  OnInit,
  Output,
  computed,
  inject,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatNativeDateModule } from '@angular/material/core';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { FormsModule } from '@angular/forms';
import { SlotAvailabilityStore } from '../../state/slot-availability.store';
import { SlotDto } from '../../models/slot.models';

@Component({
  selector: 'app-slot-picker',
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
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './slot-picker.component.html',
  styleUrl: './slot-picker.component.scss',
})
export class SlotPickerComponent implements OnInit, OnDestroy {
  /** The specialty for which slots will be fetched. */
  @Input({ required: true }) specialtyId!: string;

  /** Emitted when the user selects an available slot. */
  @Output() slotSelected = new EventEmitter<SlotDto>();

  /** Emitted when the user clicks "Join Waitlist" on a fully-booked day. */
  @Output() joinWaitlistRequested = new EventEmitter<{
    specialtyId: string;
    date: string;
  }>();

  protected readonly store = inject(SlotAvailabilityStore);

  /** Minimum selectable date: today. Used by mat-datepicker [min] binding. */
  protected readonly today = new Date();

  protected readonly selectedDate = signal<Date | null>(null);

  /** True when every slot for the selected day is unavailable. */
  protected readonly isFullyBooked = computed(
    () =>
      this.store.slots().length > 0 &&
      this.store.slots().every((s) => !s.isAvailable),
  );

  /** Available slots (isAvailable === true). */
  protected readonly availableSlots = computed(() =>
    this.store.slots().filter((s) => s.isAvailable),
  );

  /** Unavailable slots (isAvailable === false) — shown greyed when day is NOT fully booked. */
  protected readonly unavailableSlots = computed(() =>
    this.store.slots().filter((s) => !s.isAvailable),
  );

  ngOnInit(): void {
    // Pre-load today's slots when the component mounts.
    const today = new Date();
    this.selectedDate.set(today);
    this.fetchSlotsForDate(today);
  }

  ngOnDestroy(): void {
    this.store.reset();
  }

  protected onDateChange(date: Date | null): void {
    if (!date) return;
    this.selectedDate.set(date);
    this.fetchSlotsForDate(date);
  }

  protected onSlotSelect(slot: SlotDto): void {
    this.store.selectSlot(slot);
    this.slotSelected.emit(slot);
  }

  protected onJoinWaitlist(): void {
    const date = this.selectedDate();
    if (!date) return;
    this.joinWaitlistRequested.emit({
      specialtyId: this.specialtyId,
      date: this.toDateString(date),
    });
  }

  protected slotLabel(slot: SlotDto): string {
    const start = new Date(slot.timeSlotStart);
    const end = new Date(slot.timeSlotEnd);
    return `${this.formatTime(start)} – ${this.formatTime(end)}`;
  }

  protected isSelected(slot: SlotDto): boolean {
    const sel = this.store.selectedSlot();
    return sel?.timeSlotStart === slot.timeSlotStart;
  }

  private fetchSlotsForDate(date: Date): void {
    this.store.loadSlots({
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

  private formatTime(date: Date): string {
    return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  }
}
