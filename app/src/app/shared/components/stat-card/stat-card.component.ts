import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-stat-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './stat-card.component.html',
  styleUrl: './stat-card.component.scss'
})
export class StatCardComponent {
  @Input() label!: string;
  @Input() value!: string | number;
  @Input() delta?: string;
  @Input() deltaType?: 'up' | 'down' | 'neutral';
  @Input() badgeText?: string;
  @Input() badgeType?: 'success' | 'warning' | 'error' | 'info';
}
