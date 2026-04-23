import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { RequiresAttentionSectionComponent } from '../requires-attention-section/requires-attention-section.component';

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
  imports: [RequiresAttentionSectionComponent, RouterLink],
  template: `
    <div class="dashboard-page">
      <header class="page-header">
        <h1 class="page-title">Staff Dashboard</h1>
        <p class="page-subtitle">
          Manage appointments, walk-ins, and high-risk patient flags.
        </p>
      </header>

      <!-- Requires Attention is rendered at the top (AC-4) -->
      <app-requires-attention-section />

      <!-- Quick navigation links -->
      <nav class="quick-nav" aria-label="Staff quick navigation">
        <a
          class="nav-card"
          routerLink="/staff/appointments"
          aria-label="Manage appointments"
        >
          <span class="nav-icon" aria-hidden="true">📅</span>
          <span class="nav-label">Appointments</span>
        </a>
        <a
          class="nav-card"
          routerLink="/staff/walkin"
          aria-label="Manage walk-in bookings"
        >
          <span class="nav-icon" aria-hidden="true">🚶</span>
          <span class="nav-label">Walk-In</span>
        </a>
        <a
          class="nav-card"
          routerLink="/staff/queue"
          aria-label="Manage same-day queue"
        >
          <span class="nav-icon" aria-hidden="true">🏥</span>
          <span class="nav-label">Queue</span>
        </a>
        <a
          class="nav-card"
          routerLink="/staff/settings/reminders"
          aria-label="Configure reminder settings"
        >
          <span class="nav-icon" aria-hidden="true">⏰</span>
          <span class="nav-label">Reminders</span>
        </a>
      </nav>
    </div>
  `,
  styles: [
    `
      .dashboard-page {
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

      /* ── Quick nav cards ────────────────────── */
      .quick-nav {
        display: flex;
        gap: 16px;
        flex-wrap: wrap;
        margin-top: 8px;
      }

      .nav-card {
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: 8px;
        width: 140px;
        padding: 20px 16px;
        background-color: #fff;
        border: 1px solid #e0e0e0;
        border-radius: 8px;
        text-decoration: none;
        color: #212121;
        transition:
          border-color 0.15s,
          box-shadow 0.15s;
      }

      .nav-card:hover {
        border-color: #1565c0;
        box-shadow: 0 2px 8px rgba(21, 101, 192, 0.15);
      }

      .nav-card:focus-visible {
        outline: 2px solid #1565c0;
        outline-offset: 2px;
      }

      .nav-icon {
        font-size: 1.75rem;
      }

      .nav-label {
        font-size: 0.875rem;
        font-weight: 600;
        color: #1565c0;
      }
    `,
  ],
})
export class StaffDashboardComponent {}
