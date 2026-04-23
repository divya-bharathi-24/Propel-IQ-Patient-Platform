import {
  ChangeDetectionStrategy,
  Component,
  Input,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { RiskFlagStore } from '../../state/risk-flag.store';
import { RiskInterventionDto } from '../../models/risk-flag.models';

/**
 * Displays the "High-Risk" flag banner inside a staff appointment card.
 *
 * Rendered only when `appointment.noShowRisk.severity === 'High'` and the
 * appointment has at least one pending intervention.
 *
 * States:
 *   - **Flag Pending**     → banner + intervention list (default when pendingInterventions > 0)
 *   - **All Acknowledged** → success state ("All interventions acknowledged")
 *
 * Optimistic UX:
 *   - Accept / Dismiss removes the row immediately from the signal; a server
 *     error triggers a re-fetch rollback via `RiskFlagStore.getRefresh$()`.
 *
 * WCAG 2.2 AA:
 *   - `role="status"` on the banner root (AC-1 requirement).
 *   - `aria-label="High-risk appointment flag"` on banner root.
 *   - Per-button `aria-label="Accept: <label>"` / `"Dismiss: <label>"`.
 *   - Dismissal reason textarea: `maxlength="500"`, associated `<label>`.
 *
 * Route: accessible only via routes protected by `staffGuard` (role Staff | Admin).
 */
@Component({
  selector: 'app-high-risk-flag-banner',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (pendingInterventions().length > 0 || allAcknowledged()) {
      <div
        class="risk-banner"
        role="status"
        aria-label="High-risk appointment flag"
      >
        <!-- Banner header -->
        <div class="banner-header">
          <span class="banner-icon" aria-hidden="true">⚠</span>
          <span class="banner-title">High-Risk</span>
        </div>

        @if (allAcknowledged()) {
          <!-- Success state -->
          <p class="acknowledged-msg">All interventions acknowledged.</p>
        } @else {
          <!-- Intervention rows -->
          <ul class="intervention-list" aria-label="Recommended interventions">
            @for (
              intervention of pendingInterventions();
              track intervention.id
            ) {
              <li class="intervention-row">
                <span class="intervention-label">{{ intervention.label }}</span>

                <div class="intervention-actions">
                  <!-- Accept button -->
                  <button
                    class="action-btn accept-btn"
                    type="button"
                    [attr.aria-label]="'Accept: ' + intervention.label"
                    (click)="onAccept(intervention)"
                  >
                    Accept
                  </button>

                  <!-- Dismiss toggle -->
                  @if (dismissingId() === intervention.id) {
                    <div class="dismiss-panel">
                      <label
                        [attr.for]="'dismiss-reason-' + intervention.id"
                        class="dismiss-label"
                      >
                        Reason (optional)
                      </label>
                      <textarea
                        [id]="'dismiss-reason-' + intervention.id"
                        class="dismiss-textarea"
                        maxlength="500"
                        rows="2"
                        placeholder="Enter dismissal reason (optional)"
                        [value]="dismissReason()"
                        (input)="onReasonInput($event)"
                        aria-multiline="true"
                      ></textarea>
                      <div class="dismiss-confirm-actions">
                        <button
                          class="action-btn confirm-dismiss-btn"
                          type="button"
                          [attr.aria-label]="
                            'Confirm dismiss: ' + intervention.label
                          "
                          (click)="onConfirmDismiss(intervention)"
                        >
                          Confirm
                        </button>
                        <button
                          class="action-btn cancel-btn"
                          type="button"
                          aria-label="Cancel dismissal"
                          (click)="onCancelDismiss()"
                        >
                          Cancel
                        </button>
                      </div>
                    </div>
                  } @else {
                    <button
                      class="action-btn dismiss-btn"
                      type="button"
                      [attr.aria-label]="'Dismiss: ' + intervention.label"
                      (click)="onDismissToggle(intervention.id)"
                    >
                      Dismiss
                    </button>
                  }
                </div>
              </li>
            }
          </ul>
        }
      </div>
    }
  `,
  styles: [
    `
      /* ── Banner container ───────────────────── */
      .risk-banner {
        background-color: #ffebee;
        border: 1px solid #ef9a9a;
        border-left: 4px solid #c62828;
        border-radius: 6px;
        padding: 12px 14px;
        margin-top: 8px;
      }

      /* ── Header row ─────────────────────────── */
      .banner-header {
        display: flex;
        align-items: center;
        gap: 6px;
        margin-bottom: 8px;
      }

      .banner-icon {
        font-size: 1rem;
        color: #c62828;
      }

      .banner-title {
        font-size: 0.875rem;
        font-weight: 700;
        color: #b71c1c;
        text-transform: uppercase;
        letter-spacing: 0.04em;
      }

      /* ── Intervention list ──────────────────── */
      .intervention-list {
        list-style: none;
        margin: 0;
        padding: 0;
        display: flex;
        flex-direction: column;
        gap: 6px;
      }

      .intervention-row {
        display: flex;
        align-items: flex-start;
        justify-content: space-between;
        gap: 8px;
        flex-wrap: wrap;
      }

      .intervention-label {
        font-size: 0.8125rem;
        color: #424242;
        flex: 1 1 120px;
        line-height: 1.4;
      }

      /* ── Action buttons ─────────────────────── */
      .intervention-actions {
        display: flex;
        align-items: flex-start;
        gap: 6px;
        flex-wrap: wrap;
      }

      .action-btn {
        padding: 4px 12px;
        border-radius: 4px;
        font-size: 0.75rem;
        font-weight: 600;
        cursor: pointer;
        border: 1px solid transparent;
        transition:
          background-color 0.15s,
          border-color 0.15s;
      }

      .accept-btn {
        background-color: #1565c0;
        color: #fff;
      }

      .accept-btn:hover {
        background-color: #0d47a1;
      }

      .dismiss-btn {
        background-color: #fff;
        color: #616161;
        border-color: #bdbdbd;
      }

      .dismiss-btn:hover {
        background-color: #f5f5f5;
        border-color: #9e9e9e;
      }

      .confirm-dismiss-btn {
        background-color: #c62828;
        color: #fff;
      }

      .confirm-dismiss-btn:hover {
        background-color: #b71c1c;
      }

      .cancel-btn {
        background-color: #fff;
        color: #616161;
        border-color: #bdbdbd;
      }

      .cancel-btn:hover {
        background-color: #f5f5f5;
      }

      /* ── Dismiss panel ──────────────────────── */
      .dismiss-panel {
        display: flex;
        flex-direction: column;
        gap: 4px;
        width: 100%;
        margin-top: 4px;
      }

      .dismiss-label {
        font-size: 0.75rem;
        color: #616161;
        font-weight: 600;
      }

      .dismiss-textarea {
        width: 100%;
        resize: vertical;
        padding: 6px 8px;
        border: 1px solid #bdbdbd;
        border-radius: 4px;
        font-size: 0.8125rem;
        color: #212121;
        box-sizing: border-box;
      }

      .dismiss-textarea:focus {
        outline: 2px solid #1565c0;
        outline-offset: 1px;
        border-color: #1565c0;
      }

      .dismiss-confirm-actions {
        display: flex;
        gap: 6px;
      }

      /* ── Acknowledged state ─────────────────── */
      .acknowledged-msg {
        font-size: 0.8125rem;
        color: #2e7d32;
        margin: 0;
        font-weight: 600;
      }
    `,
  ],
})
export class HighRiskFlagBannerComponent implements OnInit {
  /** Appointment UUID — used to scope store lookups and service calls. */
  @Input({ required: true }) appointmentId!: string;

  protected readonly store = inject(RiskFlagStore);

  /**
   * ID of the intervention currently showing the dismissal reason panel.
   * Null means no dismiss panel is open.
   */
  protected readonly dismissingId = signal<string | null>(null);

  /** Current value of the dismissal reason textarea. */
  protected readonly dismissReason = signal<string>('');

  /** Derived list of still-pending interventions for this appointment. */
  protected readonly pendingInterventions = computed<RiskInterventionDto[]>(
    () => this.store.interventionsByAppointment()[this.appointmentId] ?? [],
  );

  /**
   * True once all interventions have been acknowledged (accepted or dismissed).
   * Drives the "All interventions acknowledged" success state.
   */
  protected readonly allAcknowledged = computed<boolean>(
    () =>
      this.store.interventionLoadingByAppointment()[this.appointmentId] ===
        'loaded' && this.pendingInterventions().length === 0,
  );

  ngOnInit(): void {
    // Load pending interventions for this appointment on first render.
    this.store.loadInterventions(this.appointmentId);

    // Wire the rollback refresh$ to re-load on optimistic update failure.
    this.store.getRefresh$().subscribe((appointmentId) => {
      if (appointmentId === this.appointmentId) {
        this.store.loadInterventions(appointmentId);
      }
    });
  }

  protected onAccept(intervention: RiskInterventionDto): void {
    this.store.acceptIntervention(intervention.id, this.appointmentId);
  }

  protected onDismissToggle(interventionId: string): void {
    this.dismissingId.set(interventionId);
    this.dismissReason.set('');
  }

  protected onReasonInput(event: Event): void {
    const textarea = event.target as HTMLTextAreaElement;
    this.dismissReason.set(textarea.value);
  }

  protected onConfirmDismiss(intervention: RiskInterventionDto): void {
    const reason = this.dismissReason().trim() || null;
    this.store.dismissIntervention(intervention.id, reason, this.appointmentId);
    this.dismissingId.set(null);
    this.dismissReason.set('');
  }

  protected onCancelDismiss(): void {
    this.dismissingId.set(null);
    this.dismissReason.set('');
  }
}
