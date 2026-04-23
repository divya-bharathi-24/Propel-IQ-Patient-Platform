import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnDestroy,
  OnInit,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialogModule } from '@angular/material/dialog';

import { IntakeService } from '../services/intake.service';
import { LocalDraftService } from '../services/local-draft.service';
import { IntakeAutosaveService } from '../services/intake-autosave.service';
import { IntakeModeStore } from '../state/intake-mode.store';
import { IntakeFieldMap } from '../models/intake-edit-form.model';
import { AiIntakeChatComponent } from '../ai-intake-chat/ai-intake-chat.component';
import { ManualIntakeFormComponent } from '../manual-intake-form/manual-intake-form.component';

// ── Conflict resolution options ────────────────────────────────────────────────

export type ConflictChoice = 'server' | 'local' | 'merge';

// ── Component ──────────────────────────────────────────────────────────────────

/**
 * IntakePageComponent — orchestration layer for US_030.
 *
 * Responsibilities:
 *  - Owns the IntakeModeStore, IntakeAutosaveService, and LocalDraftService.
 *  - Detects and restores server / localStorage drafts on load (AC-3).
 *  - Drives AI ↔ Manual mode-switch transitions (AC-1, AC-2).
 *  - Delegates the 30-second autosave to IntakeAutosaveService.
 */
@Component({
  selector: 'app-intake-page',
  standalone: true,
  imports: [
    CommonModule,
    MatButtonModule,
    MatIconModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    MatDialogModule,
    AiIntakeChatComponent,
    ManualIntakeFormComponent,
  ],
  providers: [
    IntakeModeStore,
    IntakeAutosaveService,
    LocalDraftService,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './intake-page.component.html',
  styleUrl: './intake-page.component.scss',
})
export class IntakePageComponent implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild(ManualIntakeFormComponent)
  private manualForm?: ManualIntakeFormComponent;

  @ViewChild(AiIntakeChatComponent)
  private aiChat?: AiIntakeChatComponent;

  private readonly route = inject(ActivatedRoute);
  private readonly intakeService = inject(IntakeService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);

  readonly store = inject(IntakeModeStore);
  readonly autosaveService = inject(IntakeAutosaveService);
  readonly localDraftService = inject(LocalDraftService);

  /** Route param :appointmentId */
  readonly appointmentId = signal('');

  /**
   * Holds the context question returned by the resume endpoint.
   * Passed to AiIntakeChatComponent via [resumeQuestion] binding.
   */
  readonly resumeQuestion = signal<string | null>(null);

  /** Controls the localStorage-conflict resolution modal. */
  readonly showConflictModal = signal(false);
  private conflictLocal: IntakeFieldMap | null = null;
  private conflictServer: IntakeFieldMap | null = null;

  // ── Lifecycle ──────────────────────────────────────────────────────────────

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('appointmentId') ?? '';
    this.appointmentId.set(id);

    this.autosaveService.init(id);
    this.localDraftService.init(id, (local, server) =>
      this.handleSyncConflict(local, server),
    );

    this.loadDraftOnInit(id);
  }

  ngAfterViewInit(): void {
    // After rendering, wire form value changes into the autosave trigger.
    // ManualIntakeFormComponent emits on blur; AI fields update draftFields directly.
  }

  ngOnDestroy(): void {
    this.localDraftService.destroy();
  }

  // ── Draft restoration (AC-3) ───────────────────────────────────────────────

  private loadDraftOnInit(appointmentId: string): void {
    this.intakeService
      .getDraft(appointmentId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          if (response.exists && response.draftData) {
            // Map IntakeFormValue → IntakeFieldMap for the shared store.
            // The manual form has a superset shape; demographics / symptoms /
            // medications fields share the same names so the cast is safe for
            // the sections that overlap.
            const draft = response.draftData as unknown as IntakeFieldMap;
            this.store.patchDraftFields(draft);
            this.store.setHasDraft(true);
            this.snackBar.open('Resuming your saved intake', 'Dismiss', {
              duration: 5_000,
            });
          } else {
            // No server draft — fall back to localStorage.
            this.applyLocalDraftIfPresent(appointmentId);
          }
        },
        error: () => {
          // 404 or network error — check localStorage.
          this.applyLocalDraftIfPresent(appointmentId);
        },
      });
  }

  private applyLocalDraftIfPresent(appointmentId: string): void {
    const localEntry = this.localDraftService.load(appointmentId);
    if (localEntry) {
      this.store.patchDraftFields(localEntry.fields);
      this.store.setHasDraft(true);
      this.snackBar.open('Resuming your saved intake', 'Dismiss', {
        duration: 5_000,
      });
    }
  }

  // ── Mode-switch: AI → Manual (AC-1) ───────────────────────────────────────

  switchToManual(): void {
    const fields = this.store.draftFields();
    this.store.setMode('Manual');
    // Defer patch until ManualIntakeFormComponent is in the DOM.
    // Angular renders the child on the next CD cycle after mode changes.
    setTimeout(() => {
      if (fields && this.manualForm) {
        this.manualForm.patchValues(fields);
      }
    });
  }

  // ── Mode-switch: Manual → AI (AC-2) ───────────────────────────────────────

  switchToAi(): void {
    const appointmentId = this.appointmentId();
    const currentFields = this.store.draftFields();

    if (!currentFields) {
      // No fields collected yet — just switch mode directly.
      this.store.setMode('AI');
      return;
    }

    this.intakeService
      .resumeAiSession(appointmentId, currentFields)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ nextQuestion }) => {
          this.resumeQuestion.set(nextQuestion);
          this.store.setMode('AI');
        },
        error: () => {
          // Keep Manual mode on HTTP error — do not lose patient data.
          this.snackBar.open(
            'Could not switch to AI mode. Please try again.',
            'Dismiss',
            { duration: 5_000 },
          );
        },
      });
  }

  // ── Autosave trigger ───────────────────────────────────────────────────────

  /**
   * Called by child form components on any field mutation.
   * Updates the shared draftFields snapshot and resets the 30-second debounce.
   */
  onFieldsChanged(fields: IntakeFieldMap): void {
    this.store.patchDraftFields(fields);
    this.autosaveService.trigger(fields);
  }

  // ── LocalStorage conflict resolution ──────────────────────────────────────

  private handleSyncConflict(
    local: IntakeFieldMap,
    server: IntakeFieldMap,
  ): void {
    this.conflictLocal = local;
    this.conflictServer = server;
    this.showConflictModal.set(true);
  }

  onConflictResolved(choice: ConflictChoice): void {
    this.showConflictModal.set(false);

    if (choice === 'local' && this.conflictLocal) {
      this.store.patchDraftFields(this.conflictLocal);
      this.localDraftService.clear();
    } else if (choice === 'server' && this.conflictServer) {
      this.store.patchDraftFields(this.conflictServer);
      this.localDraftService.clear();
    }
    // 'merge' — leave both copies; user resolves manually in the form.

    this.conflictLocal = null;
    this.conflictServer = null;
  }
}
