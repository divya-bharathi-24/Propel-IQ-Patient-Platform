import {
  Component,
  EventEmitter,
  Input,
  Output,
  OnChanges,
  SimpleChanges,
} from '@angular/core';
import { ReactiveFormsModule, FormControl, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { inject } from '@angular/core';
import { AiOperationalMetricsSummary } from '../../../models/admin.models';

/** Phase-1 hardcoded list of allowed model versions. */
const ALLOWED_VERSIONS = [
  'gpt-4o',
  'gpt-4o-mini',
  'gpt-4-turbo',
  'gpt-4',
  'gpt-3.5-turbo',
];

/**
 * Displays the current active model version and provides an inline
 * reactive form to update it (US_050 / AC-3 operator UI).
 *
 * Emits `modelVersionChange` with the selected version string on submit.
 * Shows a snackbar confirming the change is "effective within 5 minutes".
 */
@Component({
  selector: 'app-model-version-panel',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatSelectModule,
    MatButtonModule,
    MatSnackBarModule,
  ],
  template: `
    <mat-card>
      <mat-card-header>
        <mat-card-title>Model Version &amp; Configuration</mat-card-title>
        <mat-card-subtitle>Current active model</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        @if (metrics) {
          <p class="current-version">
            <strong>Active version:</strong>
            <code>{{ metrics.currentModelVersion }}</code>
          </p>
          <form
            (ngSubmit)="onSubmit()"
            class="version-form"
            aria-label="Change model version"
          >
            <mat-form-field appearance="outline" class="version-field">
              <mat-label>Select new version</mat-label>
              <mat-select [formControl]="versionControl" aria-required="true">
                @for (v of allowedVersions; track v) {
                  <mat-option [value]="v">{{ v }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
            <button
              mat-flat-button
              color="primary"
              type="submit"
              [disabled]="
                versionControl.invalid ||
                versionControl.value === metrics.currentModelVersion
              "
            >
              Apply Version
            </button>
          </form>
        } @else {
          <p class="muted">No configuration data available</p>
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: [
    `
      mat-card-content {
        padding-top: 16px;
      }

      .current-version {
        margin: 0 0 16px;
        font-size: 0.9375rem;

        code {
          background: var(--mat-sys-surface-variant, #f5f5f5);
          border-radius: 4px;
          padding: 2px 6px;
          margin-left: 6px;
          font-family: monospace;
        }
      }

      .version-form {
        display: flex;
        align-items: center;
        gap: 12px;
        flex-wrap: wrap;
      }

      .version-field {
        flex: 1;
        min-width: 200px;
      }

      .muted {
        color: var(--mat-sys-on-surface-variant, #555);
        font-style: italic;
      }
    `,
  ],
})
export class ModelVersionPanelComponent implements OnChanges {
  @Input() metrics: AiOperationalMetricsSummary | null = null;
  @Output() modelVersionChange = new EventEmitter<string>();

  protected readonly allowedVersions = ALLOWED_VERSIONS;
  protected readonly versionControl = new FormControl<string>('', {
    nonNullable: true,
    validators: [Validators.required],
  });

  private readonly snackBar = inject(MatSnackBar);

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['metrics'] && this.metrics?.currentModelVersion) {
      this.versionControl.setValue(this.metrics.currentModelVersion);
    }
  }

  onSubmit(): void {
    if (this.versionControl.invalid) return;
    this.modelVersionChange.emit(this.versionControl.value);
    this.snackBar.open(
      'Model version updated — effective within 5 minutes.',
      'Dismiss',
      { duration: 5000, panelClass: 'snack-success' },
    );
  }
}
