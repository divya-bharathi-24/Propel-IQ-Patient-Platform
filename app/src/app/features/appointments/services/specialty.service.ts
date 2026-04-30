import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { SpecialtyDto } from '../models/slot.models';

@Injectable({ providedIn: 'root' })
export class SpecialtyService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = '/api/appointments';

  /** Fetches all available specialties from GET /api/appointments/specialties. */
  getSpecialties(): Observable<SpecialtyDto[]> {
    return this.http.get<SpecialtyDto[]>(`${this.apiBase}/specialties`);
  }
}
