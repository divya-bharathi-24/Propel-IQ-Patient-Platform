import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, tap, throwError } from 'rxjs';
import { AuthState, TokenResponse } from '../../../core/auth/auth-state.model';
import {
  RegistrationRequest,
  RegistrationResponse,
  ResendVerificationRequest,
  ResendVerificationResponse,
  VerifyEmailResponse,
} from '../models/registration.models';

export interface AuthServiceError {
  status: number;
  message: string;
}

/** Seconds before expiry at which a proactive background refresh is triggered. */
const PROACTIVE_REFRESH_THRESHOLD_S = 60;

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly apiBase = '/api/auth';

  // ── In-memory token state (never written to localStorage) ────────────────
  private readonly _authState = signal<AuthState>({
    accessToken: null,
    refreshToken: null,
    userId: null,
    role: null,
    deviceId: null,
    expiresAt: null,
  });

  /** True when a valid access token is held in memory. */
  readonly isAuthenticated = computed(
    () =>
      this._authState().accessToken !== null &&
      (this._authState().expiresAt ?? 0) > Date.now(),
  );

  /** Current access token (read-only). */
  readonly accessToken = computed(() => this._authState().accessToken);

  /** Current refresh token (read-only). */
  readonly refreshToken = computed(() => this._authState().refreshToken);

  /** Role claim from the current JWT (e.g. 'Admin', 'Staff', 'Patient'). */
  readonly currentRole = computed(() => this._authState().role);

  /** The authenticated user's ID from the current JWT. */
  readonly currentUserId = computed(() => this._authState().userId);

  /**
   * True when the access token is within the proactive-refresh window
   * (i.e. expires within the next 60 s).
   */
  readonly isTokenExpiringSoon = computed(() => {
    const { expiresAt } = this._authState();
    if (expiresAt === null) return false;
    return expiresAt - Date.now() < PROACTIVE_REFRESH_THRESHOLD_S * 1_000;
  });

  // ── Session timer reference (lazy-injected to avoid circular dep) ─────────
  private _sessionTimerStopFn: (() => void) | null = null;

  /** Registers a stop callback provided by SessionTimerService. */
  registerSessionTimerStop(fn: () => void): void {
    this._sessionTimerStopFn = fn;
  }

  // ── Auth API calls ────────────────────────────────────────────────────────

  /**
   * Authenticates the user and stores the returned tokens in memory.
   * Navigates to /dashboard on success.
   */
  login(email: string, password: string): Observable<TokenResponse> {
    return this.http
      .post<TokenResponse>(`${this.apiBase}/login`, { email, password })
      .pipe(
        tap((res) => this._storeTokens(res)),
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  /**
   * Silently exchanges the current refresh token for a new token pair.
   * Called by AuthInterceptor; throws on failure so the interceptor can
   * force a full logout.
   */
  refresh(): Observable<TokenResponse> {
    const currentRefresh = this._authState().refreshToken;
    const currentDeviceId = this._authState().deviceId;

    return this.http
      .post<TokenResponse>(`${this.apiBase}/refresh`, {
        refreshToken: currentRefresh,
        deviceId: currentDeviceId,
      })
      .pipe(
        tap((res) => this._storeTokens(res)),
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  /**
   * Clears all in-memory tokens, stops the session timer, notifies the
   * backend (fire-and-forget), and navigates to /auth/login.
   */
  logout(reason?: 'idle_timeout' | 'session_expired'): void {
    const { refreshToken, deviceId } = this._authState();

    this._clearState();
    this._sessionTimerStopFn?.();

    // Fire-and-forget — errors are intentionally suppressed
    if (refreshToken && deviceId) {
      this.http
        .post(`${this.apiBase}/logout`, { refreshToken, deviceId })
        .pipe(catchError(() => []))
        .subscribe();
    }

    const queryParams = reason ? { reason } : {};
    this.router.navigate(['/auth/login'], { queryParams });
  }

  // ── Private helpers ───────────────────────────────────────────────────────

  private _storeTokens(res: TokenResponse): void {
    console.log('[AuthService] Storing tokens:', {
      hasAccessToken: !!res.accessToken,
      hasRefreshToken: !!res.refreshToken,
      userId: res.userId,
      role: res.role,
      deviceId: res.deviceId,
      expiresIn: res.expiresIn,
    });

    this._authState.set({
      accessToken: res.accessToken,
      refreshToken: res.refreshToken,
      userId: res.userId,
      role: res.role,
      deviceId: res.deviceId,
      expiresAt: Date.now() + res.expiresIn * 1_000,
    });

    console.log('[AuthService] Token state after storage:', {
      hasAccessToken: !!this._authState().accessToken,
      isAuthenticated: this.isAuthenticated(),
      deviceId: this._authState().deviceId,
      expiresAt: new Date(this._authState().expiresAt || 0).toISOString(),
    });
  }

  private _clearState(): void {
    this._authState.set({
      accessToken: null,
      refreshToken: null,
      userId: null,
      role: null,
      deviceId: null,
      expiresAt: null,
    });
  }

  // ── Registration API calls ────────────────────────────────────────────────

  register(dto: RegistrationRequest): Observable<RegistrationResponse> {
    return this.http
      .post<RegistrationResponse>(`${this.apiBase}/register`, dto)
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  verifyEmail(token: string): Observable<VerifyEmailResponse> {
    return this.http
      .get<VerifyEmailResponse>(`${this.apiBase}/verify`, { params: { token } })
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  resendVerification(email: string): Observable<ResendVerificationResponse> {
    const body: ResendVerificationRequest = { email };
    return this.http
      .post<ResendVerificationResponse>(
        `${this.apiBase}/resend-verification`,
        body,
      )
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  /**
   * Submits the one-time setup token and chosen password to complete
   * credential setup for a newly invited Staff or Admin user.
   */
  setupCredentials(dto: {
    token: string;
    password: string;
  }): Observable<{ message: string }> {
    return this.http
      .post<{ message: string }>(`${this.apiBase}/setup-credentials`, dto)
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  // ── Shared error mapper ───────────────────────────────────────────────────

  private mapError(err: HttpErrorResponse): AuthServiceError {
    return {
      status: err.status,
      message: err.error?.message ?? 'An unexpected error occurred.',
    };
  }
}
