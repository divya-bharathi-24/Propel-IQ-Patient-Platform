import { Routes } from '@angular/router';
import { authGuard } from '../../core/guards/auth.guard';

export const patientRoutes: Routes = [
  {
    path: 'profile',
    loadComponent: () =>
      import('./components/profile/patient-profile.component').then(
        (m) => m.PatientProfileComponent,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'documents',
    loadComponent: () =>
      import('./document-upload/document-upload.component').then(
        (m) => m.DocumentUploadComponent,
      ),
    canActivate: [authGuard],
    title: 'My Documents — Propel IQ',
  },
];
