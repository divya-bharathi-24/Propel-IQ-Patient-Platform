/** DTO sent to POST /api/appointments/{id}/reschedule */
export interface RescheduleRequestDto {
  newSlotDate: string; // "YYYY-MM-DD"
  newSlotStart: string; // ISO 8601
  newSlotEnd: string; // ISO 8601
  specialtyId: string;
}

/** Response from POST /api/appointments/{id}/cancel */
export interface CancelResponseDto {
  appointmentId: string;
  status: 'Cancelled';
}

/** Minimal appointment summary shown in the cancel dialog and reschedule wizard. */
export interface AppointmentSummary {
  id: string;
  date: string; // "YYYY-MM-DD"
  timeStart: string; // ISO 8601
  timeEnd: string; // ISO 8601
  specialtyId: string;
  specialtyName: string;
}
