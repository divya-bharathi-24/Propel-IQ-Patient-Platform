import { Injectable, NgZone, inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { Subscription, fromEvent, merge } from 'rxjs';
import { switchMap, debounceTime } from 'rxjs/operators';
import { timer } from 'rxjs';

/** 15 minutes expressed in milliseconds. */
const IDLE_TIMEOUT_MS = 900_000;

/** DOM events that reset the inactivity timer. */
const ACTIVITY_EVENTS = [
  'mousemove',
  'keydown',
  'click',
  'touchstart',
] as const;

/**
 * Tracks user inactivity and triggers logout after IDLE_TIMEOUT_MS.
 * Uses RxJS fromEvent + switchMap pattern to reset the countdown on activity.
 *
 * Lifecycle: start() on successful login, stop() on logout / session end.
 */
@Injectable({ providedIn: 'root' })
export class SessionTimerService {
  private readonly document = inject(DOCUMENT);
  private readonly ngZone = inject(NgZone);

  private _subscription: Subscription | null = null;

  /**
   * Begins inactivity monitoring.
   * @param onTimeout Callback invoked when the idle window elapses.
   */
  start(onTimeout: () => void): void {
    this.stop(); // ensure no duplicate subscriptions

    // Run outside Angular zone so the timer does not trigger unnecessary
    // change-detection cycles on every mousemove / keydown event.
    this.ngZone.runOutsideAngular(() => {
      const activityStreams = ACTIVITY_EVENTS.map((event) =>
        fromEvent(this.document, event),
      );

      this._subscription = merge(...activityStreams)
        .pipe(
          debounceTime(200), // coalesce rapid bursts (e.g. mouse drags)
          switchMap(() => timer(IDLE_TIMEOUT_MS)),
        )
        .subscribe(() => {
          // Re-enter zone so Angular router / signals update properly
          this.ngZone.run(() => onTimeout());
        });

      // Also start an initial timer (no activity yet after login)
      this._subscription.add(
        timer(IDLE_TIMEOUT_MS).subscribe(() => {
          this.ngZone.run(() => onTimeout());
        }),
      );
    });
  }

  /** Cancels the inactivity timer. Safe to call multiple times. */
  stop(): void {
    this._subscription?.unsubscribe();
    this._subscription = null;
  }
}
