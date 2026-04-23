import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { SlotAvailabilityResponseDto } from '../models/slot.models';

@Injectable({ providedIn: 'root' })
export class SlotAvailabilityService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = '/api/appointments';

  /**
   * Fetches available slots from the backend for a given specialty and date.
   * No client-side caching — cache freshness is guaranteed by the backend (≤5 s Redis TTL).
   *
   * @param specialtyId UUID of the specialty
   * @param date        Date string in "YYYY-MM-DD" format
   */
  getAvailableSlots(
    specialtyId: string,
    date: string,
  ): Observable<SlotAvailabilityResponseDto> {
    const params = new HttpParams()
      .set('specialtyId', specialtyId)
      .set('date', date);

    return this.http.get<SlotAvailabilityResponseDto>(`${this.apiBase}/slots`, {
      params,
    });
  }
}
