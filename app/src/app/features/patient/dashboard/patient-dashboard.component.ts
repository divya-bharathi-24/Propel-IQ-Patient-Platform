import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe, UpperCasePipe } from '@angular/common';
import { AppointmentStatusBadgeComponent } from '../../../shared/components/appointment-status-badge/appointment-status-badge.component';
import { StatCardComponent } from '../../../shared/components/stat-card/stat-card.component';
import { QuickActionCardComponent } from '../../../shared/components/quick-action-card/quick-action-card.component';
import { PatientDashboardService } from './patient-dashboard.service';
import {
  DashboardLoadState,
  PatientDashboardDto,
  UpcomingAppointmentItem,
} from './patient-dashboard.model';

@Component({
  selector: 'app-patient-dashboard',
  standalone: true,
  imports: [
    RouterLink,
    DatePipe,
    UpperCasePipe,
    AppointmentStatusBadgeComponent,
    StatCardComponent,
    QuickActionCardComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './patient-dashboard.component.html',
  styleUrl: './patient-dashboard.component.scss',
})
export class PatientDashboardComponent implements OnInit {
  private readonly dashboardService = inject(PatientDashboardService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly loadState = signal<DashboardLoadState>('idle');
  readonly dashboard = signal<PatientDashboardDto | null>(null);

  /** Appointments that have a pending intake form — derived reactively. */
  readonly pendingIntakeItems = computed<UpcomingAppointmentItem[]>(() =>
    (this.dashboard()?.upcomingAppointments ?? []).filter(
      (a) => a.hasPendingIntake,
    ),
  );

  ngOnInit(): void {
    this.loadDashboard();
  }

  loadDashboard(): void {
    this.loadState.set('loading');
    this.dashboardService
      .getDashboard()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.dashboard.set(data);
          this.loadState.set('success');
        },
        error: () => {
          this.loadState.set('error');
        },
      });
  }

  onRetryUpload(appointmentId: string): void {
    this.router.navigate(['/documents', 'upload'], {
      queryParams: { appointmentId },
    });
  }
}
