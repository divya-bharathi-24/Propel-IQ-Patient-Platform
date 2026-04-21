import { Routes } from '@angular/router';

export const adminRoutes: Routes = [
  {
    path: 'users',
    loadComponent: () =>
      import('./components/user-list/admin-user-list.component').then(
        (m) => m.AdminUserListComponent,
      ),
  },
  {
    path: '',
    redirectTo: 'users',
    pathMatch: 'full',
  },
];
