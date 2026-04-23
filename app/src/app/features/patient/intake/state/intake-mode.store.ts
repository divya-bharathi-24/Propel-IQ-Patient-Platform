import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { IntakeFieldMap } from '../models/intake-edit-form.model';

// ── Types ──────────────────────────────────────────────────────────────────────

export type IntakeMode = 'AI' | 'Manual';
export type AutosaveStatusSignal = 'Idle' | 'Saving' | 'Saved' | 'Error';

// ── State ──────────────────────────────────────────────────────────────────────

export interface IntakeModeState {
  /** Active intake interaction mode. */
  mode: IntakeMode;
  /** Single source-of-truth for all four intake data sections. */
  draftFields: IntakeFieldMap | null;
  /** Reflects the current autosave network call status. */
  autosaveStatus: AutosaveStatusSignal;
  /** True when a server or localStorage draft was detected on page load. */
  hasDraft: boolean;
  /**
   * Holds the AI opening question injected during Manual → AI resume.
   * Cleared after AiIntakeChatComponent consumes it in initWithContext().
   */
  resumeQuestion: string | null;
}

const initialState: IntakeModeState = {
  mode: 'AI',
  draftFields: null,
  autosaveStatus: 'Idle',
  hasDraft: false,
  resumeQuestion: null,
};

// ── Store ──────────────────────────────────────────────────────────────────────

/**
 * IntakeModeStore — orchestration-level signal store for US_030.
 * Scoped to IntakePageComponent (provided in providers array, not root)
 * so the store lifetime is tied to the intake route.
 */
export const IntakeModeStore = signalStore(
  withState<IntakeModeState>(initialState),
  withMethods((store) => ({
    setMode(mode: IntakeMode): void {
      patchState(store, { mode });
    },

    patchDraftFields(fields: IntakeFieldMap): void {
      patchState(store, { draftFields: fields });
    },

    setAutosaveStatus(status: AutosaveStatusSignal): void {
      patchState(store, { autosaveStatus: status });
    },

    setHasDraft(hasDraft: boolean): void {
      patchState(store, { hasDraft });
    },

    setResumeQuestion(question: string | null): void {
      patchState(store, { resumeQuestion: question });
    },

    reset(): void {
      patchState(store, initialState);
    },
  })),
);
