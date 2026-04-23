import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule } from '@angular/material/dialog';
import {
  IntakeConflictPayload,
  IntakeFormValue,
} from '../../models/intake-edit-form.model';

@Component({
  selector: 'app-intake-conflict-modal',
  standalone: true,
  imports: [MatButtonModule, MatDialogModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './intake-conflict-modal.component.html',
  styleUrl: './intake-conflict-modal.component.scss',
})
export class IntakeConflictModalComponent {
  @Input({ required: true }) conflict!: IntakeConflictPayload;

  /** Emits the resolved form value that the parent should use for re-submission. */
  @Output() resolved = new EventEmitter<IntakeFormValue>();

  /** Emits when the patient cancels reconciliation (keep editing locally). */
  @Output() cancelled = new EventEmitter<void>();

  /** Which version the patient has chosen to keep ('local' | 'server'). */
  chosenVersion: 'local' | 'server' = 'local';

  selectLocal(): void {
    this.chosenVersion = 'local';
  }

  selectServer(): void {
    this.chosenVersion = 'server';
  }

  confirm(): void {
    const resolvedValue =
      this.chosenVersion === 'local'
        ? this.conflict.localVersion
        : this.conflict.serverVersion;
    this.resolved.emit(resolvedValue);
  }

  cancel(): void {
    this.cancelled.emit();
  }
}
