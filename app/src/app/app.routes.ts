import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { adminGuard } from './core/guards/admin.guard';
import { staffGuard } from './core/guards/staff.guard';

export const routes: Routes = [
  {
    path: 'auth',
    loadChildren: () =>
      import('./features/auth/auth.routes').then((m) => m.authRoutes),
  },
  {
    path: 'admin',
    loadChildren: () =>
      import('./features/admin/admin.routes').then((m) => m.adminRoutes),
    canActivate: [authGuard, adminGuard],
  },
  {
    path: 'staff',
    loadChildren: () =>
      import('./features/staff/staff.routes').then((m) => m.staffRoutes),
    canActivate: [authGuard, staffGuard],
  },
  {
    path: 'appointments',
    loadChildren: () =>
      import('./features/appointments/appointments.routes').then(
        (m) => m.appointmentRoutes,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'profile',
    loadComponent: () =>
      import('./features/patient/components/profile/patient-profile.component').then(
        (m) => m.PatientProfileComponent,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'dashboard',
    loadComponent: () =>
      import('./features/patient/dashboard/patient-dashboard.component').then(
        (m) => m.PatientDashboardComponent,
      ),
    canActivate: [authGuard],
    title: 'My Dashboard — Propel IQ',
  },
  {
    path: 'intake/:appointmentId',
    loadComponent: () =>
      import('./features/patient/intake/intake-page/intake-page.component').then(
        (m) => m.IntakePageComponent,
      ),
    canActivate: [authGuard],
    title: 'Patient Intake — Propel IQ',
  },
  {
    path: 'intake/edit/:appointmentId',
    loadComponent: () =>
      import('./features/patient/intake/intake-edit.component').then(
        (m) => m.IntakeEditComponent,
      ),
    canActivate: [authGuard],
    title: 'Edit Intake — Propel IQ',
  },
  {
    path: 'intake/ai',
    loadComponent: () =>
      import('./features/patient/intake/ai-intake-chat/ai-intake-chat.component').then(
        (m) => m.AiIntakeChatComponent,
      ),
    canActivate: [authGuard],
    title: 'AI-Assisted Intake — Propel IQ',
  },
  {
    path: 'patient/intake/:appointmentId',
    loadComponent: () =>
      import('./features/patient/intake/manual-intake-form/manual-intake-form.component').then(
        (m) => m.ManualIntakeFormComponent,
      ),
    canActivate: [authGuard],
    title: 'Manual Intake Form — Propel IQ',
  },
  {
    path: 'documents',
    loadComponent: () =>
      import('./features/patient/document-upload/document-upload.component').then(
        (m) => m.DocumentUploadComponent,
      ),
    canActivate: [authGuard],
    title: 'My Documents — Propel IQ',
  },
  {
    path: 'access-denied',
    loadComponent: () =>
      import('./features/auth/components/login/login.component').then(
        (m) => m.LoginComponent,
      ),
  },
  {
    // No auth guard — Microsoft redirects here before the session is restored
    // (EP-007 / US_036 / AC-1).
    path: 'calendar/outlook/callback',
    loadComponent: () =>
      import('./features/calendar/outlook-callback/outlook-callback.component').then(
        (m) => m.OutlookCallbackComponent,
      ),
    title: 'Connecting to Outlook Calendar — Propel IQ',
  },
  {
    path: '',
    redirectTo: 'auth/login',
    pathMatch: 'full',
  },
];
