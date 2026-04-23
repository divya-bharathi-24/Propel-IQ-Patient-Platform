import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { Router } from '@angular/router';
import { inject } from '@angular/core';

// ── Domain Models ─────────────────────────────────────────────────────────────

export interface ChatMessage {
  role: 'user' | 'assistant';
  content: string;
  timestamp: Date;
}

export interface ExtractedField {
  fieldName: string;
  value: string;
  confidence: number;
  needsClarification: boolean;
}

export type ChatMode = 'ai' | 'fallback_manual';

// ── Store State ───────────────────────────────────────────────────────────────

export interface IntakeChatState {
  sessionId: string | null;
  messages: ChatMessage[];
  extractedFields: ExtractedField[];
  confidenceMap: Record<string, number>;
  chatMode: ChatMode;
  isSubmitting: boolean;
}

const initialState: IntakeChatState = {
  sessionId: null,
  messages: [],
  extractedFields: [],
  confidenceMap: {},
  chatMode: 'ai',
  isSubmitting: false,
};

const INTAKE_DRAFT_KEY = 'intake_draft';

export const IntakeChatStore = signalStore(
  withState<IntakeChatState>(initialState),
  withMethods((store) => {
    const router = inject(Router);

    return {
      setSessionId(sessionId: string): void {
        patchState(store, { sessionId });
      },

      addMessage(message: ChatMessage): void {
        patchState(store, { messages: [...store.messages(), message] });
      },

      updateExtractedFields(fields: ExtractedField[]): void {
        const confidenceMap: Record<string, number> = {};
        for (const field of fields) {
          confidenceMap[field.fieldName] = field.confidence;
        }
        patchState(store, { extractedFields: fields, confidenceMap });
      },

      setIsSubmitting(isSubmitting: boolean): void {
        patchState(store, { isSubmitting });
      },

      /**
       * Switches to fallback manual mode.
       * Persists draft extracted fields in sessionStorage for pre-population
       * on the manual intake form route.
       */
      activateFallbackMode(): void {
        try {
          sessionStorage.setItem(
            INTAKE_DRAFT_KEY,
            JSON.stringify(store.extractedFields()),
          );
        } catch {
          // sessionStorage may be unavailable (private browsing edge case)
        }
        patchState(store, { chatMode: 'fallback_manual' });
        router.navigate(['/intake/manual'], {
          state: { prefill: store.extractedFields() },
        });
      },

      reset(): void {
        patchState(store, initialState);
      },
    };
  }),
);
