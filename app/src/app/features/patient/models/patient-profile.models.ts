export interface AddressDto {
  street: string;
  city: string;
  state: string;
  postalCode: string;
  country: string;
}

export interface EmergencyContactDto {
  name: string;
  phone: string;
  relationship: string;
}

export interface CommunicationPreferencesDto {
  emailOptIn: boolean;
  smsOptIn: boolean;
  preferredLanguage: string;
}

export interface InsuranceDto {
  insurerName: string;
  memberId: string;
  groupNumber: string;
}

/** Read model returned by GET /api/patients/me */
export interface PatientProfileDto {
  id: string;
  name: string;
  dateOfBirth: string;
  biologicalSex: string;
  email: string;
  phone: string;
  address: AddressDto;
  insurance: InsuranceDto;
  emergencyContact: EmergencyContactDto;
  communicationPreferences: CommunicationPreferencesDto;
  /** ETag value for optimistic concurrency — exposed as a typed field for convenience */
  eTag?: string;
}

/** Write model sent via PATCH /api/patients/me — only editable fields */
export interface UpdatePatientProfileDto {
  phone: string;
  address: AddressDto;
  emergencyContact: EmergencyContactDto;
  communicationPreferences: CommunicationPreferencesDto;
}
