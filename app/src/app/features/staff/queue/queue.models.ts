/** Arrival status of a queue entry */
export type ArrivalStatus = 'Scheduled' | 'Waiting' | 'Arrived' | 'Cancelled';

/** How the appointment was booked */
export type BookingType = 'SelfBooked' | 'WalkIn';

/** Risk band from the no-show risk score */
export type RiskLevel = 'High' | 'Medium' | 'Low';

/** A single row in the same-day queue view */
export interface QueueItem {
  appointmentId: string;
  /** FK to the patient record; null for anonymous walk-ins */
  patientId: string | null;
  patientName: string;
  /** Sequential position in today's queue (e.g. 1, 2, 3…) */
  queuePosition: number;
  /** Free-text reason for the visit; null when not recorded */
  chiefComplaint: string | null;
  /** Formatted as "HH:mm" */
  timeSlotStart: string;
  /** Human-readable elapsed wait (e.g. "47 min"); null when not applicable */
  waitTime: string | null;
  /** No-show risk band; null when no score has been calculated */
  riskLevel: RiskLevel | null;
  bookingType: BookingType;
  arrivalStatus: ArrivalStatus;
  /** ISO UTC timestamp of arrival — null when not yet arrived */
  arrivalTimestamp: string | null;
}
