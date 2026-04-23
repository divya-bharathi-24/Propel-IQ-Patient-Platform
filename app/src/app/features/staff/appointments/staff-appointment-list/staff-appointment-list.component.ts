import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
} from '@angular/core';
import { Router } from '@angular/router';
import { StaffAppointmentStore } from '../../state/staff-appointment.store';
import { RiskBadgeComponent } from '../../../../shared/components/risk-badge/risk-badge.component';
import { AppointmentStatusBadgeComponent } from '../../../../shared/components/appointment-status-badge/appointment-status-badge.component';
import { AppointmentStatus } from '../../../patient/dashboard/patient-dashboard.model';
import { HighRiskFlagBannerComponent } from '../../components/high-risk-flag-banner/high-risk-flag-banner.component';

/**
 * Staff appointment management list page.
 *
 * Renders a date-filtered table of all appointments with an embedded
 * colour-coded no-show risk badge (AC-2, AC-4 of US-031).
 *
 * Accessibility (WCAG 2.2 AA):
 * - Risk column header carries aria-label="No-show risk level".
 * - `RiskBadgeComponent` sets role="status" + full aria-label (colour is
 *   supplementary, not sole risk indicator — WCAG 1.4.1).
 * - Date input has an associated <label>.
 *
 * Route: /staff/appointments — protected by authGuard + staffGuard.
 */
