import { Routes } from '@angular/router';

export const authRoutes: Routes = [
  {
    path: 'login',
    loadComponent: () =>
      import('./components/login/login.component').then(
        (m) => m.LoginComponent,
      ),
  },
  {
    path: 'register',
    loadComponent: () =>
      import('./components/registration-form/registration-form.component').then(
        (m) => m.RegistrationFormComponent,
      ),
  },
  {
    path: 'verify-pending',
    loadComponent: () =>
      import('./components/email-verification-pending/email-verification-pending.component').then(
        (m) => m.EmailVerificationPendingComponent,
      ),
  },
  {
    path: 'verify',
    loadComponent: () =>
      import('./components/email-verify-callback/email-verify-callback.component').then(
        (m) => m.EmailVerifyCallbackComponent,
      ),
  },
  {
    path: 'setup-credentials',
    loadComponent: () =>
      import('./components/credential-setup/credential-setup.component').then(
        (m) => m.CredentialSetupComponent,
      ),
  },
  {
    path: '',
    redirectTo: 'register',
    pathMatch: 'full',
  },
];
