import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import {
  AdminUser,
  CreateUserRequest,
  CreateUserResponse,
  ReAuthTokenResponse,
  UpdateUserRequest,
  UserRole,
} from '../models/admin.models';

export interface AdminServiceError {
  status: number;
  message: string;
}

@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = '/api/admin';

  listUsers(): Observable<AdminUser[]> {
    return this.http
      .get<AdminUser[]>(`${this.apiBase}/users`)
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  createUser(dto: CreateUserRequest): Observable<CreateUserResponse> {
    return this.http
      .post<CreateUserResponse>(`${this.apiBase}/users`, dto)
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  updateUser(id: string, dto: UpdateUserRequest): Observable<AdminUser> {
    return this.http
      .patch<AdminUser>(`${this.apiBase}/users/${id}`, dto)
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  deactivateUser(id: string): Observable<void> {
    return this.http
      .delete<void>(`${this.apiBase}/users/${id}`)
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  resendCredentialEmail(id: string): Observable<void> {
    return this.http
      .post<void>(`${this.apiBase}/users/${id}/resend-credentials`, {})
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  /**
   * Re-authenticates the current Admin session before a destructive action.
   * POST /api/admin/reauthenticate { currentPassword }
   * Returns a short-lived reAuthToken on success (HTTP 200).
   * Throws with status 401 on incorrect password.
   */
  reauthenticate(password: string): Observable<ReAuthTokenResponse> {
    return this.http
      .post<ReAuthTokenResponse>(`${this.apiBase}/reauthenticate`, {
        currentPassword: password,
      })
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  /**
   * Updates the role of an existing managed user.
   * PATCH /api/admin/users/{id}/role { role, reAuthToken? }
   * reAuthToken is required when elevating to Admin (FR-062).
   */
  updateUserRole(
    id: string,
    role: UserRole,
    reAuthToken?: string,
  ): Observable<AdminUser> {
    return this.http
      .patch<AdminUser>(`${this.apiBase}/users/${id}/role`, {
        role,
        ...(reAuthToken !== undefined ? { reAuthToken } : {}),
      })
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  private mapError(err: HttpErrorResponse): AdminServiceError {
    return {
      status: err.status,
      message: err.error?.message ?? 'An unexpected error occurred.',
    };
  }
}
