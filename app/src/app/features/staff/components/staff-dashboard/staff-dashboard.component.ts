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
import { QueueService } from '../../queue/queue.service';
import { QueueItem } from '../../queue/queue.models';
import { AuthService } from '../../../../features/auth/services/auth.service';
import { StaffAppointmentService } from '../../services/staff-appointment.service';
import { StaffAppointmentDto } from '../../models/staff-appointment.models';

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

  // Queue signals (loaded by QueueService)
  protected readonly queueItems = signal<QueueItem[]>([]);
  protected readonly queueLoading = signal(false);
  protected readonly queueError = signal<string | null>(null);
  protected readonly checkingInId = signal<string | null>(null);

  // Appointment stats signals
  protected readonly todayAppointments = signal<StaffAppointmentDto[]>([]);
  protected readonly appointmentsLoading = signal(false);

  // Pending intakes signal
  protected readonly pendingIntakesCount = signal<number | null>(null);
  protected readonly pendingIntakesLoading = signal(false);

  // Derived stats
  protected readonly totalAppointmentsCount = computed(() => this.todayAppointments().length);
  protected readonly highRiskCount = computed(() =>
    this.todayAppointments().filter(a => a.noShowRisk?.severity === 'High').length
  );
  protected readonly queueSize = computed(() => this.queueItems().length);

  private readonly queueService = inject(QueueService);
  private readonly appointmentService = inject(StaffAppointmentService);
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
    this.loadTodayAppointments();
    this.loadPendingIntakesCount();
  }

  loadTodayAppointments(): void {
    this.appointmentsLoading.set(true);
    const dateStr = this.today.toISOString().split('T')[0];
    this.appointmentService.getAppointments(dateStr).subscribe({
      next: (items) => {
        this.todayAppointments.set(items);
        this.appointmentsLoading.set(false);
      },
      error: () => this.appointmentsLoading.set(false),
    });
  }

  loadPendingIntakesCount(): void {
    this.pendingIntakesLoading.set(true);
    this.appointmentService.getPendingIntakesCount().subscribe({
      next: (count) => {
        this.pendingIntakesCount.set(count);
        this.pendingIntakesLoading.set(false);
      },
      error: () => {
        this.pendingIntakesCount.set(null);
        this.pendingIntakesLoading.set(false);
      },
    });
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

  logout(): void {
    this.authService.logout();
  }
}
