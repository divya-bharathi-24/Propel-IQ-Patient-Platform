import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import {
  IntakeConflictPayload,
  IntakeLoadState,
  IntakeSaveState,
} from '../models/intake-edit-form.model';

export interface IntakeState {
  loadingState: IntakeLoadState;
  savingState: IntakeSaveState;
  draftSavedAt: string | null;
  conflictPayload: IntakeConflictPayload | null;
  missingFields: string[];
  eTag: string;
}

const initialState: IntakeState = {
  loadingState: 'idle',
  savingState: 'idle',
  draftSavedAt: null,
  conflictPayload: null,
  missingFields: [],
  eTag: '',
};

export const IntakeStore = signalStore(
  withState<IntakeState>(initialState),
  withMethods((store) => ({
    setLoading(state: IntakeLoadState): void {
      patchState(store, { loadingState: state });
    },
    setSaving(state: IntakeSaveState): void {
      patchState(store, { savingState: state });
    },
    setETag(eTag: string): void {
      patchState(store, { eTag });
    },
    setDraftSavedAt(timestamp: string): void {
      patchState(store, { draftSavedAt: timestamp });
    },
    setConflictPayload(payload: IntakeConflictPayload | null): void {
      patchState(store, { conflictPayload: payload });
    },
    setMissingFields(fields: string[]): void {
      patchState(store, { missingFields: fields });
    },
    clearMissingFields(): void {
      patchState(store, { missingFields: [] });
    },
    reset(): void {
      patchState(store, initialState);
    },
  })),
);
