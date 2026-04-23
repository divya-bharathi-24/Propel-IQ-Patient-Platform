import { Injectable, inject, NgZone } from '@angular/core';
import { IntakeFieldMap } from '../models/intake-edit-form.model';
import { IntakeService } from './intake.service';

// ── Types ──────────────────────────────────────────────────────────────────────

export interface LocalDraftEntry {
  fields: IntakeFieldMap;
  /** Unix timestamp (ms) recorded when the draft was written. */
  capturedAt: number;
}

// ── Service ────────────────────────────────────────────────────────────────────

/**
 * LocalDraftService — localStorage fallback for intake drafts.
 *
 * HIPAA note (NFR-013): localStorage is a *transient* fallback only.
 * The draft is cleared immediately after a successful server sync so PHI
 * is not persisted in the browser beyond the current session's failure window.
 */
@Injectable()
export class LocalDraftService {
  private readonly intakeService = inject(IntakeService);
  private readonly ngZone = inject(NgZone);

  /** Active appointment ID; set once by IntakePageComponent on init. */
  private appointmentId = '';

  /** Conflict resolution callback registered by the consuming component. */
  private onConflict?: (
    local: IntakeFieldMap,
    server: IntakeFieldMap,
  ) => void;

  private onlineHandler = (): void => {
    this.ngZone.run(() => this.syncToServer());
  };

  // ── Lifecycle ──────────────────────────────────────────────────────────────

  /**
   * Binds the service to an appointment and registers the online-event listener
   * that triggers server sync after a network reconnect.
   */
  init(
    appointmentId: string,
    onConflict: (local: IntakeFieldMap, server: IntakeFieldMap) => void,
  ): void {
    this.appointmentId = appointmentId;
    this.onConflict = onConflict;
    window.addEventListener('online', this.onlineHandler);
  }

  /** Must be called from the consuming component's ngOnDestroy. */
  destroy(): void {
    window.removeEventListener('online', this.onlineHandler);
  }

  // ── Read / Write ───────────────────────────────────────────────────────────

  /**
   * Persists the draft in localStorage.
   * Key format: `intake_draft_{appointmentId}` for per-appointment isolation.
   */
  save(fields: IntakeFieldMap, capturedAt: number = Date.now()): void {
    if (!this.appointmentId) {
      return;
    }
    try {
      const entry: LocalDraftEntry = { fields, capturedAt };
      localStorage.setItem(
        this.storageKey(),
        JSON.stringify(entry),
      );
    } catch {
      // localStorage quota exceeded — silently discard; not a fatal error.
    }
  }

  /**
   * Loads the locally stored draft entry, or null if none exists.
   */
  load(appointmentId: string): LocalDraftEntry | null {
    try {
      const raw = localStorage.getItem(`intake_draft_${appointmentId}`);
      if (!raw) {
        return null;
      }
      return JSON.parse(raw) as LocalDraftEntry;
    } catch {
      return null;
    }
  }

  /** Removes the local draft. */
  clear(): void {
    if (!this.appointmentId) {
      return;
    }
    try {
      localStorage.removeItem(this.storageKey());
    } catch {
      // Ignore — nothing to clear.
    }
  }

  // ── Server sync ────────────────────────────────────────────────────────────

  private syncToServer(): void {
    const entry = this.load(this.appointmentId);
    if (!entry) {
      return;
    }

    this.intakeService
      .syncLocalDraft(this.appointmentId, entry.fields, entry.capturedAt)
      .subscribe({
        next: () => {
          // Draft successfully synced — clear local copy immediately (NFR-013).
          this.clear();
        },
        error: (err) => {
          // 409 Conflict — let the parent component decide resolution strategy.
          if (err?.status === 409 && this.onConflict) {
            const serverFields = err?.error?.serverVersion as IntakeFieldMap;
            this.onConflict(entry.fields, serverFields);
          }
          // Other errors are ignored — local draft remains for the next reconnect.
        },
      });
  }

  // ── Helpers ────────────────────────────────────────────────────────────────

  private storageKey(): string {
    return `intake_draft_${this.appointmentId}`;
  }
}
