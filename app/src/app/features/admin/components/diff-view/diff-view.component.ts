import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTableModule } from '@angular/material/table';
import { AuditEventDetails } from '../../models/admin.models';

/**
 * Presentational component that renders a structured before/after diff table
 * from the JSONB `details` field of a clinical audit event (FR-058).
 *
 * - When `details` is non-null, the component shows a three-column table:
 *   field name | old value | new value, with changed fields highlighted.
 * - When `details` is null, a "No detail available" fallback message is shown.
 */
@Component({
  selector: 'app-diff-view',
  standalone: true,
  imports: [CommonModule, MatTableModule],
  template: `
    @if (details) {
      <table
        class="diff-table"
        role="table"
        aria-label="Field change comparison"
      >
        <thead>
          <tr>
            <th scope="col" class="diff-col-field">Field</th>
            <th scope="col" class="diff-col-before">Before</th>
            <th scope="col" class="diff-col-after">After</th>
          </tr>
        </thead>
        <tbody>
          @for (key of fieldKeys; track key) {
            <tr [class.diff-row--changed]="hasChanged(key)">
              <td class="diff-col-field">{{ key }}</td>
              <td class="diff-col-before">
                {{ formatValue(details!.before[key]) }}
              </td>
              <td class="diff-col-after">
                {{ formatValue(details!.after[key]) }}
              </td>
            </tr>
          }
          @if (fieldKeys.length === 0) {
            <tr>
              <td colspan="3" class="diff-empty">No fields recorded.</td>
            </tr>
          }
        </tbody>
      </table>
    } @else {
      <p class="diff-unavailable" role="note">No detail available.</p>
    }
  `,
  styles: [
    `
      .diff-table {
        width: 100%;
        border-collapse: collapse;
        font-size: 0.8125rem;
      }

      .diff-table th,
      .diff-table td {
        padding: 6px 12px;
        border: 1px solid var(--mat-sys-outline-variant, #ccc);
        vertical-align: top;
      }

      .diff-table th {
        background-color: var(--mat-sys-surface-variant, #f4f4f4);
        font-weight: 600;
        text-align: left;
      }

      .diff-col-field {
        width: 30%;
        font-family: monospace;
      }

      .diff-col-before {
        width: 35%;
        color: var(--mat-sys-error, #c00);
        word-break: break-all;
      }

      .diff-col-after {
        width: 35%;
        color: var(--mat-sys-primary, #0066cc);
        word-break: break-all;
      }

      .diff-row--changed td {
        font-weight: 500;
        background-color: var(--mat-sys-surface-container, #f9f9f9);
      }

      .diff-empty,
      .diff-unavailable {
        color: var(--mat-sys-on-surface-variant, #666);
        font-style: italic;
        padding: 8px;
        margin: 0;
      }
    `,
  ],
})
export class DiffViewComponent {
  @Input({ required: true }) details: AuditEventDetails | null = null;

  get fieldKeys(): string[] {
    if (!this.details) return [];
    const allKeys = new Set([
      ...Object.keys(this.details.before),
      ...Object.keys(this.details.after),
    ]);
    return Array.from(allKeys).sort();
  }

  hasChanged(key: string): boolean {
    if (!this.details) return false;
    return (
      JSON.stringify(this.details.before[key]) !==
      JSON.stringify(this.details.after[key])
    );
  }

  formatValue(value: unknown): string {
    if (value === undefined) return '—';
    if (value === null) return 'null';
    if (typeof value === 'object') return JSON.stringify(value);
    return String(value);
  }
}
