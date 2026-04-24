import { Directive, HostBinding, Input } from '@angular/core';
import { ConflictSeverity } from '../../../../../core/services/patient-360-view.service';

/**
 * ConflictHighlightDirective — US_044 AC-2
 *
 * Attribute directive applied to clinical field row wrappers.
 * Adds a severity-coded CSS class to the host element based on the conflict severity:
 *  - `conflict-critical` → red border + background tint (Critical severity)
 *  - `conflict-warning`  → amber border + background tint (Warning severity)
 *  - No class applied when severity is null (no conflict on this field).
 *
 * Usage:
 *   `<div [appConflictHighlight]="conflictSeverity">`
 *
 * WCAG 2.2 AA:
 *  - Border + background tint ensures information is conveyed beyond colour alone (1.4.1).
 */
@Directive({
  selector: '[appConflictHighlight]',
  standalone: true,
})
export class ConflictHighlightDirective {
  @Input('appConflictHighlight') severity: ConflictSeverity | null = null;

  @HostBinding('class.conflict-critical')
  get isCritical(): boolean {
    return this.severity === 'Critical';
  }

  @HostBinding('class.conflict-warning')
  get isWarning(): boolean {
    return this.severity === 'Warning';
  }
}
