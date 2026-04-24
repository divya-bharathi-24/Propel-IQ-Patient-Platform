import { Routes } from '@angular/router';
import { authGuard } from '../../core/guards/auth.guard';
import { staffGuard } from '../../core/guards/staff.guard';

export const staffRoutes: Routes = [
  {
    path: 'dashboard',
    loadComponent: () =>
      import('./components/staff-dashboard/staff-dashboard.component').then(
        (m) => m.StaffDashboardComponent,
      ),
    canActivate: [authGuard, staffGuard],
    title: 'Staff Dashboard — Propel IQ',
  },
  {
    path: 'walkin',
    loadComponent: () =>
      import('./components/walkin-booking/walkin-booking.component').then(
        (m) => m.WalkInBookingComponent,
      ),
    canActivate: [authGuard, staffGuard],
    title: 'Walk-In Booking — Propel IQ',
  },
  {
    path: 'queue',
    loadComponent: () =>
      import('./queue/same-day-queue.component').then(
        (m) => m.SameDayQueueComponent,
      ),
    canActivate: [authGuard, staffGuard],
    title: 'Same-Day Queue — Propel IQ',
  },
  {
    path: 'appointments',
    loadComponent: () =>
      import('./appointments/staff-appointment-list/staff-appointment-list.component').then(
        (m) => m.StaffAppointmentListComponent,
      ),
    canActivate: [authGuard, staffGuard],
    title: 'Appointment Management — Propel IQ',
  },
  {
    path: 'appointments/:id',
    loadComponent: () =>
      import('./appointments/appointment-detail/appointment-detail.component').then(
        (m) => m.AppointmentDetailComponent,
      ),
    canActivate: [authGuard, staffGuard],
    title: 'Appointment Detail — Propel IQ',
  },
  {
    path: 'settings/reminders',
    loadComponent: () =>
      import('./settings/reminder-settings/reminder-settings.component').then(
        (m) => m.ReminderSettingsComponent,
      ),
    canActivate: [authGuard, staffGuard],
    title: 'Reminder Settings — Propel IQ',
  },
  {
    path: 'patients/:patientId',
    loadComponent: () =>
      import('./patient-record/staff-patient-record.component').then(
        (m) => m.StaffPatientRecordComponent,
      ),
    canActivate: [authGuard, staffGuard],
    title: 'Patient Record — Propel IQ',
  },
  {
    path: 'patients/:patientId/360-view',
    loadComponent: () =>
      import('./patients/patient-360-view/patient-360-view.component').then(
        (m) => m.Patient360ViewComponent,
      ),
    canActivate: [authGuard, staffGuard],
    title: '360° Patient View — Propel IQ',
  },
  {
    path: 'patients/:patientId/medical-codes',
    loadComponent: () =>
      import('./patients/medical-code-review/medical-code-review.page').then(
        (m) => m.MedicalCodeReviewPageComponent,
      ),
    canActivate: [authGuard, staffGuard],
    title: 'Medical Code Review — Propel IQ',
  },
  {
    path: '',
    redirectTo: 'walkin',
    pathMatch: 'full',
  },
];
