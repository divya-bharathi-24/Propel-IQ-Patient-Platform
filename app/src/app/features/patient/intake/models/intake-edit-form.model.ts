export type IntakeLoadState = 'idle' | 'loading' | 'success' | 'error';
export type IntakeSaveState = 'idle' | 'saving' | 'success' | 'error';

// ── Intake record data shapes ────────────────────────────────────────────────

export interface IntakeDemographics {
  firstName: string;
  lastName: string;
  dateOfBirth: string;
  biologicalSex: string;
  phone: string;
  street: string;
  city: string;
  state: string;
  postalCode: string;
  country: string;
}

export interface IntakeMedicalHistoryItem {
  condition: string;
  diagnosedAt?: string;
  notes?: string;
}

export interface IntakeSymptomItem {
  name: string;
  severity?: string;
  onsetDate?: string;
}

export interface IntakeMedicationItem {
  name: string;
  dosage?: string;
  frequency?: string;
}

/** Canonical form value sent to / received from the API. */
export interface IntakeFormValue {
  demographics: IntakeDemographics;
  medicalHistory: IntakeMedicalHistoryItem[];
  symptoms: IntakeSymptomItem[];
  medications: IntakeMedicationItem[];
}

// ── API response shapes ──────────────────────────────────────────────────────

/** Shape of GET /api/intake/{appointmentId} */
export interface IntakeRecordDto {
  appointmentId: string;
  status: 'Draft' | 'Submitted' | 'Partial';
  completedAt: string | null;
  rowVersion: string;
  data: IntakeFormValue;
}

/** Shape of GET /api/intake/{appointmentId}/draft */
export interface IntakeDraftResponse {
  exists: boolean;
  draftData?: IntakeFormValue;
  savedAt?: string;
}

/** Partial-save validation error (422 Unprocessable Entity) */
export interface IntakeMissingFieldsError {
  missingFields: string[];
}

/** Concurrent-conflict payload (409 Conflict) */
export interface IntakeConflictPayload {
  serverVersion: IntakeFormValue;
  serverRowVersion: string;
  localVersion: IntakeFormValue;
}

// ── Manual intake form types (US_029) ────────────────────────────────────────

export type AutosaveStatus = 'idle' | 'saving' | 'saved' | 'error';

/** Demographics extended with emergency contact fields for the manual intake form. */
export interface ManualDemographics {
  firstName: string;
  lastName: string;
  dateOfBirth: string;
  gender: string;
  phone: string;
  street: string;
  city: string;
  postalCode: string;
  emergencyContactName: string;
  emergencyContactPhone: string;
}

export interface ManualAllergyItem {
  substance: string;
  reaction?: string;
}

export interface ManualSurgeryItem {
  procedure: string;
  year?: string;
  notes?: string;
}

export interface ManualMedicalHistory {
  conditions: IntakeMedicalHistoryItem[];
  allergies: ManualAllergyItem[];
  surgeries: ManualSurgeryItem[];
  familyHistory: string;
}

/** Symptom with severity selector and duration (for the manual intake form). */
export interface ManualSymptomItem {
  name: string;
  severity: 'Mild' | 'Moderate' | 'Severe' | '';
  onsetDate?: string;
  duration?: string;
}

/** Medication with OTC/supplement flag (for the manual intake form). */
export interface ManualMedicationItem {
  name: string;
  dosage?: string;
  frequency?: string;
  isOtcSupplement: boolean;
}

/** Full manual intake form value sent to / received from the API. */
export interface ManualIntakeFormValue {
  demographics: ManualDemographics;
  medicalHistory: ManualMedicalHistory;
  symptoms: ManualSymptomItem[];
  medications: ManualMedicationItem[];
}

/**
 * Canonical field map shared by the intake orchestration layer (IntakeModeStore).
 * Mirrors the four JSONB column shapes of IntakeRecord so that patchValues()
 * and server draft restoration require no runtime transformation.
 * Aliased to ManualIntakeFormValue — the most complete representation of
 * all intake sections including allergies, surgeries, and emergency contact.
 */
export type IntakeFieldMap = ManualIntakeFormValue;

/** Response returned by POST /api/intake/session/resume on Manual → AI switch. */
export interface ResumeAiSessionResponse {
  nextQuestion: string;
}

/** Payload for POST /api/intake/sync-local-draft (localStorage → server sync). */
export interface SyncLocalDraftRequest {
  appointmentId: string;
  fields: IntakeFieldMap;
  capturedAt: number;
}

/** Draft wrapper returned by getForm when a manual draft exists. */
export interface ManualIntakeFormDraft {
  data: ManualIntakeFormValue;
  savedAt: string;
  completedAt: string | null;
}

/** Shape of GET /api/intake/form?appointmentId={id} (US_029). */
export interface IntakeFormResponseDto {
  appointmentId: string;
  manualDraft: ManualIntakeFormDraft | null;
  aiExtracted: ManualIntakeFormValue | null;
}
