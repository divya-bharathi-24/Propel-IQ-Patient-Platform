import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';

export interface AppState {
  isLoading: boolean;
}

const initialState: AppState = {
  isLoading: false,
};

export const AppStore = signalStore(
  { providedIn: 'root' },
  withState<AppState>(initialState),
  withMethods((store) => ({
    setLoading(loading: boolean): void {
      patchState(store, { isLoading: loading });
    },
  })),
);
