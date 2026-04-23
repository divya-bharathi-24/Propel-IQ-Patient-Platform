import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { RiskFlagStore } from '../../state/risk-flag.store';
import { RequiresAttentionItemDto } from '../../models/risk-flag.models';

/**
 * Surfaces unacknowledged High-risk appointments at the top of the Staff
 * dashboard in a "Requires Attention" section (AC-4 of US_032).
 *
 * On init, fetches items via `RiskFlagStore.loadRequiresAttention()`.
 * Results are sorted ascending by `appointmentTime` (earliest first) — the
 * sort is applied in the store to keep this component pure display logic.
 *
 * States:
 *   - **Loading**   → skeleton rows
 *   - **Empty**     → "No appointments require attention" message
 *   - **Populated** → list of items with patient name, time, and a navigation link
 *   - **Error**     → error message
 *
 * WCAG 2.2 AA:
 *   - `aria-live="polite"` on the count announcement span.
 *   - Section header has `role="heading"` via semantic `<h2>`.
 *   - Each link has a descriptive `aria-label`.
 *
 * Route: rendered inside `/staff/dashboard` — protected by `staffGuard` (role Staff | Admin).
 */
@Component({
  selector: 'app-requires-attention-section',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <section
      class="requires-attention"
      aria-labelledby="requires-attention-heading"
    >
      <div class="section-header">
        <h2 id="requires-attention-heading" class="section-title">
          ⚠ Requires Attention
        </h2>
        @if (store.requiresAttentionLoadingState() === 'loaded') {
          <span
            class="count-badge"
            aria-live="polite"
            [attr.aria-label]="
              store.requiresAttentionCount() +
              ' unacknowledged high-risk appointments'
            "
          >
            {{ store.requiresAttentionCount() }}
          </span>
        }
      </div>

      @if (store.requiresAttentionLoadingState() === 'loading') {
        <!-- Loading skeleton -->
        <div
          class="skeleton-list"
          role="status"
          aria-label="Loading high-risk appointments…"
        >
          @for (n of skeletonRows; track n) {
            <div class="skeleton-row">
              <div class="skeleton-cell skeleton-md"></div>
              <div class="skeleton-cell skeleton-sm"></div>
              <div class="skeleton-cell skeleton-xs"></div>
            </div>
          }
        </div>
      } @else if (store.requiresAttentionLoadingState() === 'error') {
        <p class="error-msg" role="alert">
          {{ store.requiresAttentionError() }}
        </p>
      } @else {
        <ul
          class="attention-list"
          aria-label="High-risk appointments requiring attention"
        >
          @for (
            item of store.requiresAttentionItems();
            track item.appointmentId
          ) {
            <li class="attention-item">
              <div class="item-info">
                <span class="patient-name">{{ item.patientName }}</span>
                <span class="appointment-time">{{
                  formatTime(item.appointmentTime)
                }}</span>
                <span class="pending-count">
                  {{ item.pendingCount }} pending
                  {{
                    item.pendingCount === 1 ? 'intervention' : 'interventions'
                  }}
                </span>
              </div>
              <a
                class="view-link"
                [routerLink]="['/staff/appointments']"
                [queryParams]="{ appointmentId: item.appointmentId }"
                [attr.aria-label]="
                  'View appointment for ' +
                  item.patientName +
                  ' at ' +
                  formatTime(item.appointmentTime)
                "
              >
                View
              </a>
            </li>
          } @empty {
            <li class="empty-state">No appointments require attention.</li>
          }
        </ul>
      }
    </section>
  `,
  styles: [
    `
      /* ── Section wrapper ────────────────────── */
      .requires-attention {
        background-color: #fff8e1;
        border: 1px solid #ffe082;
        border-left: 4px solid #f9a825;
        border-radius: 8px;
        padding: 16px 20px;
        margin-bottom: 24px;
      }

      /* ── Header ─────────────────────────────── */
      .section-header {
        display: flex;
        align-items: center;
        gap: 10px;
        margin-bottom: 12px;
      }

      .section-title {
        font-size: 1rem;
        font-weight: 700;
        color: #e65100;
        margin: 0;
      }

      .count-badge {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        min-width: 22px;
        height: 22px;
        padding: 0 6px;
        border-radius: 11px;
        background-color: #c62828;
        color: #fff;
        font-size: 0.75rem;
        font-weight: 700;
      }

      /* ── Attention list ─────────────────────── */
      .attention-list {
        list-style: none;
        margin: 0;
        padding: 0;
        display: flex;
        flex-direction: column;
        gap: 8px;
      }

      .attention-item {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 12px;
        background-color: #fff;
        border: 1px solid #ffe082;
        border-radius: 6px;
        padding: 10px 14px;
        flex-wrap: wrap;
      }

      .item-info {
        display: flex;
        flex-direction: column;
        gap: 2px;
        flex: 1 1 160px;
      }

      .patient-name {
        font-size: 0.875rem;
        font-weight: 600;
        color: #212121;
      }

      .appointment-time {
        font-size: 0.75rem;
        color: #616161;
        font-variant-numeric: tabular-nums;
      }

      .pending-count {
        font-size: 0.75rem;
        color: #c62828;
        font-weight: 600;
      }

      /* ── Navigation link ────────────────────── */
      .view-link {
        display: inline-block;
        padding: 4px 14px;
        border-radius: 4px;
        background-color: #1565c0;
        color: #fff;
        font-size: 0.75rem;
        font-weight: 600;
        text-decoration: none;
        transition: background-color 0.15s;
        white-space: nowrap;
      }

      .view-link:hover {
        background-color: #0d47a1;
      }

      .view-link:focus-visible {
        outline: 2px solid #1565c0;
        outline-offset: 2px;
      }

      /* ── Empty state ────────────────────────── */
      .empty-state {
        font-size: 0.875rem;
        color: #757575;
        padding: 8px 0;
      }

      /* ── Error ──────────────────────────────── */
      .error-msg {
        font-size: 0.875rem;
        color: #b71c1c;
        background-color: #fce4ec;
        border-radius: 4px;
        padding: 8px 12px;
        margin: 0;
      }

      /* ── Skeleton loader ────────────────────── */
      .skeleton-list {
        display: flex;
        flex-direction: column;
        gap: 8px;
      }

      .skeleton-row {
        display: flex;
        align-items: center;
        gap: 12px;
        background-color: #fff;
        border: 1px solid #ffe082;
        border-radius: 6px;
        padding: 12px 14px;
        animation: skeleton-pulse 1.4s ease-in-out infinite;
      }

      .skeleton-cell {
        height: 14px;
        border-radius: 4px;
        background-color: #e0e0e0;
      }

      .skeleton-xs {
        width: 48px;
      }
      .skeleton-sm {
        width: 80px;
      }
      .skeleton-md {
        width: 140px;
      }

      @keyframes skeleton-pulse {
        0%,
        100% {
          opacity: 1;
        }
        50% {
          opacity: 0.45;
        }
      }
    `,
  ],
})
export class RequiresAttentionSectionComponent implements OnInit {
  protected readonly store = inject(RiskFlagStore);

  protected readonly skeletonRows = [1, 2, 3];

  ngOnInit(): void {
    this.store.loadRequiresAttention(undefined);
  }

  protected formatTime(isoUtc: string): string {
    return new Date(isoUtc).toLocaleString(undefined, {
      dateStyle: 'short',
      timeStyle: 'short',
    });
  }

  // Type helper for template access
  protected asItem(item: RequiresAttentionItemDto): RequiresAttentionItemDto {
    return item;
  }
}
