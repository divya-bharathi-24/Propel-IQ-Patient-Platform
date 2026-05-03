import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import {
  AvailableSlot,
  BookingResult,
  CreateBookingRequest,
} from './booking.models';

@Injectable({ providedIn: 'root' })
export class BookingService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = '/api/appointments';

  /**
   * Places a 5-minute Redis hold on the given slot.
   * Must be called when the patient advances from Step 1 (slot selection).
   */
  holdSlot(slot: AvailableSlot): Observable<void> {
    return this.http
      .post<void>(`${this.apiBase}/hold-slot`, {
        specialtyId: slot.specialtyId,
        date: slot.date,
        timeSlotStart: slot.timeSlotStart,
      })
      .pipe(catchError(this.handleError));
  }

  /**
   * Submits the full booking request.
   * Returns BookingResult on success (HTTP 200/201).
   * Throws HttpErrorResponse with status 409 when the slot is no longer available.
   */
  confirmBooking(request: CreateBookingRequest): Observable<BookingResult> {
    return this.http
      .post<BookingResult>(`${this.apiBase}/book`, request)
      .pipe(catchError(this.handleError));
  }

  private handleError(err: unknown): Observable<never> {
    if (err instanceof HttpErrorResponse) {
      return throwError(() => err);
    }
    return throwError(() => new Error('An unexpected error occurred.'));
  }
}
