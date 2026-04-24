import {
  ChangeDetectionStrategy,
  Component,
  inject,
  signal,
} from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MedicalCodeReviewStore } from '../../store/medical-code-review.store';
import { MedicalCodeService } from '../../services/medical-code.service';
import { CodeType } from '../../models/medical-code.models';

/**
 * ManualCodeEntryComponent — US_043 AC-4
 *
 * Reactive form that allows staff to manually enter and validate a medical code
 * before adding it to the review panel.
 *
 * Workflow:
 *  1. Staff enters a code and selects a code type (ICD10 | CPT).
 *  2. On blur from the code field, the component calls
 *     `POST /api/medical-codes/validate`.
 *  3. If valid — the validated code is dispatched to the store as a manual entry.
 *  4. If invalid — an inline `mat-error` shows the backend's error message.
 *
 * WCAG 2.2 AA:
 *  - All form controls have explicit `<mat-label>` associations.
 *  - Errors are announced via `mat-error` (which is in the accessibility tree).
 *  - Submit button is `disabled` while validation is in-flight.
 */
@Component({
  selector: 'app-manual-code-entry',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
  ],
  template: `
    <section
      class="manual-entry-section"
      aria-label="Manual code entry"
      role="region"
    >
      <h3 class="entry-title">
        <mat-icon aria-hidden="true">add_circle_outline</mat-icon>
        Add Code Manually
      </h3>

      <form
        [formGroup]="form"
        class="entry-form"
        (ngSubmit)="onSubmit()"
        novalidate
      >
        <!-- Code type selector -->
        <mat-form-field appearance="outline" class="type-field">
          <mat-label>Code Type</mat-label>
          <mat-select formControlName="codeType" aria-label="Code type">
            <mat-option value="ICD10">ICD-10</mat-option>
            <mat-option value="CPT">CPT</mat-option>
          </mat-select>
        </mat-form-field>

        <!-- Code input with blur-triggered validation -->
        <mat-form-field appearance="outline" class="code-field">
          <mat-label>Medical Code</mat-label>
          <input
            matInput
            formControlName="code"
            placeholder="e.g. E11.9 or 99213"
            aria-required="true"
            (blur)="onCodeBlur()"
            autocomplete="off"
          />

          @if (
            form.get('code')?.hasError('required') && form.get('code')?.touched
          ) {
            <mat-error>Code is required.</mat-error>
          }
          @if (validationError()) {
            <mat-error>{{ validationError() }}</mat-error>
          }
        </mat-form-field>

        <!-- Validated description (read-only confirmation) -->
        @if (validatedDescription()) {
          <p class="validated-description" role="status" aria-live="polite">
            <mat-icon aria-hidden="true" class="valid-icon"
              >check_circle</mat-icon
            >
            {{ validatedDescription() }}
          </p>
        }

        <button
          mat-raised-button
          color="accent"
          type="submit"
          [disabled]="form.invalid || validating() || !validatedDescription()"
          aria-label="Add validated code to review panel"
        >
          @if (validating()) {
            Validating…
          } @else {
            Add to Panel
          }
        </button>
      </form>
    </section>
  `,
  styles: [
    `
      .manual-entry-section {
        padding: 16px;
        border: 1px solid rgba(0, 0, 0, 0.12);
        border-radius: 4px;
        background: #fafafa;
      }

      .entry-title {
        display: flex;
        align-items: center;
        gap: 8px;
        margin: 0 0 16px;
        font-size: 1rem;
        font-weight: 600;
      }

      .entry-form {
        display: flex;
        flex-wrap: wrap;
        align-items: flex-start;
        gap: 12px;
      }

      .type-field {
        flex: 0 0 140px;
      }

      .code-field {
        flex: 1 1 200px;
      }

      .validated-description {
        display: flex;
        align-items: center;
        gap: 4px;
        margin: 0;
        font-size: 0.875rem;
        color: #2e7d32;
        flex: 1 1 100%;
      }

      .valid-icon {
        font-size: 18px;
        height: 18px;
        width: 18px;
      }
    `,
  ],
})
export class ManualCodeEntryComponent {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(MedicalCodeService);
  private readonly store = inject(MedicalCodeReviewStore);

  protected readonly form = this.fb.nonNullable.group({
    codeType: ['ICD10' as CodeType, Validators.required],
    code: ['', [Validators.required, Validators.minLength(2)]],
  });

  /** True while the validate API call is in-flight. */
  protected validating = signal(false);
  /** Description returned by the validate API for the current code. */
  protected validatedDescription = signal<string | null>(null);
  /** Error message returned by the validate API. */
  protected validationError = signal<string | null>(null);

  /** Called on blur from the code input field (AC-4). */
  protected onCodeBlur(): void {
    const code = this.form.getRawValue().code.trim();
    if (!code) return;

    this.validationError.set(null);
    this.validatedDescription.set(null);
    this.validating.set(true);

    const codeType = this.form.getRawValue().codeType;

    this.service.validateCode({ code, codeType }).subscribe({
      next: (result) => {
        this.validating.set(false);
        if (result.valid && result.description) {
          this.validatedDescription.set(result.description);
        } else {
          this.validationError.set(
            result.errorMessage ??
              'Code is not recognised. Please check and retry.',
          );
        }
      },
      error: () => {
        this.validating.set(false);
        this.validationError.set(
          'Validation service unavailable. Please check the code manually.',
        );
      },
    });
  }

  /** Adds the validated code to the store and resets the form (AC-4). */
  protected onSubmit(): void {
    const description = this.validatedDescription();
    if (this.form.invalid || !description) return;

    const { code, codeType } = this.form.getRawValue();

    this.store.addManualCode({
      code: code.trim(),
      codeType,
      description,
    });

    this.form.reset({ codeType: 'ICD10', code: '' });
    this.validatedDescription.set(null);
    this.validationError.set(null);
  }
}
