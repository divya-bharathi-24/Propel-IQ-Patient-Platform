import { Injectable } from '@angular/core';
import { UpdatePatientProfileDto } from '../models/patient-profile.models';

const DRAFT_KEY = 'patient-profile-draft';

@Injectable({ providedIn: 'root' })
export class PatientProfileDraftService {
  /**
   * Persists the current form value to sessionStorage.
   * No PHI field names are logged.
   */
  saveDraft(formValue: UpdatePatientProfileDto): void {
    try {
      sessionStorage.setItem(DRAFT_KEY, JSON.stringify(formValue));
    } catch {
      // sessionStorage may be unavailable (private browsing quota exceeded)
    }
  }

  /**
   * Retrieves the persisted draft, or `null` if absent or unparseable.
   */
  loadDraft(): UpdatePatientProfileDto | null {
    try {
      const raw = sessionStorage.getItem(DRAFT_KEY);
      if (!raw) return null;
      return JSON.parse(raw) as UpdatePatientProfileDto;
    } catch {
      return null;
    }
  }

  /** Removes the persisted draft from sessionStorage. */
  clearDraft(): void {
    sessionStorage.removeItem(DRAFT_KEY);
  }
}
