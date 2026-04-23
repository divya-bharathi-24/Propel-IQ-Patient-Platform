export type IntakeMode = 'AiAssisted' | 'Manual';
export type InsuranceStatus =
  | 'Verified'
  | 'NotRecognized'
  | 'Incomplete'
  | 'CheckPending';

export interface AvailableSlot {
  slotId: string;
  specialtyId: string;
  specialtyName: string;
  date: string; // ISO 8601 date, e.g. "2026-05-10"
  timeSlotStart: string; // "HH:mm"
  timeSlotEnd: string; // "HH:mm"
}

export interface InsuranceInfo {
  insurerName: string | null;
  memberId: string | null;
}

export interface CreateBookingRequest {
  slotId: string;
  specialtyId: string;
  intakeMode: IntakeMode;
  insuranceName: string | null;
  insuranceId: string | null;
  /** Preferred waitlist date in "YYYY-MM-DD" format; null when patient skipped. */
  preferredDate: string | null;
  /** Preferred waitlist slot start time (ISO 8601); null when patient skipped. */
  preferredTimeSlot: string | null;
}

export interface BookingResult {
  appointmentId: string;
  referenceNumber: string; // First 8 chars of appointmentId (uppercase)
  date: string;
  timeSlotStart: string;
  timeSlotEnd: string;
  specialtyName: string;
  insuranceStatus: InsuranceStatus;
}
