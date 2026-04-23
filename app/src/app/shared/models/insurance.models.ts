/** Possible outcomes of the insurance soft pre-check (FR-038). */
export type InsuranceStatus =
  | 'Verified'
  | 'NotRecognized'
  | 'Incomplete'
  | 'CheckPending';

/** Request payload sent to POST /api/insurance/pre-check. */
export interface InsurancePreCheckRequest {
  providerName: string;
  insuranceId: string;
}

/** Response returned by POST /api/insurance/pre-check (FR-039). */
export interface InsurancePreCheckResponse {
  status: InsuranceStatus;
  /** Guidance text from the server — must not be hard-coded on the FE. */
  guidance: string;
}

/**
 * Holds the result of an insurance check (or a client-side skip/fallback).
 * Stored on BookingWizardStore and forwarded in the booking confirmation payload.
 */
export interface InsuranceCheckResult {
  status: InsuranceStatus;
  guidance: string;
}
