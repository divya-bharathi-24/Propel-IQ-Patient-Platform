import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
} from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { DatePipe } from '@angular/common';
import { Patient360ViewStore } from './patient-360-view.store';
import { ClinicalConflictStore } from './store/clinical-conflict.store';
import { ClinicalSectionComponent } from './clinical-section/clinical-section.component';
import {
  UnresolvedCriticalBlockerModalComponent,
  UnresolvedCriticalBlockerModalData,
} from './components/unresolved-critical-modal/unresolved-critical-modal.component';
import {
  ClinicalSectionDto,
  DataConflictDto,
  DocumentStatusDto,
  SectionType,
} from '../../../../core/services/patient-360-view.service';

/** Ordered list of section types to render — matches AC-1. */
const SECTION_ORDER: SectionType[] = [
  'Vitals',
  'Medications',
  'Diagnoses',
  'Allergies',
  'Immunizations',
  'SurgicalHistory',
];

/**
 * 360-degree patient view page (US_041, task_001).
 *
 * Renders aggregated clinical data sections, a "Verify Profile" action,
 * conflict warnings, and document failure badges.
 *
 * AC-1: Six expandable sections for Vitals, Medications, Diagnoses, Allergies,
 *       Immunizations, Surgical History.
 * AC-2: Confidence badges and low-confidence row flags on each data row.
 * AC-3: "Verify Profile" button shows success confirmation with timestamp + staff name.
 * AC-4: Button is blocked (aria-disabled) with an inline conflict list when
 *       unresolved Critical conflicts exist.
 *
 * WCAG 2.2 AA:
 *  - Conflict list announced via aria-live="assertive" (4.1.3 Status Messages).
 *  - Verify button uses aria-disabled + aria-describedby when blocked (4.1.2 Name/Role/Value).
 *  - Progress bar has aria-label.
 *  - All interactive elements have visible focus indicators.
 */
