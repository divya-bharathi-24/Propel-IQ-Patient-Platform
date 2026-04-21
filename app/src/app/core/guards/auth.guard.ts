import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../../features/auth/services/auth.service';

/**
 * Route guard that protects authenticated routes.
 *
 * - Unauthenticated → redirects to /auth/login.
 * - Token expiring soon → triggers a background refresh before allowing
 *   navigation (best-effort; does not block if the refresh fails).
 */
export const authGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    return router.createUrlTree(['/auth/login']);
  }

  // Proactive background refresh when within the last 60 s of expiry.
  // The guard allows navigation immediately; the interceptor handles any
  // 401 that might occur while the refresh is in flight.
  if (authService.isTokenExpiringSoon()) {
    authService.refresh().subscribe({
      error: () => authService.logout('session_expired'),
    });
  }

  return true;
};
