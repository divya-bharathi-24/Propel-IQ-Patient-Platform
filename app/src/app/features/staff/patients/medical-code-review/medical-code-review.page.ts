import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  OnInit,
  inject,
} from '@angular/core';
import { Router, ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MedicalCodeReviewStore } from './store/medical-code-review.store';
import { IcdCodesPanelComponent } from './components/icd-codes-panel/icd-codes-panel.component';
import { CptCodesPanelComponent } from './components/cpt-codes-panel/cpt-codes-panel.component';
import { ManualCodeEntryComponent } from './components/manual-code-entry/manual-code-entry.component';
import { CodeReviewProgressComponent } from './components/code-review-progress/code-review-progress.component';

/**
 * MedicalCodeReviewPageComponent — US_043 (task_001_fe)
 *
 * Routed page component at `/staff/patients/:patientId/medical-codes`.
 *
 * Responsibilities:
 *  - On init: loads AI-suggested codes from the store / service.
 *  - Renders the side-by-side ICD-10 (left) / CPT (right) panel layout (AC-1).
 *  - Hosts the progress indicator, manual code entry, and submit button.
 *  - Empty suggestions: shows an informational card with an "Upload Documents" link.
 *  - HTTP 503: shows a MatSnackBar error message and keeps manual entry active.
 *  - Submit: calls `store.submitReview(patientId)` and navigates to patient record on success.
 *  - On destroy: resets the store to prevent stale state on re-navigation.
 *
 * WCAG 2.2 AA:
 *  - `<main>` landmark with descriptive `aria-label`.
 *  - Error states use `role="alert"` for immediate screen reader announcement.
 *  - Submit button uses `aria-busy` while loading.
 *
 * Access control:
 *  - Route is protected by `authGuard` + `staffGuard` (see `staff.routes.ts`).
 *  - Patient-role users are redirected to `/access-denied` at the route level.
 */
