import { ChangeDetectionStrategy, Component, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';

/**
 * Displays a dismissible banner when an incomplete manual intake draft exists.
 * The parent component decides whether to resume or start fresh, and this
 * component emits the appropriate event so that logic stays out of the view.
 */
@Component({
  selector: 'app-resume-draft-banner',
  standalone: true,
  imports: [MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './resume-draft-banner.component.html',
  styleUrl: './resume-draft-banner.component.scss',
})
export class ResumeDraftBannerComponent {
  readonly resumeDraft = output<void>();
  readonly startFresh = output<void>();

  onResume(): void {
    this.resumeDraft.emit();
  }

  onStartFresh(): void {
    this.startFresh.emit();
  }
}
