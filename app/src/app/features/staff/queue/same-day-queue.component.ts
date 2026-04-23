import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import {
  EMPTY,
  Subject,
  catchError,
  interval,
  merge,
  of,
  switchMap,
} from 'rxjs';
import { QueueItem } from './queue.models';
import { QueueService } from './queue.service';
import { QueueRowComponent } from './queue-row/queue-row.component';

/**
 * Staff-facing same-day queue page.
 *
 * Features:
 * - Live polling every 10 s via `interval(10_000)` merged with a manual
 *   `refresh$` Subject (AC-3 — new walk-ins appear within 10 s).
 * - `isLoading` is set to `true` only on the initial load to avoid screen
 *   flicker on background polls (AC-3).
 * - Optimistic updates for "Mark as Arrived" / "Undo Arrived" actions;
 *   on API error the next poll restores server state.
 * - WCAG 2.2 AA: `aria-live="polite"` on the table container for screen
 *   reader announcements; action buttons carry descriptive aria-labels
 *   (delegated to QueueRowComponent).
 *
 * Route: /staff/queue — protected by staffGuard (Staff | Admin roles).
 */
@Component({
  selector: 'app-same-day-queue',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [QueueRowComponent, RouterLink],
  template: `
    <div class="queue-page">
      <header class="queue-header">
        <h1 class="queue-title">Same-Day Queue</h1>
        <p class="queue-subtitle">
          Today's appointments — live, refreshing every 10 s
        </p>
      </header>

      <section
        class="queue-content"
        aria-label="Same-Day Appointment Queue"
        aria-live="polite"
        aria-atomic="false"
      >
        @if (isLoading()) {
          <div
            class="skeleton-container"
            aria-label="Loading queue…"
            role="status"
          >
            @for (n of skeletonRows; track n) {
              <div class="skeleton-row">
                <div class="skeleton-cell skeleton-sm"></div>
                <div class="skeleton-cell skeleton-lg"></div>
                <div class="skeleton-cell skeleton-md"></div>
                <div class="skeleton-cell skeleton-md"></div>
                <div class="skeleton-cell skeleton-md"></div>
              </div>
            }
          </div>
        } @else if (errorMessage()) {
          <p class="error-message" role="alert">{{ errorMessage() }}</p>
        } @else {
          <div class="table-wrapper">
            <table class="queue-table" aria-label="Same-Day Queue">
              <thead>
                <tr>
                  <th scope="col">Time</th>
                  <th scope="col">Patient Name</th>
                  <th scope="col">Booking Type</th>
                  <th scope="col">Status</th>
                  <th scope="col">
                    <span class="sr-only">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                @for (item of queueItems(); track item.appointmentId) {
                  <app-queue-row
                    [item]="item"
                    (markArrived)="onMarkArrived($event)"
                    (revertArrived)="onRevertArrived($event)"
                  />
                } @empty {
                  <tr>
                    <td colspan="5" class="empty-state">
                      <p>No appointments scheduled for today.</p>
                      <a routerLink="/staff/walkin" class="cta-link">
                        Add a walk-in
                      </a>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </section>
    </div>
  `,
  styles: [
    `
      .queue-page {
        padding: 24px;
        max-width: 960px;
        margin: 0 auto;
      }

      .queue-header {
        margin-bottom: 24px;
      }

      .queue-title {
        font-size: 1.5rem;
        font-weight: 700;
        color: #212121;
        margin: 0 0 4px;
      }

      .queue-subtitle {
        font-size: 0.875rem;
        color: #757575;
        margin: 0;
      }

      /* ── Table ──────────────────────────────── */
      .table-wrapper {
        overflow-x: auto;
        border-radius: 8px;
        border: 1px solid #e0e0e0;
      }

      .queue-table {
        width: 100%;
        border-collapse: collapse;
        background: #ffffff;
      }

      .queue-table thead tr {
        background-color: #fafafa;
      }

      .queue-table th {
        padding: 12px 16px;
        text-align: left;
        font-size: 0.75rem;
        font-weight: 700;
        color: #616161;
        text-transform: uppercase;
        letter-spacing: 0.05em;
        border-bottom: 1px solid #e0e0e0;
      }

      /* ── Empty state ────────────────────────── */
      .empty-state {
        text-align: center;
        padding: 48px 16px;
        color: #757575;
        font-size: 0.9375rem;
      }

      .empty-state p {
        margin: 0 0 12px;
      }

      .cta-link {
        font-size: 0.875rem;
        font-weight: 600;
        color: #1565c0;
        text-decoration: none;
      }

      .cta-link:hover {
        text-decoration: underline;
      }

      /* ── Error ──────────────────────────────── */
      .error-message {
        padding: 12px 16px;
        border-radius: 4px;
        background-color: #fce4ec;
        color: #b71c1c;
        font-size: 0.875rem;
      }

      /* ── Skeleton loader ────────────────────── */
      .skeleton-container {
        display: flex;
        flex-direction: column;
        gap: 1px;
        border-radius: 8px;
        border: 1px solid #e0e0e0;
        overflow: hidden;
      }

      .skeleton-row {
        display: flex;
        align-items: center;
        gap: 16px;
        padding: 16px;
        background: #ffffff;
        border-bottom: 1px solid #f5f5f5;
      }

      .skeleton-cell {
        height: 14px;
        border-radius: 4px;
        background: linear-gradient(
          90deg,
          #f0f0f0 25%,
          #e0e0e0 50%,
          #f0f0f0 75%
        );
        background-size: 200% 100%;
        animation: shimmer 1.4s infinite;
      }

      .skeleton-sm {
        width: 60px;
      }
      .skeleton-md {
        width: 100px;
      }
      .skeleton-lg {
        flex: 1;
      }

      @keyframes shimmer {
        0% {
          background-position: 200% 0;
        }
        100% {
          background-position: -200% 0;
        }
      }

      /* ── Accessibility ──────────────────────── */
      .sr-only {
        position: absolute;
        width: 1px;
        height: 1px;
        padding: 0;
        margin: -1px;
        overflow: hidden;
        clip: rect(0, 0, 0, 0);
        white-space: nowrap;
        border: 0;
      }

      /* ── Responsive ─────────────────────────── */
      @media (max-width: 768px) {
        .queue-page {
          padding: 16px;
        }

        .queue-table th,
        :host ::ng-deep .queue-row td {
          padding: 8px 10px;
          font-size: 0.8125rem;
        }
      }
    `,
  ],
})
export class SameDayQueueComponent {
  private readonly queueService = inject(QueueService);
  private readonly destroyRef = inject(DestroyRef);

