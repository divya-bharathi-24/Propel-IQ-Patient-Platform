import { Component, Input } from '@angular/core';
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
  @Input() route!: string;
  @Input() iconBg?: string;
}
