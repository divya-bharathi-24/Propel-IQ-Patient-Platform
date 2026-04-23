export type WaitlistEntryStatus = 'Active' | 'Expired' | 'Booked';

/** A waitlist entry returned by GET /api/waitlist/me */
export interface WaitlistEntryDto {
  id: string;
  appointmentId: string;
  specialtyId: string;
  preferredDate: string; // "YYYY-MM-DD"
  preferredTimeSlot: string; // ISO 8601 start time, e.g. "2026-06-01T09:00:00Z"
  status: WaitlistEntryStatus;
}
