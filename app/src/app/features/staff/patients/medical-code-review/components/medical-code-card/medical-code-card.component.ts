import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  signal,
} from '@angular/core';
import { NgClass } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import {
  CodeDecision,
  MedicalCodeSuggestionDto,
} from '../../models/medical-code.models';

/**
 * MedicalCodeCardComponent — US_043 AC-1 / AC-2 / AC-3
 *
 * Displays a single AI-suggested medical code with:
 *   - Code string, description, confidence badge (green ≥ 0.80, amber < 0.80).
 *   - "Low Confidence — Review Required" tooltip on the badge when flagged.
 *   - Supporting evidence text in a collapsible mat-expansion-panel (AC-1).
 *   - "Confirm" button → emits `confirmed` output; card transitions to Accepted (AC-2).
 *   - "Reject" button → reveals a rejection reason field; Reject is disabled until the
 *     reason is non-empty, then emits `rejected` with the reason string (AC-3).
 *
 * WCAG 2.2 AA:
 *  - Confidence badge uses colour + icon so information is not colour-only (1.4.1).
 *  - Reject confirmation button is `aria-disabled` while reason is empty (4.1.2).
 *  - Card root has `aria-label` carrying the code and description (4.1.2).
 */
@Component({
  selector: 'app-medical-code-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgClass,
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatExpansionModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatTooltipModule,
  ],
  template: `
    <mat-card
      class="code-card"
      [ngClass]="cardStateClass"
      [attr.aria-label]="suggestion.code + ' — ' + suggestion.description"
    >
      <mat-card-header>
        <mat-card-title class="code-title">
          <span class="code-value">{{ suggestion.code }}</span>

          <!-- Confidence badge -->
          <span
            class="confidence-badge"
            [ngClass]="confidenceBadgeClass"
            [matTooltip]="confidenceTooltip"
            matTooltipPosition="above"
            role="status"
            [attr.aria-label]="confidenceAriaLabel"
          >
            @if (suggestion.lowConfidence) {
              <mat-icon class="badge-icon" aria-hidden="true">warning</mat-icon>
            }
            {{ confidencePercent }}
          </span>
        </mat-card-title>

        <mat-card-subtitle>{{ suggestion.description }}</mat-card-subtitle>
      </mat-card-header>

      <!-- Decision state badge -->
      @if (decision.status !== 'Pending') {
        <div
          class="decision-banner"
          [ngClass]="{
            'decision-accepted': decision.status === 'Accepted',
            'decision-rejected': decision.status === 'Rejected',
          }"
          role="status"
          [attr.aria-label]="'Decision: ' + decision.status"
        >
          @if (decision.status === 'Accepted') {
            <mat-icon aria-hidden="true">check_circle</mat-icon>
            Accepted
          }
          @if (decision.status === 'Rejected') {
            <mat-icon aria-hidden="true">cancel</mat-icon>
            Rejected
            @if (decision.rejectionReason) {
              <span class="rejection-reason-text">
                — {{ decision.rejectionReason }}</span
              >
            }
          }
        </div>
      }

      <!-- Supporting evidence (AC-1) -->
      <mat-accordion>
        <mat-expansion-panel>
          <mat-expansion-panel-header>
            <mat-panel-title>Supporting Evidence</mat-panel-title>
          </mat-expansion-panel-header>
          <p class="evidence-text">{{ suggestion.evidenceText }}</p>
        </mat-expansion-panel>
      </mat-accordion>

      <!-- Action buttons -->
      @if (decision.status === 'Pending') {
        <mat-card-actions class="card-actions">
          <!-- Confirm (AC-2) -->
          <button
            mat-raised-button
            color="primary"
            type="button"
            (click)="onConfirm()"
            [attr.aria-label]="'Confirm code ' + suggestion.code"
          >
            <mat-icon aria-hidden="true">check</mat-icon>
            Confirm
          </button>

          <!-- Reject (AC-3) -->
          @if (!showRejectForm()) {
            <button
              mat-stroked-button
              color="warn"
              type="button"
              (click)="openRejectForm()"
              [attr.aria-label]="'Reject code ' + suggestion.code"
            >
              <mat-icon aria-hidden="true">close</mat-icon>
              Reject
            </button>
          }
        </mat-card-actions>
      }

      <!-- Rejection reason form (AC-3) -->
      @if (showRejectForm() && decision.status === 'Pending') {
        <div class="reject-form" role="form" aria-label="Rejection reason form">
          <mat-form-field appearance="outline" class="reason-field">
            <mat-label>Rejection reason</mat-label>
            <input
              matInput
              [(ngModel)]="rejectionReason"
              placeholder="Enter a reason for rejection"
              aria-required="true"
              [attr.aria-invalid]="rejectionReason.trim().length === 0"
            />
            @if (rejectionReason.trim().length === 0) {
              <mat-hint
                >A reason is required before confirming rejection.</mat-hint
              >
            }
          </mat-form-field>

          <div class="reject-actions">
            <button
              mat-raised-button
              color="warn"
              type="button"
              [disabled]="rejectionReason.trim().length === 0"
              [attr.aria-disabled]="rejectionReason.trim().length === 0"
              (click)="onReject()"
              [attr.aria-label]="'Confirm rejection of code ' + suggestion.code"
            >
              Confirm Rejection
            </button>

            <button
              mat-button
              type="button"
              (click)="cancelReject()"
              aria-label="Cancel rejection"
            >
              Cancel
            </button>
          </div>
        </div>
      }
    </mat-card>
  `,
  styles: [
    `
      :host {
        display: block;
      }

      .code-card {
        margin-bottom: 12px;
        transition: box-shadow 0.2s ease;
      }

      .code-card:focus-within {
        box-shadow: 0 0 0 3px #1976d2;
      }

      .code-card.state-accepted {
        border-left: 4px solid #2e7d32;
      }

      .code-card.state-rejected {
        border-left: 4px solid #c62828;
      }

      .code-card.state-low-confidence {
        border-left: 4px solid #f57c00;
      }

      .code-title {
        display: flex;
        align-items: center;
        gap: 8px;
        font-size: 1rem;
        font-weight: 600;
      }

      .code-value {
        font-family: 'Courier New', Courier, monospace;
      }

      .confidence-badge {
        display: inline-flex;
        align-items: center;
        gap: 2px;
        padding: 2px 8px;
        border-radius: 12px;
        font-size: 0.72rem;
        font-weight: 600;
        color: #fff;
        cursor: default;
      }

      .badge-high {
        background-color: #2e7d32;
      }

      .badge-low {
        background-color: #f57c00;
      }

      .badge-icon {
        font-size: 14px;
        height: 14px;
        width: 14px;
        line-height: 14px;
      }

      .decision-banner {
        display: flex;
        align-items: center;
        gap: 4px;
        padding: 6px 16px;
        font-size: 0.875rem;
        font-weight: 500;
      }

      .decision-accepted {
        color: #2e7d32;
        background-color: #e8f5e9;
      }

      .decision-rejected {
        color: #c62828;
        background-color: #ffebee;
      }

      .rejection-reason-text {
        font-style: italic;
        font-weight: 400;
      }

      .evidence-text {
        font-size: 0.875rem;
        color: rgba(0, 0, 0, 0.7);
        line-height: 1.5;
        white-space: pre-wrap;
      }

      .card-actions {
        display: flex;
        gap: 8px;
        padding: 8px 16px;
      }

      .reject-form {
        padding: 0 16px 12px;
      }

      .reason-field {
        width: 100%;
      }

      .reject-actions {
        display: flex;
        gap: 8px;
        margin-top: 4px;
      }
    `,
  ],
})
export class MedicalCodeCardComponent {
  @Input({ required: true }) suggestion!: MedicalCodeSuggestionDto;
  @Input({ required: true }) decision!: CodeDecision;

