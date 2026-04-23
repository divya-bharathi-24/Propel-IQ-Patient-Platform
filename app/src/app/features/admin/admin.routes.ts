import { Routes } from '@angular/router';
import { adminGuard } from '../../core/guards/admin.guard';

export const adminRoutes: Routes = [
  {
    path: 'users',
    loadComponent: () =>
      import('./pages/user-management/user-management.page').then(
        (m) => m.UserManagementPageComponent,
      ),
    title: 'User Management — Propel IQ Admin',
  },
  {
    path: 'audit-log',
    loadComponent: () =>
      import('./pages/audit-log/audit-log.page').then(
        (m) => m.AuditLogPageComponent,
      ),
    canActivate: [adminGuard],
    title: 'Audit Log — Propel IQ Admin',
  },
  {
    path: '',
    redirectTo: 'users',
    pathMatch: 'full',
  },
];
