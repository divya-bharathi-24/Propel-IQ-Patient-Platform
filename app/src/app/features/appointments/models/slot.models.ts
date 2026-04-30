/** A specialty returned by GET /api/appointments/specialties */
export interface SpecialtyDto {
  id: string;
  name: string;
}

/** A single bookable time slot returned by GET /api/appointments/slots */
export interface SlotDto {
  timeSlotStart: string; // ISO 8601, e.g. "2026-05-01T09:00:00Z"
  timeSlotEnd: string; // ISO 8601, e.g. "2026-05-01T09:30:00Z"
  isAvailable: boolean;
  specialtyId: string;
  date: string; // "YYYY-MM-DD"
}

/** Response envelope from GET /api/appointments/slots */
export type SlotAvailabilityResponseDto = SlotDto[];
