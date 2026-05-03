import { Component, inject, output } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatNativeDateModule } from '@angular/material/core';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import {
  AuditActionType,
  AuditEntityType,
  AuditLogQueryParams,
} from '../../models/admin.models';

/** Static list of selectable action types for the filter dropdown. */
const ACTION_TYPE_OPTIONS: Array<{ value: AuditActionType; label: string }> = [
  { value: 'Create', label: 'Create' },
  { value: 'Read', label: 'Read' },
  { value: 'Update', label: 'Update' },
  { value: 'Delete', label: 'Delete' },
];

/**
 * Static list of filterable entity types (FR-057).
 * AuditLog itself is excluded from filtering per task requirements.
 */
const ENTITY_TYPE_OPTIONS: Array<{ value: AuditEntityType; label: string }> = [
  { value: 'Patient', label: 'Patient' },
  { value: 'Appointment', label: 'Appointment' },
  { value: 'Document', label: 'Document' },
  { value: 'IntakeForm', label: 'Intake Form' },
  { value: 'MedicalCode', label: 'Medical Code' },
  { value: 'DataConflict', label: 'Data Conflict' },
  { value: 'User', label: 'User' },
];

/**
 * Filter panel for the Audit Log page (US_047 / AC-2).
 *
 * Emits `filtersApplied` with the current form values when the Apply button
 * is clicked, and `filtersCleared` (with an empty object) when Clear is clicked.
 *
 * Active state: when any control is non-empty the "active-filters" CSS class
 * is applied to the host element so the parent can display a visual indicator.
 */
@Component({
  selector: 'app-audit-filter-panel',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatNativeDateModule,
    MatSelectModule,
  ],
  templateUrl: './audit-filter-panel.component.html',
  styleUrl: './audit-filter-panel.component.scss',
})
export class AuditFilterPanelComponent {
  private readonly fb = inject(FormBuilder);

  readonly filtersApplied = output<AuditLogQueryParams>();
  readonly filtersCleared = output<void>();

  readonly actionTypeOptions = ACTION_TYPE_OPTIONS;
  readonly entityTypeOptions = ENTITY_TYPE_OPTIONS;

  readonly filterForm = this.fb.group({
    dateFrom: [null as Date | null],
    dateTo: [null as Date | null],
    userId: [''],
    actionType: [null as AuditActionType | null],
    entityType: [null as AuditEntityType | null],
  });

  get hasActiveFilters(): boolean {
    const v = this.filterForm.value;
    return !!(
      v.dateFrom ||
      v.dateTo ||
      v.userId?.trim() ||
      v.actionType ||
      v.entityType
    );
  }

  applyFilters(): void {
    const v = this.filterForm.value;
    const params: AuditLogQueryParams = {};

    if (v.dateFrom) {
      params.dateFrom = (v.dateFrom as Date).toISOString();
    }
    if (v.dateTo) {
      params.dateTo = (v.dateTo as Date).toISOString();
    }
    if (v.userId?.trim()) {
      params.userId = v.userId.trim();
    }
    if (v.actionType) {
      params.actionType = v.actionType;
    }
    if (v.entityType) {
      params.entityType = v.entityType;
    }

    this.filtersApplied.emit(params);
  }

  clearFilters(): void {
    this.filterForm.reset();
    this.filtersCleared.emit();
  }
}
