import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CancelResponseDto,
  RescheduleRequestDto,
} from '../models/appointment-management.models';

@Injectable({ providedIn: 'root' })
export class AppointmentManagementService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = '/api/appointments';

  /**
   * Cancels an appointment by ID.
   * POST /api/appointments/{id}/cancel
   * Returns 200 on success, 400 if the appointment is in the past.
   */
  cancelAppointment(appointmentId: string): Observable<CancelResponseDto> {
    return this.http.post<CancelResponseDto>(
      `${this.apiBase}/${appointmentId}/cancel`,
      {},
    );
  }

  /**
   * Reschedules an appointment to a new slot.
   * POST /api/appointments/{id}/reschedule
   * Returns 200 on success, 409 if the requested slot is no longer available.
   */
  rescheduleAppointment(
    appointmentId: string,
    dto: RescheduleRequestDto,
  ): Observable<void> {
    return this.http.post<void>(
      `${this.apiBase}/${appointmentId}/reschedule`,
      dto,
    );
  }
}
