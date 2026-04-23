import { RiskSeverity } from '../../../shared/components/risk-badge/risk-badge.component';

/** Possible lifecycle states of a single intervention row. */
export type InterventionStatus = 'Pending' | 'Accepted' | 'Dismissed';

/** A single recommended intervention for a high-risk appointment. */
export interface RiskInterventionDto {
  /** Server-assigned UUID for the intervention row. */
  id: string;
  /** Human-readable label, e.g. "Send additional reminder" or "Request callback". */
  label: string;
  /** Current acknowledgement state. */
  status: InterventionStatus;
  /** Staff member ID who accepted/dismissed, if acknowledged. */
  staffId: string | null;
  /** ISO UTC timestamp of the acknowledgement action. */
  acknowledgedAt: string | null;
  /** Optional dismissal reason (max 500 chars). */
  dismissalReason: string | null;
}

/** Represents one unacknowledged high-risk appointment in the "Requires Attention" list. */
export interface RequiresAttentionItemDto {
  /** Appointment UUID. */
  appointmentId: string;
  /** Display name of the patient. */
  patientName: string;
  /** ISO UTC timestamp of the appointment. */
  appointmentTime: string;
  /** Severity — always 'High' in this list, but typed for type-safety. */
  riskSeverity: RiskSeverity;
  /** Number of pending (unacknowledged) interventions. */
  pendingCount: number;
}

/** Loading lifecycle states reused across risk-flag stores. */
export type RiskFlagLoadingState = 'idle' | 'loading' | 'loaded' | 'error';
