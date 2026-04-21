import {
  HttpErrorResponse,
  HttpEvent,
  HttpHandlerFn,
  HttpInterceptorFn,
  HttpRequest,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { BehaviorSubject, Observable, throwError } from 'rxjs';
import { catchError, filter, switchMap, take } from 'rxjs/operators';
import { AuthService } from '../../features/auth/services/auth.service';
import { TokenResponse } from '../auth/auth-state.model';

/** Paths that must never carry an Authorization header. */
const AUTH_BYPASS_PATHS = ['/api/auth/login', '/api/auth/refresh'];

/** Tracks whether a refresh is already in flight (shared across requests). */
let isRefreshing = false;
let refreshSubject = new BehaviorSubject<string | null>(null);

/**
 * Functional HTTP interceptor (Angular 15+ style).
 *
 * Responsibilities:
 * 1. Attach `Authorization: Bearer <token>` to every outbound request
 *    except the auth bypass paths.
 * 2. On HTTP 401, attempt ONE silent token refresh.
 *    - Success → swap stored token and retry the original request.
 *    - Failure → force logout with `reason=session_expired`.
 * 3. Queue concurrent 401 requests behind the in-flight refresh.
 */
export const authInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
): Observable<HttpEvent<unknown>> => {
  const authService = inject(AuthService);

  // Skip auth bypass paths (login, refresh)
  if (AUTH_BYPASS_PATHS.some((path) => req.url.includes(path))) {
    return next(req);
  }

  const token = authService.accessToken();
  const authorizedReq = token ? addAuthHeader(req, token) : req;

  return next(authorizedReq).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && err.status === 401) {
        return handle401(req, next, authService);
      }
      return throwError(() => err);
    }),
  );
};

// ── Helpers ──────────────────────────────────────────────────────────────────

function addAuthHeader(
  req: HttpRequest<unknown>,
  token: string,
): HttpRequest<unknown> {
  return req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}

function handle401(
  originalReq: HttpRequest<unknown>,
  next: HttpHandlerFn,
  authService: AuthService,
): Observable<HttpEvent<unknown>> {
  if (isRefreshing) {
    // Queue behind the in-flight refresh
    return refreshSubject.pipe(
      filter((token): token is string => token !== null),
      take(1),
      switchMap((newToken) => next(addAuthHeader(originalReq, newToken))),
    );
  }

  isRefreshing = true;
  refreshSubject = new BehaviorSubject<string | null>(null);

  return authService.refresh().pipe(
    switchMap((res: TokenResponse) => {
      isRefreshing = false;
      refreshSubject.next(res.accessToken);
      return next(addAuthHeader(originalReq, res.accessToken));
    }),
    catchError((refreshErr: unknown) => {
      isRefreshing = false;
      refreshSubject.next(null);
      // Stolen / expired refresh token → full logout
      authService.logout('session_expired');
      return throwError(() => refreshErr);
    }),
  );
}
