import { Component, OnInit, inject } from '@angular/core';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuditLogQueryParams } from '../../models/admin.models';
import { AuditLogStore } from '../../store/audit-log.store';
import { AuditEventTableComponent } from '../../components/audit-event-table/audit-event-table.component';
import { AuditFilterPanelComponent } from '../../components/audit-filter-panel/audit-filter-panel.component';

/**
 * Routed container page for the Audit Log view (US_047 / FR-057, FR-058, FR-059).
 *
 * Route: /admin/audit-log (protected by adminGuard at app.routes.ts level)
 *
 * Responsibilities:
 * - Loads the first page of audit events on `ngOnInit` via `AuditLogStore.loadAuditLogs()`.
 * - Renders `AuditFilterPanelComponent` and `AuditEventTableComponent` stacked vertically.
 * - Delegates filter application and clearing to the store.
 *
 * FR-059: NO export, download, or copy button is present anywhere on this page.
 */
@Component({
  selector: 'app-audit-log-page',
  standalone: true,
  imports: [
    MatProgressSpinnerModule,
    AuditEventTableComponent,
    AuditFilterPanelComponent,
  ],
  templateUrl: './audit-log.page.html',
  styleUrl: './audit-log.page.scss',
})
export class AuditLogPageComponent implements OnInit {
  protected readonly store = inject(AuditLogStore);

  ngOnInit(): void {
    this.store.loadAuditLogs();
  }

  onFiltersApplied(params: AuditLogQueryParams): void {
    this.store.applyFilters(params);
  }

  onFiltersCleared(): void {
    this.store.applyFilters({});
  }
}
