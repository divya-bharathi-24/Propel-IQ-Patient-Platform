import { Component, Input, signal, inject } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AuditEventDto } from '../../models/admin.models';
import { AuditLogStore } from '../../store/audit-log.store';
import { DiffViewComponent } from '../diff-view/diff-view.component';

/**
 * Presentational + container component rendering the audit event table (US_047 / AC-1, AC-3).
 *
 * - Displays 7 data columns + expand toggle column (8 total).
 * - Row expansion renders `DiffViewComponent` only when `event.details !== null` (FR-058).
 * - "Load More" button appends the next cursor page via `AuditLogStore.loadMore()` (AC-1).
 * - Total-count badge above table shows "Showing X of Y events" (AC-1).
 * - FR-059: NO export, download, or copy controls exist on this component.
 */
@Component({
  selector: 'app-audit-event-table',
  standalone: true,
  imports: [
    CommonModule,
    DatePipe,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTableModule,
    MatTooltipModule,
    DiffViewComponent,
  ],
  templateUrl: './audit-event-table.component.html',
  styleUrl: './audit-event-table.component.scss',
})
export class AuditEventTableComponent {
  @Input({ required: true }) events: AuditEventDto[] = [];
  @Input({ required: true }) loading = false;
  @Input({ required: true }) nextCursor: string | null = null;
  @Input({ required: true }) totalCount = 0;

  protected readonly store = inject(AuditLogStore);

  /** Tracks the currently expanded row (by event id). Null = no row expanded. */
  readonly expandedRowId = signal<string | null>(null);

  readonly displayedColumns = [
    'userId',
    'userRole',
    'entityType',
    'entityId',
    'actionType',
    'ipAddress',
    'timestamp',
    'expand',
  ] as const;

  toggleExpand(event: AuditEventDto): void {
    if (event.details === null) return;
    this.expandedRowId.update((id) => (id === event.id ? null : event.id));
  }

  isExpanded(event: AuditEventDto): boolean {
    return this.expandedRowId() === event.id;
  }

  loadMore(): void {
    this.store.loadMore();
  }
}
