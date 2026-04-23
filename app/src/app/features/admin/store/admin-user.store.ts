import { inject } from '@angular/core';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, catchError, pipe, switchMap, tap } from 'rxjs';
import {
  AdminUser,
  CreateUserRequest,
  UpdateUserRequest,
  UserRole,
} from '../models/admin.models';
import { AdminService } from '../services/admin.service';

export type AdminUserLoadingState = 'idle' | 'loading' | 'success' | 'error';

export interface AdminUserState {
  users: AdminUser[];
  loadingState: AdminUserLoadingState;
  errorMessage: string | null;
  roleUpdateLoading: boolean;
}

const initialState: AdminUserState = {
  users: [],
  loadingState: 'idle',
  errorMessage: null,
  roleUpdateLoading: false,
};

/**
 * NgRx Signals store managing the admin user list lifecycle.
 *
 * - `loadUsers()` fetches all staff/admin accounts from GET /api/admin/users.
 * - `createUser(payload)` posts to create a new account, appends result to `users`.
 * - `updateUser(id, payload)` patches the named account, replaces matching entry.
 * - `deactivateUser(id)` applies an optimistic `status = 'Deactivated'` then
 *   calls DELETE /api/admin/users/{id}; reverts on error.
 */
export const AdminUserStore = signalStore(
  { providedIn: 'root' },
  withState<AdminUserState>(initialState),
  withMethods((store, service = inject(AdminService)) => ({
    /**
     * Loads all managed users.
     * GET /api/admin/users
     */
    loadUsers: rxMethod<void>(
      pipe(
        tap(() =>
          patchState(store, { loadingState: 'loading', errorMessage: null }),
        ),
        switchMap(() =>
          service.listUsers().pipe(
            tap((users) =>
              patchState(store, { users, loadingState: 'success' }),
            ),
            catchError(() => {
              patchState(store, {
                loadingState: 'error',
                errorMessage: 'Failed to load users. Please try again.',
              });
              return EMPTY;
            }),
          ),
        ),
      ),
    ),

    /**
     * Creates a new staff/admin account and appends it to the user list.
     * POST /api/admin/users
     * Returns an Observable so the caller can react to success/error.
     */
    createUser(payload: CreateUserRequest) {
      return service.createUser(payload).pipe(
        tap(() => {
          // Reload the full list so the new user row is authoritative from the API.
          patchState(store, { loadingState: 'loading' });
          service
            .listUsers()
            .pipe(
              tap((users) =>
                patchState(store, { users, loadingState: 'success' }),
              ),
              catchError(() => {
                patchState(store, {
                  loadingState: 'error',
                  errorMessage: 'User created but list refresh failed.',
                });
                return EMPTY;
              }),
            )
            .subscribe();
        }),
      );
    },

    /**
     * Updates name and/or role for an existing user, replacing the matching
     * entry in the signal state on success.
     * PATCH /api/admin/users/{id}
     */
    updateUser(id: string, payload: UpdateUserRequest) {
      return service.updateUser(id, payload).pipe(
        tap((updated) => {
          patchState(store, {
            users: store.users().map((u) => (u.id === id ? updated : u)),
          });
        }),
      );
    },

    /**
     * Optimistically marks the user as Deactivated, then calls the API.
     * Reverts status on error to keep the UI consistent.
     * DELETE /api/admin/users/{id}
     */
    deactivateUser(id: string) {
      // Optimistic update
      patchState(store, {
        users: store
          .users()
          .map((u) =>
            u.id === id ? { ...u, status: 'Deactivated' as const } : u,
          ),
      });

      return service.deactivateUser(id).pipe(
        catchError((err) => {
          // Revert optimistic update on failure
          patchState(store, {
            users: store
              .users()
              .map((u) =>
                u.id === id ? { ...u, status: 'Active' as const } : u,
              ),
          });
          throw err;
        }),
      );
    },

    /**
     * Updates the role of an existing managed user.
     * Sets `roleUpdateLoading` while the request is in flight.
     * Replaces the matching user entry in the signal state on success.
     * PATCH /api/admin/users/{id}/role
     * reAuthToken is required when elevating to Admin (FR-062).
     */
    updateUserRole(id: string, role: UserRole, reAuthToken?: string) {
      patchState(store, { roleUpdateLoading: true });

      return service.updateUserRole(id, role, reAuthToken).pipe(
        tap((updated) => {
          patchState(store, {
            roleUpdateLoading: false,
            users: store.users().map((u) => (u.id === id ? updated : u)),
          });
        }),
        catchError((err) => {
          patchState(store, { roleUpdateLoading: false });
          throw err;
        }),
      );
    },
  })),
);