@Component({
  selector: 'app-patient-360-view',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatDialogModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatIconModule,
    MatChipsModule,
    ClinicalSectionComponent,
  ],
  template: `
    <main class="view-360-page" aria-label="360-degree patient view">
      <!-- Loading bar -->
      @if (store.loadingState() === 'loading') {
        <mat-progress-bar
          mode="indeterminate"
          aria-label="Loading 360-degree patient view"
        />
      }

      <!-- Load error -->
      @if (store.loadingState() === 'error') {
        <mat-card class="error-card" role="alert">
          <mat-card-content>
            <mat-icon aria-hidden="true">error_outline</mat-icon>
            {{ store.loadError() ?? 'Failed to load patient data.' }}
          </mat-card-content>
        </mat-card>
      }

      @if (store.loadingState() === 'loaded' && store.view360(); as view) {
        <!-- >10 documents banner (edge case) -->
        @if (view.documents.length > 10) {
          <div class="banner-info" role="status" aria-live="polite">
            <mat-icon aria-hidden="true">info</mat-icon>
            Showing data from all {{ view.documents.length }} documents —
            2-minute SLA applies to ≤10 documents only.
          </div>
        }

        <!-- Document failure badges -->
        @if (failedDocuments(view.documents).length > 0) {
          <mat-card class="doc-failures-card">
            <mat-card-header>
              <mat-card-title>
                <mat-icon aria-hidden="true">report_problem</mat-icon>
                Document Processing Issues
              </mat-card-title>
            </mat-card-header>
            <mat-card-content>
              @for (
                doc of failedDocuments(view.documents);
                track doc.documentId
              ) {
                <div class="doc-failure-row">
                  <mat-chip
                    class="failed-chip"
                    aria-label="{{ doc.documentName }}: Processing Failed"
                  >
                    Processing Failed
                  </mat-chip>
                  <span class="doc-name">{{ doc.documentName }}</span>
                  <button
                    mat-stroked-button
                    type="button"
                    (click)="onRetryDocument(view.patientId, doc.documentId)"
                    [attr.aria-label]="'Retry extraction for ' + doc.documentName"
                  >
                    <mat-icon aria-hidden="true">refresh</mat-icon>
                    Retry
                  </button>
                </div>
              }
            </mat-card-content>
          </mat-card>
        }

        <!-- Verification success -->
        @if (
          store.verifyState() === 'success' && store.verifyResult();
          as result
        ) {
          <mat-card
            class="verify-success-card"
            role="status"
            aria-live="polite"
          >
            <mat-card-content>
              <mat-icon aria-hidden="true" class="success-icon"
                >verified</mat-icon
              >
              Profile verified on {{ result.verifiedAt | date: 'medium' }} by
              {{ result.verifiedByStaffName }}.
            </mat-card-content>
          </mat-card>
        }

        <!-- Conflict warning block (AC-4) -->
        @if (conflictStore.unresolvedCriticalCount() > 0) {
          <mat-card
            id="conflict-warning-block"
            class="conflict-warning-card"
            role="alert"
            aria-live="assertive"
          >
            <mat-card-header>
              <mat-card-title>
                <mat-icon aria-hidden="true">block</mat-icon>
                Verification Blocked — {{ conflictStore.unresolvedCriticalCount() }} Unresolved Critical
                {{ conflictStore.unresolvedCriticalCount() === 1 ? 'Conflict' : 'Conflicts' }}
              </mat-card-title>
            </mat-card-header>
            <mat-card-content>
              <p class="conflict-hint">
                Resolve all Critical conflicts in the sections below before verifying.
              </p>
            </mat-card-content>
          </mat-card>
        }

        <!-- Clinical sections (AC-1) -->
        <section class="sections-container" aria-label="Clinical data sections">
          @for (
            section of orderedSections(view.sections);
            track section.sectionType
          ) {
            <app-clinical-section
              [section]="section"
              [conflicts]="conflictStore.conflicts()"
            />
          }
        </section>

        <!-- Verify Profile button (AC-3, AC-4) -->
        @if (store.verifyState() !== 'success') {
          <div class="verify-action-row">
            <button
              mat-raised-button
              color="primary"
              type="button"
              [disabled]="store.verifyState() === 'loading'"
              [attr.aria-describedby]="
                conflictStore.unresolvedCriticalCount() > 0
                  ? 'conflict-warning-block'
                  : null
              "
              (click)="onVerifyProfile(view.patientId)"
            >
              @if (store.verifyState() === 'loading') {
                <mat-spinner diameter="18" strokeWidth="2" />
                Verifying…
              } @else {
                Verify Profile
              }
            </button>

            @if (store.verifyState() === 'error') {
              <span class="verify-error" role="alert" aria-live="assertive">
                <mat-icon aria-hidden="true">error</mat-icon>
                {{ store.verifyError() }}
              </span>
            }
          </div>
        }
      }
    </main>
  `,
  styles: [
    `
      .view-360-page {
        max-width: 1100px;
        margin: 0 auto;
        padding: 24px 16px;
        display: flex;
        flex-direction: column;
        gap: 16px;
      }

      .banner-info {
        display: flex;
        align-items: center;
        gap: 8px;
        padding: 12px 16px;
        background-color: #e3f2fd;
        border-radius: 6px;
        font-size: 0.875rem;
        color: #0d47a1;
      }

      .doc-failures-card mat-card-header {
        color: #b71c1c;
      }

      .doc-failure-row {
        display: flex;
        align-items: center;
        gap: 12px;
        padding: 8px 0;
      }

      .failed-chip {
        background-color: #b71c1c !important;
        color: #fff !important;
        font-size: 0.72rem;
        font-weight: 700;
      }

      .doc-name {
        flex: 1;
        font-size: 0.875rem;
      }

      .verify-success-card {
        background-color: #e8f5e9;
        border-left: 4px solid #2e7d32;
      }

      .success-icon {
        color: #2e7d32;
        vertical-align: middle;
        margin-right: 6px;
      }

      .conflict-warning-card {
        background-color: #fff3e0;
        border-left: 4px solid #e65100;
      }

      .conflict-hint {
        font-size: 0.875rem;
        color: #6d4c41;
        margin: 0;
      }

      .conflict-list {
        margin: 0;
        padding-left: 20px;
        font-size: 0.875rem;
      }

      .conflict-list li {
        margin-bottom: 4px;
      }

      .sections-container {
        display: flex;
        flex-direction: column;
        gap: 0;
      }

      .verify-action-row {
        display: flex;
        align-items: center;
        gap: 16px;
        padding: 8px 0;
      }

      .verify-error {
        display: flex;
        align-items: center;
        gap: 4px;
        color: #b71c1c;
        font-size: 0.875rem;
      }

      .error-card {
        border-left: 4px solid #b71c1c;
        color: #b71c1c;
      }

      .error-card mat-card-content {
        display: flex;
        align-items: center;
        gap: 8px;
      }
    `,
  ],
})
export class Patient360ViewComponent implements OnInit {
  protected readonly store = inject(Patient360ViewStore);
  protected readonly conflictStore = inject(ClinicalConflictStore);
  private readonly route = inject(ActivatedRoute);
  private readonly dialog = inject(MatDialog);

  private patientId = '';

  ngOnInit(): void {
    this.patientId = this.route.snapshot.paramMap.get('patientId') ?? '';
    if (this.patientId) {
      this.store.load360View(this.patientId);
    }
  }

  protected orderedSections(
    sections: ClinicalSectionDto[],
  ): ClinicalSectionDto[] {
    const sectionMap = new Map(sections.map((s) => [s.sectionType, s]));
    return SECTION_ORDER.map((type) => sectionMap.get(type)).filter(
      (s): s is ClinicalSectionDto => s !== undefined,
    );
  }

  protected failedDocuments(docs: DocumentStatusDto[]): DocumentStatusDto[] {
    return docs.filter((d) => d.status === 'Failed');
  }

  protected onVerifyProfile(patientId: string): void {
    const unresolvedCritical = this.conflictStore
      .conflicts()
      .filter((c) => c.severity === 'Critical' && c.resolutionStatus === 'Unresolved');

    if (unresolvedCritical.length > 0) {
      // AC-4: Block verify and open modal listing unresolved Critical conflicts
      this.dialog.open<
        UnresolvedCriticalBlockerModalComponent,
        UnresolvedCriticalBlockerModalData
      >(UnresolvedCriticalBlockerModalComponent, {
        data: { conflicts: unresolvedCritical },
        width: '560px',
        disableClose: false,
        ariaLabelledBy: 'blocker-dialog-title',
      });
      return;
    }

    this.store.verifyProfile(patientId);
  }

  protected onRetryDocument(patientId: string, documentId: string): void {
    this.store.retryDocument({ patientId, documentId });
  }
}
