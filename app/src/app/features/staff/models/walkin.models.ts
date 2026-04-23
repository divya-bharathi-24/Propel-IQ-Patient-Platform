/** Result returned by GET /api/staff/patients/search */
export interface PatientSearchResultDto {
  patientId: string;
  name: string;
  dateOfBirth: string | null;
  email: string;
  contactNumber: string | null;
}

/** Mode of walk-in booking */
export type WalkInMode = 'link' | 'create' | 'anonymous';

/** Payload for POST /api/staff/walkin */
export interface WalkInBookingDto {
  mode: WalkInMode;
  /** Required when mode = 'link' */
  patientId?: string;
  /** Required when mode = 'create' */
  name?: string;
  contactNumber?: string;
  email?: string;
}

/** Response from POST /api/staff/walkin */
export interface WalkInResponseDto {
  appointmentId: string;
  visitId: string;
  patientId: string | null;
  queuedOnly: boolean;
}

/** Error payload returned when a duplicate email is detected (HTTP 409) */
export interface DuplicatePatientError {
  existingPatientId: string;
  existingPatientName: string;
  message: string;
}
