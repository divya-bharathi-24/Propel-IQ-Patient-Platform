/**
 * Medical code models shared across the review feature (US_043).
 *
 * These types mirror the backend DTOs returned by:
 *   GET  /api/patients/{patientId}/medical-codes
 *   POST /api/medical-codes/validate
 *   POST /api/medical-codes/confirm
 */

export type CodeType = 'ICD10' | 'CPT';

export type DecisionStatus = 'Pending' | 'Accepted' | 'Rejected';

/** A single AI-suggested medical code returned from the suggestion pipeline. */
export interface MedicalCodeSuggestionDto {
  /** Unique identifier for the suggestion record. */
  codeId: string;
  /** Code type: ICD-10 diagnostic code or CPT procedure code. */
  codeType: CodeType;
  /** The code string, e.g. "E11.9" or "99213". */
  code: string;
  /** Human-readable description of the code. */
  description: string;
  /** Confidence score in the range [0, 1]. */
  confidenceScore: number;
  /** Raw evidence text extracted from the source document. */
  evidenceText: string;
  /** Flag set by the backend when confidenceScore < 0.80. */
  lowConfidence: boolean;
}

/**
 * Top-level API response shape for GET /api/patients/{patientId}/medical-codes.
 * When no documents have been processed, `suggestions` is empty and `message`
 * contains a user-friendly explanation.
 */
export interface MedicalCodeSuggestionsResponse {
  suggestions: MedicalCodeSuggestionDto[];
  /** Optional message when suggestions are empty (e.g. "No documents processed yet"). */
  message?: string;
}

/** Per-code staff decision held in the local store. */
export interface CodeDecision {
  status: DecisionStatus;
  rejectionReason?: string;
}

/** Request payload for POST /api/medical-codes/validate (AC-4). */
export interface CodeValidationRequest {
  code: string;
  codeType: CodeType;
}

/** Response payload for POST /api/medical-codes/validate. */
export interface CodeValidationResult {
  valid: boolean;
  description?: string;
  errorMessage?: string;
}

/** A single decision item included in the confirm payload. */
export interface CodeDecisionItem {
  codeId: string;
  status: DecisionStatus;
  rejectionReason?: string;
}

/** A manually entered code included in the confirm payload. */
export interface ManualCodeItem {
  code: string;
  codeType: CodeType;
  description: string;
}

/** Request payload for POST /api/medical-codes/confirm. */
export interface ConfirmCodesPayload {
  patientId: string;
  decisions: CodeDecisionItem[];
  manualCodes: ManualCodeItem[];
}
