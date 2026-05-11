import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../features/auth/services/auth.service';
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
  imports: [QueueRowComponent, RouterLink, RouterLinkActive, FormsModule],
  templateUrl: './same-day-queue.component.html',
  styleUrl: './same-day-queue.component.scss',
})
export class SameDayQueueComponent {
  private readonly queueService = inject(QueueService);
  private readonly destroyRef = inject(DestroyRef);
  readonly authService = inject(AuthService);

  readonly queueItems = signal<QueueItem[]>([]);
  readonly isLoading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  searchQuery = '';
  statusFilter = '';

  readonly filteredItems = computed(() => {
    const items = this.queueItems();
    const q = this.searchQuery.toLowerCase().trim();
    const s = this.statusFilter;
    return items.filter(
      (item) =>
        (!q || item.patientName.toLowerCase().includes(q)) &&
        (!s || item.arrivalStatus === s),
    );
  });

  readonly waitingCount = computed(
    () => this.queueItems().filter((i) => i.arrivalStatus === 'Waiting').length,
  );

  readonly arrivedCount = computed(
    () => this.queueItems().filter((i) => i.arrivalStatus === 'Arrived').length,
  );

  readonly completedCount = computed(
    () => this.queueItems().filter((i) => i.arrivalStatus === 'Cancelled').length,
  );

  readonly skeletonRows = [1, 2, 3, 4, 5];
  readonly refresh$ = new Subject<void>();

  constructor() {
    merge(of(null), interval(10_000), this.refresh$)
      .pipe(
        switchMap(() =>
          this.queueService.getQueue().pipe(
            catchError(() => {
              this.errorMessage.set('Unable to refresh queue. Retrying automatically…');
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

  onMarkArrived(appointmentId: string): void {
    this.queueItems.update((items) =>
      items.map((item) =>
        item.appointmentId === appointmentId
          ? { ...item, arrivalStatus: 'Arrived' as const, arrivalTimestamp: new Date().toISOString() }
          : item,
      ),
    );
    this.queueService.markArrived(appointmentId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ error: () => this.refresh$.next() });
  }

  onRevertArrived(appointmentId: string): void {
    this.queueItems.update((items) =>
      items.map((item) =>
        item.appointmentId === appointmentId
          ? { ...item, arrivalStatus: 'Waiting' as const, arrivalTimestamp: null }
          : item,
      ),
    );
    this.queueService.revertArrived(appointmentId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ error: () => this.refresh$.next() });
  }
}
