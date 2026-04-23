import { HttpClient, HttpHeaders, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import {
  PatientProfileDto,
  UpdatePatientProfileDto,
} from '../models/patient-profile.models';

@Injectable({ providedIn: 'root' })
export class PatientService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = '/api/patients';

  /**
   * Fetches the authenticated patient's own profile.
   * Returns the full response so the caller can extract the ETag header.
   */
  getProfile(): Observable<{ profile: PatientProfileDto; eTag: string }> {
    return this.http
      .get<PatientProfileDto>(`${this.apiBase}/me`, { observe: 'response' })
      .pipe(
        map((response: HttpResponse<PatientProfileDto>) => ({
          profile: response.body as PatientProfileDto,
          eTag: response.headers.get('ETag') ?? '',
        })),
      );
  }

  /**
   * Partially updates the patient's own profile.
   * Sends the `If-Match` header with the stored ETag to detect concurrent edits.
   *
   * @returns Observable<PatientProfileDto> — updated profile on 200.
   *   On 409 the HTTP interceptor propagates an HttpErrorResponse; callers
   *   must catch and handle status 409 themselves.
   */
  updateProfile(
    dto: UpdatePatientProfileDto,
    eTag: string,
  ): Observable<PatientProfileDto> {
    const headers = new HttpHeaders({ 'If-Match': eTag });
    return this.http.patch<PatientProfileDto>(`${this.apiBase}/me`, dto, {
      headers,
    });
  }
}
