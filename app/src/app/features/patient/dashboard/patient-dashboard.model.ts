export type AppointmentStatus =
  | 'Booked'
  | 'Arrived'
  | 'Completed'
  | 'Cancelled';

export type DocumentProcessingStatus =
  | 'Pending'
  | 'Processing'
  | 'Completed'
  | 'Failed';

export interface UpcomingAppointmentItem {
  id: string;
  date: string;
  timeSlotStart: string;
  specialty: string;
  status: AppointmentStatus;
  hasPendingIntake: boolean;
  hasSubmittedIntake: boolean;
  /**
   * Insurance pre-check result for this appointment (AC-4, FR-039).
   * Null when no insurance check was performed.
   */
  insuranceStatus?:
    | 'Verified'
    | 'NotRecognized'
    | 'Incomplete'
    | 'CheckPending'
    | null;
}

export interface DocumentHistoryItem {
  id: string;
  fileName: string;
  uploadedAt: string;
  processingStatus: DocumentProcessingStatus;
}

export interface PatientDashboardDto {
  upcomingAppointments: UpcomingAppointmentItem[];
  documents: DocumentHistoryItem[];
  viewVerified: boolean;
}

export type DashboardLoadState = 'idle' | 'loading' | 'success' | 'error';

export interface DashboardLoadError {
  message: string;
  statusCode?: number;
}