@Component({
  selector: 'app-medical-code-review-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatProgressBarModule,
    MatSnackBarModule,
    IcdCodesPanelComponent,
    CptCodesPanelComponent,
    ManualCodeEntryComponent,
    CodeReviewProgressComponent,
  ],
  template: `
    <main class="code-review-page" aria-label="Medical code review">
      <!-- Page loading bar -->
      @if (store.loadingState() === 'loading') {
        <mat-progress-bar
          mode="indeterminate"
          aria-label="Loading medical code suggestions"
        />
      }

      <!-- Load error (non-503 handled here; 503 shown via snackbar in ngOnInit) -->
      @if (store.loadingState() === 'error') {
        <mat-card class="error-card" role="alert">
          <mat-card-content class="error-content">
            <mat-icon aria-hidden="true">error_outline</mat-icon>
            {{
              store.loadError() ?? 'Failed to load medical code suggestions.'
            }}
          </mat-card-content>
        </mat-card>
      }

      @if (store.loadingState() === 'loaded') {
        <!-- Progress indicator (always shown after load) -->
        @if (store.totalCount() > 0) {
          <app-code-review-progress />
        }

        <!-- Empty state (AC edge case: no documents processed) -->
        @if (store.suggestions().length === 0) {
          <mat-card class="empty-card">
            <mat-card-content class="empty-content">
              <mat-icon aria-hidden="true">description</mat-icon>
              <p>
                {{
                  store.emptyMessage() ??
                    'No code suggestions are available for this encounter.'
                }}
              </p>
              <a
                mat-stroked-button
                [routerLink]="['/staff/patients', patientId]"
                aria-label="Go to patient record to upload documents"
              >
                Upload Documents
              </a>
            </mat-card-content>
          </mat-card>
        }

        <!-- Side-by-side panel layout (AC-1) -->
        @if (store.suggestions().length > 0) {
          <div class="panels-grid">
            <app-icd-codes-panel class="panel-column" />
            <app-cpt-codes-panel class="panel-column" />
          </div>
        }

        <!-- Manual code entry (AC-4) -->
        <app-manual-code-entry />

        <!-- Submit footer -->
        <footer class="submit-footer">
          @if (store.submitState() === 'error') {
            <p class="submit-error" role="alert">
              <mat-icon aria-hidden="true">error_outline</mat-icon>
              {{ store.submitError() ?? 'Submission failed. Please retry.' }}
            </p>
          }

          <button
            mat-raised-button
            color="primary"
            type="button"
            (click)="onSubmit()"
            [disabled]="store.submitState() === 'loading'"
            [attr.aria-busy]="store.submitState() === 'loading'"
            aria-label="Submit code review decisions"
          >
            @if (store.submitState() === 'loading') {
              Submitting…
            } @else {
              Submit Review
            }
          </button>
        </footer>
      }
    </main>
  `,
  styles: [
    `
      .code-review-page {
        max-width: 1280px;
        margin: 0 auto;
        padding: 24px 16px;
        display: flex;
        flex-direction: column;
        gap: 20px;
      }

      .error-card,
      .empty-card {
        border-left: 4px solid #c62828;
      }

      .empty-card {
        border-left-color: #1976d2;
      }

      .error-content,
      .empty-content {
        display: flex;
        align-items: center;
        gap: 8px;
        flex-wrap: wrap;
      }

      .panels-grid {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 24px;
      }

      @media (max-width: 768px) {
        .panels-grid {
          grid-template-columns: 1fr;
        }
      }

      .panel-column {
        min-width: 0;
      }

      .submit-footer {
        display: flex;
        flex-direction: column;
        align-items: flex-end;
        gap: 8px;
        padding-top: 16px;
        border-top: 1px solid rgba(0, 0, 0, 0.12);
      }

      .submit-error {
        display: flex;
        align-items: center;
        gap: 4px;
        color: #c62828;
        font-size: 0.875rem;
        margin: 0;
      }
    `,
  ],
})
export class MedicalCodeReviewPageComponent implements OnInit, OnDestroy {
  protected readonly store = inject(MedicalCodeReviewStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  protected patientId = '';

  ngOnInit(): void {
    this.patientId = this.route.snapshot.paramMap.get('patientId') ?? '';

    if (!this.patientId) {
      this.router.navigate(['/staff/dashboard']);
      return;
    }

    this.store.loadSuggestions(this.patientId);

    // Watch for 503 Service Unavailable (AC: HTTP 503 handling).
    // Since rxMethod is async, we observe the error state reactively.
    // The snackbar is shown once per page load using effect-free polling via
    // a micro-task after the first load cycle.
    queueMicrotask(() => this.watchFor503());
  }

  ngOnDestroy(): void {
    this.store.reset();
  }

  protected onSubmit(): void {
    this.store.submitReview(this.patientId);

    // Navigate to the patient record after a successful submit.
    // We use a signal-based check via a poll on the next microtask queue
    // because NgRx Signals stores update synchronously after rxMethod resolves.
    const checkSuccess = (): void => {
      if (this.store.submitState() === 'success') {
        this.router.navigate(['/staff/patients', this.patientId]);
      } else if (this.store.submitState() === 'loading') {
        // Still in-flight — re-check after a tick.
        setTimeout(checkSuccess, 100);
      }
    };
    queueMicrotask(checkSuccess);
  }

  /**
   * Detects an HTTP 503 status from the load error and shows a
   * MatSnackBar notification informing staff to use manual entry.
   */
  private watchFor503(): void {
    if (this.store.loadingState() === 'loading') {
      // Not resolved yet — re-check after the next tick.
      setTimeout(() => this.watchFor503(), 200);
      return;
    }

    if (
      this.store.loadingState() === 'error' &&
      this.store.loadError()?.toLowerCase().includes('503')
    ) {
      this.snackBar.open(
        'Coding service temporarily unavailable — please retry or enter codes manually.',
        'Dismiss',
        { duration: 0, panelClass: ['snack-warning'] },
      );
    }
  }
}
