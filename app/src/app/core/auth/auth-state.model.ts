/** In-memory session state — never persisted to localStorage (OWASP A02). */
export interface AuthState {
  accessToken: string | null;
  refreshToken: string | null;
  userId: string | null;
  role: string | null;
  deviceId: string | null;
  /** UTC epoch milliseconds at which the access token expires. */
  expiresAt: number | null;
  /** Email address of the logged-in user, used for display purposes only. */
  email: string | null;
}

/** Shape of the login / refresh response from the backend. */
export interface TokenResponse {
  accessToken: string;
  refreshToken: string;
  /** Seconds until access token expires (e.g. 900 for 15 min). */
  expiresIn: number;
  userId: string;
  role: string;
  deviceId: string;
  /** Optional: email returned by the backend (may not always be present). */
  email?: string;
}
