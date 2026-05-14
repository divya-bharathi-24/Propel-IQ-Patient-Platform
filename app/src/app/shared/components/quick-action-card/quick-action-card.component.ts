import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-quick-action-card',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './quick-action-card.component.html',
  styleUrl: './quick-action-card.component.scss'
})
export class QuickActionCardComponent {
  @Input() label!: string;
  @Input() icon!: string;
  /** When provided the card renders as a link. Omit to render as a button. */
  @Input() route?: string;
  @Input() iconBg?: string;
  @Input() ariaLabel?: string;
  /** Emitted when the card is used as a button (no route supplied). */
  @Output() clicked = new EventEmitter<void>();
}
