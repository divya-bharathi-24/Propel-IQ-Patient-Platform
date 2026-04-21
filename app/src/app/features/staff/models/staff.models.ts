export interface CreateWalkInPatientRequest {
  name: string;
  phone?: string;
  email: string;
}

export interface CreateWalkInPatientResponse {
  patientId: string;
  message: string;
}

export interface PatientSearchResult {
  patientId: string;
  name: string;
  email: string;
}
