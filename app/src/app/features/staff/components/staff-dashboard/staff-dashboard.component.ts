import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { DatePipe } from '@angular/common';
import { RequiresAttentionSectionComponent } from '../requires-attention-section/requires-attention-section.component';
import { AuthService } from '../../../auth/services/auth.service';

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
  imports: [RequiresAttentionSectionComponent, RouterLink, RouterLinkActive, DatePipe],
  templateUrl: './staff-dashboard.component.html',
  styleUrl: './staff-dashboard.component.scss',
})
export class StaffDashboardComponent {
  readonly today = new Date();
  readonly authService = inject(AuthService);
}
