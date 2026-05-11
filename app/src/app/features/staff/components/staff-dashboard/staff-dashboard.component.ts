import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { RouterLink, RouterLinkActive, Router } from '@angular/router';
import { DatePipe, SlicePipe } from '@angular/common';
import { RequiresAttentionSectionComponent } from '../requires-attention-section/requires-attention-section.component';
import { QueueService } from '../../queue/queue.service';
import { QueueItem } from '../../queue/queue.models';
import { AuthService } from '../../../../features/auth/services/auth.service';

/**
 * Staff dashboard page — the primary landing view for Staff and Admin users
 * at the route `/staff/dashboard`.
 *
 * Prominently surfaces the "Requires Attention" section (AC-4 of US_032)
 * above any other dashboard widgets, so unacknowledged High-risk appointments
 * are immediately visible on login.
 *
 * Route: /staff/dashboard — protected by authGuard + staffGuard.
 */
@Component({
  selector: 'app-staff-dashboard',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RequiresAttentionSectionComponent,
    RouterLink,
    RouterLinkActive,
    DatePipe,
    SlicePipe,
  ],
  templateUrl: './staff-dashboard.component.html',
  styleUrl: './staff-dashboard.component.scss',
})
export class StaffDashboardComponent implements OnInit {
  readonly today = new Date();

  protected readonly queueItems = signal<QueueItem[]>([]);
  protected readonly queueLoading = signal(false);
  protected readonly queueError = signal<string | null>(null);
  protected readonly checkingInId = signal<string | null>(null);

  private readonly queueService = inject(QueueService);
  readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  /** First name of the logged-in staff member, derived from the full display name. */
  protected readonly greetingName = computed(() => {
    const name = this.authService.currentUserName();
    if (!name) return 'there';
    return name.split(' ')[0];
  });

  /** Time-of-day greeting word. */
  protected readonly greetingWord = computed(() => {
    const h = new Date().getHours();
    if (h < 12) return 'morning';
    if (h < 18) return 'afternoon';
    return 'evening';
  });

  ngOnInit(): void {
    this.loadQueue();
  }

  loadQueue(): void {
    this.queueLoading.set(true);
    this.queueError.set(null);
    this.queueService.getQueue().subscribe({
      next: (items) => {
        this.queueItems.set(items);
        this.queueLoading.set(false);
      },
      error: () => {
        this.queueError.set('Failed to load queue data.');
        this.queueLoading.set(false);
      },
    });
  }

  checkIn(appointmentId: string): void {
    this.checkingInId.set(appointmentId);
    this.queueService.markArrived(appointmentId).subscribe({
      next: () => {
        this.checkingInId.set(null);
        this.loadQueue();
      },
      error: () => {
        this.checkingInId.set(null);
      },
    });
  }

  viewAppointment(appointmentId: string): void {
    void this.router.navigate(['/staff/appointments', appointmentId]);
  }
}
