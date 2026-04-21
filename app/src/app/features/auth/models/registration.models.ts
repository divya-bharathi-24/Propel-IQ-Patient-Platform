export interface RegistrationRequest {
  email: string;
  password: string;
  name: string;
  phone?: string;
  dateOfBirth: string;
}

export interface RegistrationResponse {
  message: string;
}

export interface VerifyEmailResponse {
  message: string;
}

export interface ResendVerificationRequest {
  email: string;
}

export interface ResendVerificationResponse {
  message: string;
}

export interface ApiFieldError {
  field: string;
  message: string;
}

export interface ApiValidationErrorResponse {
  errors: ApiFieldError[];
}