@Component({
  selector: 'app-staff-appointment-list',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RiskBadgeComponent,
    AppointmentStatusBadgeComponent,
    HighRiskFlagBannerComponent,
  ],
  template: `
    <div class="appointment-page">
      <header class="page-header">
        <h1 class="page-title">Appointment Management</h1>
        <p class="page-subtitle">
          View and manage daily appointments with no-show risk indicators
        </p>
      </header>

      <!-- Date selector -->
      <div class="date-control">
        <label for="appointment-date" class="date-label">Date</label>
        <input
          id="appointment-date"
          type="date"
          class="date-input"
          [value]="store.selectedDate()"
          (change)="onDateChange($event)"
          aria-label="Filter appointments by date"
        />
        <button
          class="refresh-btn"
          (click)="refresh()"
          [disabled]="store.loadingState() === 'loading'"
          aria-label="Refresh appointment list"
        >
          Refresh
        </button>
      </div>

      <section
        class="list-content"
        aria-label="Appointment list"
        aria-live="polite"
        aria-atomic="false"
      >
        <!-- Loading skeleton -->
        @if (store.loadingState() === 'loading') {
          <div
            class="skeleton-container"
            role="status"
            aria-label="Loading appointments…"
          >
            @for (n of skeletonRows; track n) {
              <div class="skeleton-row">
                <div class="skeleton-cell skeleton-sm"></div>
                <div class="skeleton-cell skeleton-lg"></div>
                <div class="skeleton-cell skeleton-md"></div>
                <div class="skeleton-cell skeleton-md"></div>
                <div class="skeleton-cell skeleton-sm"></div>
              </div>
            }
          </div>
        } @else if (store.loadingState() === 'error') {
          <p class="error-message" role="alert">{{ store.errorMessage() }}</p>
        } @else {
          <div class="table-wrapper">
            <table class="appointment-table" aria-label="Appointments">
              <thead>
                <tr>
                  <th scope="col">Time</th>
                  <th scope="col">Patient</th>
                  <th scope="col">Specialty</th>
                  <th scope="col">Status</th>
                  <th scope="col" aria-label="No-show risk level">Risk</th>
                  <th scope="col"><span class="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (row of store.appointments(); track row.id) {
                  <tr class="appointment-row">
                    <td class="cell-time">{{ row.timeSlot }}</td>
                    <td class="cell-patient">{{ row.patientName }}</td>
                    <td class="cell-specialty">{{ row.specialty }}</td>
                    <td class="cell-status">
                      <app-appointment-status-badge
                        [status]="asStatus(row.status)"
                      />
                    </td>
                    <td class="cell-risk">
                      <app-risk-badge
                        [severity]="row.noShowRisk?.severity ?? null"
                        [score]="row.noShowRisk?.score ?? null"
                      />
                    </td>
                    <td class="cell-action">
                      <button
                        class="view-btn"
                        (click)="viewDetail(row.id)"
                        [attr.aria-label]="
                          'View details for ' + row.patientName
                        "
                      >
                        View
                      </button>
                    </td>
                  </tr>
                  <!-- High-risk banner row: only rendered when severity is High (AC-1 of US_032) -->
                  @if (row.noShowRisk?.severity === 'High') {
                    <tr class="banner-row">
                      <td colspan="6" class="banner-cell">
                        <app-high-risk-flag-banner [appointmentId]="row.id" />
                      </td>
                    </tr>
                  }
                } @empty {
                  <tr>
                    <td colspan="6" class="empty-state">
                      No appointments for this date.
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
      .appointment-page {
        padding: 24px;
        max-width: 1040px;
        margin: 0 auto;
      }

      .page-header {
        margin-bottom: 20px;
      }

      .page-title {
        font-size: 1.5rem;
        font-weight: 700;
        color: #212121;
        margin: 0 0 4px;
      }

      .page-subtitle {
        font-size: 0.875rem;
        color: #757575;
        margin: 0;
      }

      /* ── Date control ───────────────────────── */
      .date-control {
        display: flex;
        align-items: center;
        gap: 12px;
        margin-bottom: 20px;
      }

      .date-label {
        font-size: 0.875rem;
        font-weight: 600;
        color: #424242;
      }

      .date-input {
        padding: 6px 10px;
        border: 1px solid #bdbdbd;
        border-radius: 4px;
        font-size: 0.875rem;
        color: #212121;
        cursor: pointer;
      }

      .date-input:focus {
        outline: 2px solid #1565c0;
        outline-offset: 1px;
        border-color: #1565c0;
      }

      .refresh-btn {
        padding: 6px 16px;
        border: none;
        border-radius: 4px;
        background-color: #1565c0;
        color: #fff;
        font-size: 0.875rem;
        font-weight: 600;
        cursor: pointer;
        transition: background-color 0.15s;
      }

      .refresh-btn:hover:not(:disabled) {
        background-color: #0d47a1;
      }

      .refresh-btn:disabled {
        background-color: #90caf9;
        cursor: not-allowed;
      }

      /* ── Table ──────────────────────────────── */
      .table-wrapper {
        overflow-x: auto;
        border-radius: 8px;
        border: 1px solid #e0e0e0;
      }

      .appointment-table {
        width: 100%;
        border-collapse: collapse;
        background: #ffffff;
      }

      .appointment-table thead tr {
        background-color: #fafafa;
      }

      .appointment-table th {
        padding: 12px 16px;
        text-align: left;
        font-size: 0.75rem;
        font-weight: 700;
        color: #616161;
        text-transform: uppercase;
        letter-spacing: 0.05em;
        border-bottom: 1px solid #e0e0e0;
      }

      .appointment-row td {
        padding: 12px 16px;
        font-size: 0.875rem;
        color: #212121;
        border-bottom: 1px solid #f5f5f5;
        vertical-align: middle;
      }

      .appointment-row:last-child td {
        border-bottom: none;
      }

      /* ── High-risk banner row ───────────────── */
      .banner-row td {
        padding: 0 16px 12px;
        border-bottom: 1px solid #f5f5f5;
      }

      .banner-cell {
        background-color: #fff;
      }

      .cell-time {
        white-space: nowrap;
        font-variant-numeric: tabular-nums;
      }

      .cell-action {
        white-space: nowrap;
        text-align: right;
      }

      .view-btn {
        padding: 4px 12px;
        border: 1px solid #1565c0;
        border-radius: 4px;
        background: transparent;
        color: #1565c0;
        font-size: 0.8125rem;
        font-weight: 600;
        cursor: pointer;
        transition: background-color 0.15s;

        &:hover {
          background-color: #e3f2fd;
        }

        &:focus-visible {
          outline: 2px solid #1565c0;
          outline-offset: 2px;
        }
      }

      /* Visually hidden but available to screen readers */
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

      /* ── Empty state ────────────────────────── */
      .empty-state {
        text-align: center;
        padding: 48px 16px;
        color: #757575;
        font-size: 0.9375rem;
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
    `,
  ],
})
export class StaffAppointmentListComponent implements OnInit {
  protected readonly store = inject(StaffAppointmentStore);
  protected readonly skeletonRows = [1, 2, 3, 4, 5];
  private readonly router = inject(Router);

  ngOnInit(): void {
    this.store.loadAppointments(this.store.selectedDate());
  }

  onDateChange(event: Event): void {
    const date = (event.target as HTMLInputElement).value;
    if (date) {
      this.store.loadAppointments(date);
    }
  }

  refresh(): void {
    this.store.loadAppointments(this.store.selectedDate());
  }

  /** Navigates to the appointment detail page. */
  viewDetail(appointmentId: string): void {
    void this.router.navigate(['/staff/appointments', appointmentId]);
  }

  /** Casts the raw status string from DTO to the typed AppointmentStatus. */
  asStatus(status: string): AppointmentStatus {
    const allowed: AppointmentStatus[] = [
      'Booked',
      'Arrived',
      'Completed',
      'Cancelled',
    ];
    return allowed.includes(status as AppointmentStatus)
      ? (status as AppointmentStatus)
      : 'Booked';
  }
}
