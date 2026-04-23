import { Routes } from '@angular/router';
import { authGuard } from '../../core/guards/auth.guard';

export const appointmentRoutes: Routes = [
  {
    path: 'book',
    loadComponent: () =>
      import('../booking/wizard/booking-wizard.component').then(
        (m) => m.BookingWizardComponent,
      ),
    canActivate: [authGuard],
    title: 'Book Appointment — Propel IQ',
  },
  {
    path: ':id/reschedule',
    loadComponent: () =>
      import('./components/reschedule-wizard/reschedule-wizard.component').then(
        (m) => m.RescheduleWizardComponent,
      ),
    canActivate: [authGuard],
    title: 'Reschedule Appointment — Propel IQ',
  },
  {
    path: '',
    redirectTo: 'book',
    pathMatch: 'full',
  },
];
