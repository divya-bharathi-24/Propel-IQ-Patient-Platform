import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../../features/auth/services/auth.service';

/**
 * Route guard that restricts access to Staff-role users only.
 *
 * - Unauthenticated → redirects to /auth/login.
 * - Authenticated but non-Staff → redirects to /access-denied (HTTP 403
 *   is enforced server-side; this guard prevents client-side navigation only).
 *
 * OWASP A01 — Broken Access Control: ensures walk-in booking interface is
 * inaccessible to Patient-role and other non-Staff users (AC-4).
 */
export const staffGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    return router.createUrlTree(['/auth/login']);
  }

  const role = authService.currentRole();
  if (role !== 'Staff' && role !== 'Admin') {
    return router.createUrlTree(['/access-denied']);
  }

  return true;
};
