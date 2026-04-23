import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { WaitlistEntryDto } from '../models/waitlist.models';

@Injectable({ providedIn: 'root' })
export class WaitlistService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = '/api/waitlist';

  /**
   * Fetches all waitlist entries for the authenticated patient.
   */
  getMyWaitlistEntries(): Observable<WaitlistEntryDto[]> {
    return this.http.get<WaitlistEntryDto[]>(`${this.apiBase}/me`);
  }

  /**
   * Cancels (expires) the preferred slot designation for the given waitlist entry.
   *
   * @param waitlistId UUID of the WaitlistEntry to cancel
   */
  cancelPreference(waitlistId: string): Observable<void> {
    return this.http.patch<void>(`${this.apiBase}/${waitlistId}/cancel`, {});
  }
}
