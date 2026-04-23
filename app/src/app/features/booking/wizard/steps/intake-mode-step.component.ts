import {
  ChangeDetectionStrategy,
  Component,
  inject,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatRadioModule } from '@angular/material/radio';
import { FormsModule } from '@angular/forms';
import { IntakeMode } from '../../booking.models';
import { BookingWizardStore } from '../booking-wizard.store';

@Component({
  selector: 'app-intake-mode-step',
  standalone: true,
  imports: [FormsModule, MatButtonModule, MatRadioModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="intake-mode-step" aria-label="Step 2: Select intake mode">
      <h2 class="step-heading">How would you like to complete your intake?</h2>

      <div
        role="radiogroup"
        aria-label="Intake mode selection"
        class="radio-group"
      >
        <label
          class="radio-card"
          [class.radio-card--selected]="selected() === 'AiAssisted'"
        >
          <mat-radio-button
            value="AiAssisted"
            [checked]="selected() === 'AiAssisted'"
            (change)="selected.set('AiAssisted')"
            aria-label="AI-Assisted intake mode"
          >
            <span class="radio-label">AI-Assisted</span>
          </mat-radio-button>
          <p class="radio-description">
            Our AI guides you through intake questions step-by-step.
          </p>
        </label>

        <label
          class="radio-card"
          [class.radio-card--selected]="selected() === 'Manual'"
        >
          <mat-radio-button
            value="Manual"
            [checked]="selected() === 'Manual'"
            (change)="selected.set('Manual')"
            aria-label="Manual intake mode"
          >
            <span class="radio-label">Manual</span>
          </mat-radio-button>
          <p class="radio-description">
            Complete the intake form at your own pace without AI guidance.
          </p>
        </label>
      </div>

      <div class="actions">
        <button
          mat-flat-button
          color="primary"
          [disabled]="!selected()"
          (click)="onContinue()"
          aria-label="Continue to insurance step"
        >
          Continue
        </button>
      </div>
    </section>
  `,
  styles: [
    `
      .intake-mode-step {
        display: flex;
        flex-direction: column;
        gap: 1.5rem;
      }

      .step-heading {
        font-size: 1.25rem;
        font-weight: 600;
        margin: 0;
      }

      .radio-group {
        display: flex;
        flex-direction: column;
        gap: 1rem;

        @media (min-width: 600px) {
          flex-direction: row;
        }
      }

      .radio-card {
        display: flex;
        flex-direction: column;
        gap: 0.5rem;
        padding: 1rem 1.25rem;
        border: 2px solid #e0e0e0;
        border-radius: 8px;
        cursor: pointer;
        flex: 1;
        transition: border-color 0.15s ease;

        &--selected {
          border-color: #1565c0;
          background-color: #f0f4ff;
        }
      }

      .radio-label {
        font-weight: 600;
        font-size: 1rem;
      }

      .radio-description {
        margin: 0;
        color: #5f6368;
        font-size: 0.875rem;
      }

      .actions {
        display: flex;
        justify-content: flex-end;
      }
    `,
  ],
})
export class IntakeModeStepComponent {
  protected readonly store = inject(BookingWizardStore);
  protected readonly selected = signal<IntakeMode | null>(null);

  protected onContinue(): void {
    const mode = this.selected();
    if (!mode) return;
    this.store.setIntakeMode(mode);
  }
}
