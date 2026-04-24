import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
} from '@angular/core';
import {
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { DataConflictDto } from '../../../../../../core/services/patient-360-view.service';

export type ResolutionChoice = 'value1' | 'value2' | 'custom';

export interface ConflictResolutionEvent {
  conflictId: string;
  resolvedValue: string;
}

interface ResolutionForm {
  choice: FormControl<ResolutionChoice | null>;
  customValue: FormControl<string>;
}

/**
 * ConflictResolutionFormComponent — US_044 AC-3
 *
 * Reactive form with three mutually exclusive options:
 *  - Option A: "Use value from [sourceDoc1]" → resolvedValue = value1
 *  - Option B: "Use value from [sourceDoc2]" → resolvedValue = value2
 *  - Option C: "Enter custom value" → reveals text input; resolvedValue = customValue
 *
 * Submit button is disabled until a choice is selected and, for Option C,
 * the custom text is non-empty (AC-3 edge case).
 *
 * On submit emits `(resolved)` with `{ conflictId, resolvedValue }`.
 *
 * WCAG 2.2 AA:
 *  - Radio group has an aria-labelledby tied to the fieldset legend (1.3.1).
 *  - Submit is aria-disabled when form is invalid (4.1.2).
 */
@Component({
  selector: 'app-conflict-resolution-form',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatRadioModule,
  ],
  template: `
    <form [formGroup]="form" (ngSubmit)="onSubmit()" class="resolution-form">
      <fieldset class="radio-fieldset">
        <legend class="radio-legend" [id]="legendId">Choose authoritative value:</legend>

        <mat-radio-group
          formControlName="choice"
          class="radio-group"
          [attr.aria-labelledby]="legendId"
        >
          <mat-radio-button value="value1" class="radio-option">
            Use <strong>{{ conflict.value1 }}</strong>
            <span class="source-label"> (from {{ conflict.sourceDoc1 }})</span>
          </mat-radio-button>

          <mat-radio-button value="value2" class="radio-option">
            Use <strong>{{ conflict.value2 }}</strong>
            <span class="source-label"> (from {{ conflict.sourceDoc2 }})</span>
          </mat-radio-button>

          <mat-radio-button value="custom" class="radio-option">
            Enter custom value
          </mat-radio-button>
        </mat-radio-group>
      </fieldset>

      @if (form.controls.choice.value === 'custom') {
        <mat-form-field appearance="outline" class="custom-field">
          <mat-label>Custom value</mat-label>
          <input
            matInput
            formControlName="customValue"
            placeholder="Enter the correct value"
            [attr.aria-required]="true"
          />
          @if (form.controls.customValue.hasError('required') && form.controls.customValue.touched) {
            <mat-error>Custom value is required.</mat-error>
          }
        </mat-form-field>
      }

      <button
        mat-raised-button
        color="primary"
        type="submit"
        [disabled]="!canSubmit"
        [attr.aria-disabled]="!canSubmit"
        class="submit-btn"
      >
        Apply Resolution
      </button>
    </form>
  `,
  styles: [
    `
      .resolution-form {
        display: flex;
        flex-direction: column;
        gap: 12px;
        padding: 12px 0 4px;
      }

      .radio-fieldset {
        border: none;
        padding: 0;
        margin: 0;
      }

      .radio-legend {
        font-size: 0.8rem;
        font-weight: 600;
        color: #555;
        margin-bottom: 6px;
      }

      .radio-group {
        display: flex;
        flex-direction: column;
        gap: 6px;
      }

      .radio-option {
        font-size: 0.875rem;
      }

      .source-label {
        color: #666;
        font-size: 0.78rem;
      }

      .custom-field {
        width: 100%;
      }

      .submit-btn {
        align-self: flex-start;
        font-size: 0.8rem;
      }
    `,
  ],
})
export class ConflictResolutionFormComponent implements OnInit {
  @Input({ required: true }) conflict!: DataConflictDto;
  @Output() readonly resolved = new EventEmitter<ConflictResolutionEvent>();

  private readonly fb = inject(FormBuilder);

  protected readonly legendId = `legend-${Math.random().toString(36).slice(2, 9)}`;

  protected form!: FormGroup<ResolutionForm>;

  ngOnInit(): void {
    this.form = this.fb.group<ResolutionForm>({
      choice: new FormControl<ResolutionChoice | null>(null, Validators.required),
      customValue: new FormControl('', { nonNullable: true }),
    });
  }

  protected get canSubmit(): boolean {
    const choice = this.form.controls.choice.value;
    if (!choice) return false;
    if (choice === 'custom') {
      return this.form.controls.customValue.value.trim().length > 0;
    }
    return true;
  }

  protected onSubmit(): void {
    if (!this.canSubmit) return;

    const choice = this.form.controls.choice.value!;
    let resolvedValue: string;

    if (choice === 'value1') {
      resolvedValue = this.conflict.value1;
    } else if (choice === 'value2') {
      resolvedValue = this.conflict.value2;
    } else {
      resolvedValue = this.form.controls.customValue.value.trim();
    }

    this.resolved.emit({ conflictId: this.conflict.conflictId, resolvedValue });
  }
}
