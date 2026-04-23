import { Injectable, OnDestroy, inject } from '@angular/core';
import { Subject, Subscription } from 'rxjs';
import { debounceTime, switchMap } from 'rxjs/operators';
import { IntakeFieldMap } from '../models/intake-edit-form.model';
import { IntakeService } from './intake.service';
import { LocalDraftService } from './local-draft.service';
import { IntakeModeStore } from '../state/intake-mode.store';

// ── Service ────────────────────────────────────────────────────────────────────

/**
 * IntakeAutosaveService — drives the 30-second field-change autosave timer (AC-4).
 *
 * Must be provided in IntakePageComponent's providers array (not root) so the
 * Subject and Subscription are destroyed with the component, avoiding leaks.
 *
 * Usage:
 *   1. Call `init(appointmentId)` in ngOnInit.
 *   2. Call `trigger(fields)` on any field mutation (debounce resets each call).
 *   3. The service handles status updates via IntakeModeStore and falls back to
 *      LocalDraftService on network error.
 */
@Injectable()
export class IntakeAutosaveService implements OnDestroy {
  private readonly intakeService = inject(IntakeService);
  private readonly localDraft = inject(LocalDraftService);
  private readonly store = inject(IntakeModeStore);

  private readonly fieldChange$ = new Subject<IntakeFieldMap>();
  private subscription: Subscription | null = null;

  /** Active appointment ID; set once by init(). */
  private appointmentId = '';

  // ── Lifecycle ──────────────────────────────────────────────────────────────

  /**
   * Wires the debounced autosave pipeline.
   * @param appointmentId  The appointment for which drafts are saved.
   */
  init(appointmentId: string): void {
    this.appointmentId = appointmentId;

    // debounceTime(30_000): clock resets on each field change; fires 30 s after
    // the last mutation — satisfying "within 30 seconds of any modification".
    this.subscription = this.fieldChange$
      .pipe(
        debounceTime(30_000),
        switchMap((fields) => {
          this.store.setAutosaveStatus('Saving');
          return this.intakeService.saveOrchestratedDraft(appointmentId, fields);
        }),
      )
      .subscribe({
        next: () => {
          this.store.setAutosaveStatus('Saved');
          // Reset indicator to Idle after 3 s for UX clarity.
          setTimeout(() => {
            if (this.store.autosaveStatus() === 'Saved') {
              this.store.setAutosaveStatus('Idle');
            }
          }, 3_000);
        },
        error: () => {
          this.store.setAutosaveStatus('Error');
          // Persist locally as fallback when network is unavailable.
          const currentFields = this.store.draftFields();
          if (currentFields) {
            this.localDraft.save(currentFields, Date.now());
          }
        },
      });
  }

  /**
   * Signals a field mutation. Resets the 30-second debounce window.
   * @param fields  Current snapshot of all intake sections.
   */
  trigger(fields: IntakeFieldMap): void {
    this.fieldChange$.next(fields);
  }

  ngOnDestroy(): void {
    this.subscription?.unsubscribe();
    this.fieldChange$.complete();
  }
}
