import { inject } from '@angular/core';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, catchError, pipe, switchMap, tap } from 'rxjs';
import { AuditEventDto, AuditLogQueryParams } from '../models/admin.models';
import { AuditLogService } from '../services/audit-log.service';

export interface AuditLogState {
  events: AuditEventDto[];
  loading: boolean;
  filters: AuditLogQueryParams;
  nextCursor: string | null;
  totalCount: number;
  errorMessage: string | null;
}

const initialState: AuditLogState = {
  events: [],
  loading: false,
  filters: {},
  nextCursor: null,
  totalCount: 0,
  errorMessage: null,
};

/**
 * NgRx Signals store for the Audit Log page (US_047 / FR-057).
 *
 * - `loadAuditLogs()` — resets event list and fetches first page using current filters.
 * - `loadMore()` — appends the next page using `nextCursor`; no-op when cursor is null.
 * - `applyFilters(filters)` — updates filter state then delegates to `loadAuditLogs()`.
 *
 * Cursor-based pagination: page size is fixed at 50 (enforced in AuditLogService).
 */
export const AuditLogStore = signalStore(
  { providedIn: 'root' },
  withState<AuditLogState>(initialState),
  withMethods((store, service = inject(AuditLogService)) => ({
    /**
     * Loads the first page of audit events.
     * Resets `events` and `nextCursor` before fetching so filters are applied cleanly.
     */
    loadAuditLogs: rxMethod<void>(
      pipe(
        tap(() =>
          patchState(store, {
            loading: true,
            errorMessage: null,
            events: [],
            nextCursor: null,
          }),
        ),
        switchMap(() =>
          service.getAuditLogs(store.filters()).pipe(
            tap((response) =>
              patchState(store, {
                events: response.events,
                nextCursor: response.nextCursor,
                totalCount: response.totalCount,
                loading: false,
              }),
            ),
            catchError(() => {
              patchState(store, {
                loading: false,
                errorMessage: 'Failed to load audit log. Please try again.',
              });
              return EMPTY;
            }),
          ),
        ),
      ),
    ),

    /**
     * Appends the next 50 records using `nextCursor`.
     * No-op when `nextCursor` is null (end of data).
     */
    loadMore: rxMethod<void>(
      pipe(
        tap(() => {
          if (!store.nextCursor()) return;
          patchState(store, { loading: true, errorMessage: null });
        }),
        switchMap(() => {
          const cursor = store.nextCursor();
          if (!cursor) return EMPTY;
          return service.getAuditLogs({ ...store.filters(), cursor }).pipe(
            tap((response) =>
              patchState(store, {
                events: [...store.events(), ...response.events],
                nextCursor: response.nextCursor,
                totalCount: response.totalCount,
                loading: false,
              }),
            ),
            catchError(() => {
              patchState(store, {
                loading: false,
                errorMessage:
                  'Failed to load additional records. Please try again.',
              });
              return EMPTY;
            }),
          );
        }),
      ),
    ),

    /**
     * Replaces the active filter set and reloads from the first page.
     *
     * @param filters - Partial filter params; pass `{}` to clear all filters.
     */
    applyFilters(filters: AuditLogQueryParams): void {
      patchState(store, { filters });
      // Trigger a fresh load with the new filters via loadAuditLogs.
      // We call the service directly here to avoid rxMethod self-referencing.
      patchState(store, {
        loading: true,
        errorMessage: null,
        events: [],
        nextCursor: null,
      });
      service
        .getAuditLogs(filters)
        .pipe(
          tap((response) =>
            patchState(store, {
              events: response.events,
              nextCursor: response.nextCursor,
              totalCount: response.totalCount,
              loading: false,
            }),
          ),
          catchError(() => {
            patchState(store, {
              loading: false,
              errorMessage: 'Failed to load audit log. Please try again.',
            });
            return EMPTY;
          }),
        )
        .subscribe();
    },
  })),
);
