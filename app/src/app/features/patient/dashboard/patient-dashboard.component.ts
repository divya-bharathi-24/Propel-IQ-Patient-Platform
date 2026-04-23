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
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDividerModule } from '@angular/material/divider';
import { AppointmentStatusBadgeComponent } from '../../../shared/components/appointment-status-badge/appointment-status-badge.component';
import { DocumentStatusChipComponent } from '../../../shared/components/document-status-chip/document-status-chip.component';
import { ViewReadinessIndicatorComponent } from '../../../shared/components/view-readiness-indicator/view-readiness-indicator.component';
import { InsuranceStatusBadgeComponent } from '../../../shared/components/insurance-status-badge/insurance-status-badge.component';
import { OutlookCalendarSyncComponent } from '../../calendar/outlook-sync/outlook-calendar-sync.component';
import { PatientDashboardService } from './patient-dashboard.service';
import {
  DashboardLoadState,
  PatientDashboardDto,
  UpcomingAppointmentItem,
} from './patient-dashboard.model';
import { InsuranceCheckResult } from '../../../shared/models/insurance.models';

@Component({
  selector: 'app-patient-dashboard',
  standalone: true,
  imports: [
    RouterLink,
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatDividerModule,
    AppointmentStatusBadgeComponent,
    DocumentStatusChipComponent,
    ViewReadinessIndicatorComponent,
    InsuranceStatusBadgeComponent,
    OutlookCalendarSyncComponent,
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

  /**
   * Adapts the appointment's insuranceStatus string into an `InsuranceCheckResult`
   * for the reusable badge component (AC-4, FR-039).
   * Returns null when no insurance status is present.
   */
  appointmentInsuranceResult(
    appt: UpcomingAppointmentItem,
  ): InsuranceCheckResult | null {
    if (!appt.insuranceStatus) return null;
    return { status: appt.insuranceStatus, guidance: '' };
  }
}
