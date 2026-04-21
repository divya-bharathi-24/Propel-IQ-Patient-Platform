import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../../features/auth/services/auth.service';

/**
 * Route guard that restricts access to Admin-role users only.
 *
 * - Unauthenticated → redirects to /auth/login.
 * - Authenticated but non-Admin → redirects to /access-denied (HTTP 403
 *   is enforced server-side; this guard prevents client-side navigation only).
 */
export const adminGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    return router.createUrlTree(['/auth/login']);
  }

  if (authService.currentRole() !== 'Admin') {
    return router.createUrlTree(['/access-denied']);
  }

  return true;
};