  readonly queueItems = signal<QueueItem[]>([]);
  readonly isLoading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  /** 5 placeholder rows for the skeleton loader */
  readonly skeletonRows = [1, 2, 3, 4, 5];

  private readonly refresh$ = new Subject<void>();

  constructor() {
    merge(of(null), interval(10_000), this.refresh$)
      .pipe(
        switchMap(() =>
          this.queueService.getQueue().pipe(
            catchError(() => {
              this.errorMessage.set(
                'Unable to refresh queue. Retrying automatically…',
              );
              return EMPTY;
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((items) => {
        this.queueItems.set(items);
        this.isLoading.set(false);
        this.errorMessage.set(null);
      });
  }

  /**
   * Optimistically marks the appointment as Arrived in the local signal,
   * then fires the PATCH request. On API error the next poll restores state.
   */
  onMarkArrived(appointmentId: string): void {
    this.queueItems.update((items) =>
      items.map((item) =>
        item.appointmentId === appointmentId
          ? {
              ...item,
              arrivalStatus: 'Arrived' as const,
              arrivalTimestamp: new Date().toISOString(),
            }
          : item,
      ),
    );

    this.queueService
      .markArrived(appointmentId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => this.refresh$.next(),
      });
  }

  /**
   * Optimistically reverts the appointment back to Waiting in the local signal,
   * then fires the PATCH request. On API error the next poll restores state.
   */
  onRevertArrived(appointmentId: string): void {
    this.queueItems.update((items) =>
      items.map((item) =>
        item.appointmentId === appointmentId
          ? {
              ...item,
              arrivalStatus: 'Waiting' as const,
              arrivalTimestamp: null,
            }
          : item,
      ),
    );

    this.queueService
      .revertArrived(appointmentId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => this.refresh$.next(),
      });
  }
}
