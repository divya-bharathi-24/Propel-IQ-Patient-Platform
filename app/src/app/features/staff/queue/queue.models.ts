/** Arrival status of a queue entry */
export type ArrivalStatus = 'Waiting' | 'Arrived' | 'Cancelled';

/** How the appointment was booked */
export type BookingType = 'SelfBooked' | 'WalkIn';

/** A single row in the same-day queue view */
export interface QueueItem {
  appointmentId: string;
  patientName: string;
  /** Formatted as "HH:mm" */
  timeSlotStart: string;
  bookingType: BookingType;
  arrivalStatus: ArrivalStatus;
  /** ISO UTC timestamp of arrival — null when not yet arrived */
  arrivalTimestamp: string | null;
}
