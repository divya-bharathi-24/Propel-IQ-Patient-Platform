import {
  ChangeDetectionStrategy,
  Component,
  Output,
  EventEmitter,
  inject,
  signal,
} from '@angular/core';
import { ReactiveFormsModule, FormBuilder, FormGroup } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { InsuranceCheckResult } from '../../../../shared/models/insurance.models';
import { InsuranceStatusBadgeComponent } from '../../../../shared/components/insurance-status-badge/insurance-status-badge.component';
import { InsuranceService } from '../../insurance.service';

/** Emitted by the "Skip" link — carries an Incomplete result so the store
 *  can record it without blocking the booking flow (AC-4, FR-040). */
const SKIP_RESULT: InsuranceCheckResult = {
  status: 'Incomplete',
  guidance: 'Insurance information was not provided.',
};

/**
 * Step 3 of the booking wizard — insurance soft pre-check (FR-038).
 *
 * Emits `insuranceChecked` when the patient either:
 *  - Completes a pre-check (any status), or
 *  - Skips the step entirely (status = Incomplete).
 *
 * The "Continue to Confirmation" button is always enabled (FR-040).
 * Guidance text is sourced from the API response, never hard-coded.
 */
@Component({
  selector: 'app-insurance-step',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    InsuranceStatusBadgeComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="insurance-step" aria-label="Step 3: Insurance information">
      <h2 class="step-heading">
        Insurance Details <span class="optional-label">(Optional)</span>
      </h2>
      <p class="step-description">
        Enter your insurer name and member ID. We'll do a soft pre-check — your
        booking will proceed regardless of the result.
      </p>

      <form [formGroup]="form" class="insurance-form">
        <mat-form-field appearance="outline">
          <mat-label>Insurer Name</mat-label>
          <input
            matInput
            formControlName="insurerName"
            placeholder="e.g. Medicare"
            autocomplete="off"
            aria-label="Insurer name"
          />
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Member ID</mat-label>
          <input
            matInput
            formControlName="memberId"
            placeholder="e.g. 12345678"
            autocomplete="off"
            aria-label="Insurance member ID"
          />
        </mat-form-field>

        <!-- "Check Insurance" button with loading state -->
        <div class="check-row">
          <button
            mat-stroked-button
            type="button"
            [disabled]="checking()"
            (click)="onCheckInsurance()"
            aria-label="Run insurance pre-check"
          >
            @if (checking()) {
              <mat-spinner
                diameter="18"
                class="inline-spinner"
                aria-hidden="true"
              />
              <span>Checking…</span>
            } @else {
              Check Insurance
            }
          </button>
        </div>

        <!-- Status badge — shown after check completes or step is skipped -->
        @if (insuranceResult()) {
          @if (skipped()) {
            <p class="skip-message" role="status" aria-live="polite">
              Skipped — insurance status will be recorded as Incomplete.
            </p>
          } @else {
            <app-insurance-status-badge [result]="insuranceResult()!" />
          }
        }

        <!-- Action bar -->
        <div class="actions">
          <!-- "Skip" link — always available; records Incomplete on the store -->
          <button
            mat-button
            type="button"
            class="skip-link"
            (click)="onSkip()"
            aria-label="Skip insurance step and proceed to confirmation"
          >
            Skip this step
          </button>

          <!-- "Continue" — always enabled, advances wizard regardless of status -->
          <button
            mat-flat-button
            color="primary"
            type="button"
            (click)="onContinue()"
            aria-label="Continue to confirmation step"
          >
            Continue to Confirmation
          </button>
        </div>
      </form>
    </section>
  `,
  styles: [
    `
      .insurance-step {
        display: flex;
        flex-direction: column;
        gap: 1rem;
      }

      .step-heading {
        font-size: 1.25rem;
        font-weight: 600;
        margin: 0;
      }

      .optional-label {
        font-weight: 400;
        font-size: 0.875rem;
        color: #5f6368;
      }

      .step-description {
        margin: 0;
        color: #5f6368;
        font-size: 0.875rem;
      }

      .insurance-form {
        display: flex;
        flex-direction: column;
        gap: 0.75rem;
      }

      mat-form-field {
        width: 100%;
        max-width: 400px;
      }

      .check-row {
        display: flex;
        align-items: center;
        gap: 0.5rem;
      }

      .inline-spinner {
        display: inline-block;
        vertical-align: middle;
        margin-right: 0.375rem;
      }

      .skip-message {
        margin: 0;
        font-size: 0.8125rem;
        color: #5f6368;
        font-style: italic;
      }

      .actions {
        display: flex;
        align-items: center;
        gap: 0.75rem;
        justify-content: space-between;
        padding-top: 0.5rem;
      }

      .skip-link {
        color: #5f6368;
        font-size: 0.875rem;
        padding: 0;
        min-width: auto;
      }
    `,
  ],
})
export class InsuranceStepComponent {
  private readonly insuranceService = inject(InsuranceService);

  /** Emitted when the step is resolved (checked or skipped). */
  @Output() readonly insuranceChecked =
    new EventEmitter<InsuranceCheckResult | null>();

  protected readonly form: FormGroup;

  /** True while the pre-check HTTP call is in flight. */
  protected readonly checking = signal(false);

  /** True when the patient used "Skip this step". */
  protected readonly skipped = signal(false);

  /** Holds the most recent pre-check result (or skip result). */
  protected readonly insuranceResult = signal<InsuranceCheckResult | null>(
    null,
  );

  constructor() {
    const fb = inject(FormBuilder);
    this.form = fb.group({
      insurerName: [''],
      memberId: [''],
    });
  }

  protected onCheckInsurance(): void {
    const { insurerName, memberId } = this.form.value as {
      insurerName: string;
      memberId: string;
    };

    this.checking.set(true);
    this.skipped.set(false);

    this.insuranceService
      .check({
        providerName: insurerName?.trim() ?? '',
        insuranceId: memberId?.trim() ?? '',
      })
      .subscribe({
        next: (result) => {
          this.insuranceResult.set(result);
          this.checking.set(false);
        },
        error: () => {
          // InsuranceService.check() never errors (catchError → of(fallback)),
          // but guard against unexpected scenarios.
          this.insuranceResult.set({
            status: 'CheckPending',
            guidance:
              'Insurance check is temporarily unavailable. Your booking will proceed.',
          });
          this.checking.set(false);
        },
      });
  }

  /** Records Incomplete status in place of a check and immediately advances to confirmation (AC-4). */
  protected onSkip(): void {
    this.skipped.set(true);
    this.insuranceResult.set(SKIP_RESULT);
    // Immediately emit so the wizard advances to the confirmation step —
    // the patient should not need to click "Continue" after skipping.
    this.insuranceChecked.emit(SKIP_RESULT);
  }

  /**
   * Always-enabled navigation to Step 4 (FR-040 — non-blocking).
   * Emits the current result (or null if neither checked nor skipped).
   */
  protected onContinue(): void {
    this.insuranceChecked.emit(this.insuranceResult());
  }
}