  /** Emitted when staff clicks "Confirm" (AC-2). */
  @Output() confirmed = new EventEmitter<void>();

  /**
   * Emitted when staff confirms rejection with a non-empty reason (AC-3).
   * Carries the rejection reason string.
   */
  @Output() rejected = new EventEmitter<string>();

  protected showRejectForm = signal(false);
  protected rejectionReason = '';

  protected get confidencePercent(): string {
    return `${Math.round(this.suggestion.confidenceScore * 100)}%`;
  }

  protected get confidenceBadgeClass(): string {
    return this.suggestion.lowConfidence ? 'badge-low' : 'badge-high';
  }

  protected get confidenceTooltip(): string {
    return this.suggestion.lowConfidence
      ? 'Low Confidence — Review Required'
      : `Confidence: ${this.confidencePercent}`;
  }

  protected get confidenceAriaLabel(): string {
    return this.suggestion.lowConfidence
      ? `Low confidence at ${this.confidencePercent} — review required`
      : `Confidence: ${this.confidencePercent}`;
  }

  protected get cardStateClass(): string {
    if (this.decision.status === 'Accepted') return 'state-accepted';
    if (this.decision.status === 'Rejected') return 'state-rejected';
    if (this.suggestion.lowConfidence) return 'state-low-confidence';
    return '';
  }

  protected onConfirm(): void {
    this.confirmed.emit();
  }

  protected openRejectForm(): void {
    this.rejectionReason = '';
    this.showRejectForm.set(true);
  }

  protected onReject(): void {
    const reason = this.rejectionReason.trim();
    if (!reason) return;
    this.rejected.emit(reason);
    this.showRejectForm.set(false);
  }

  protected cancelReject(): void {
    this.rejectionReason = '';
    this.showRejectForm.set(false);
  }
}
