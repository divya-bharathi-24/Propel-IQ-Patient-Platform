import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import {
  AdminUser,
  CreateUserRequest,
  CreateUserResponse,
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

  private mapError(err: HttpErrorResponse): AdminServiceError {
    return {
      status: err.status,
      message: err.error?.message ?? 'An unexpected error occurred.',
    };
  }
}
