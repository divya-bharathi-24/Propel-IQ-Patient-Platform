import { RiskSeverity } from '../../../shared/components/risk-badge/risk-badge.component';

/** No-show risk data embedded in each appointment row. */
export interface NoShowRiskDto {
  /** Raw 0-1 probability score from the risk engine. */
  score: number;
  /** Human-readable severity bucket. */
  severity: RiskSeverity;
  /** Contributing factor keys, e.g. ["prior_no_shows", "long_lead_time"]. */
  factors: string[];
  /** ISO UTC timestamp of when the score was last computed. */
  calculatedAt: string;
}

/** A single row returned by GET /api/staff/appointments?date={date} */
export interface StaffAppointmentDto {
  id: string;
  patientName: string;
  specialty: string;
  /** Formatted as "HH:mm" */
  timeSlot: string;
  status: string;
  /** Null when the risk calculation job has not yet run for this appointment. */
  noShowRisk: NoShowRiskDto | null;
}

/** Loading lifecycle for the appointments store. */
export type AppointmentLoadingState = 'idle' | 'loading' | 'loaded' | 'error';

/** Confirmation data for the last manually triggered ad-hoc reminder (US-034). */
export interface LastManualReminderDto {
  /** ISO UTC timestamp of when the reminder was sent. */
  sentAt: string;
  /** Display name of the staff member who triggered the reminder. */
  triggeredByStaffName: string;
}

/**
 * Extended appointment DTO returned by GET /api/staff/appointments/{id}.
 * Includes patient contact details and last manual reminder metadata.
 */
export interface StaffAppointmentDetailDto extends StaffAppointmentDto {
  patientEmail: string;
  patientPhone: string;
  /** Null when no manual reminder has been sent for this appointment. */
  lastManualReminder: LastManualReminderDto | null;
}
