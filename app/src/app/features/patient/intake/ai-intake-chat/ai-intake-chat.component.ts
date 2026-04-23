import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Input,
  OnInit,
  ViewChild,
  effect,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import {
  AiIntakeService,
  ConfirmedIntakeFields,
} from '../../../../core/services/ai-intake.service';
import {
  ChatMessage,
  ExtractedField,
  IntakeChatStore,
} from '../intake-chat.store';
import { IntakePreviewPanelComponent } from '../intake-preview-panel/intake-preview-panel.component';

@Component({
  selector: 'app-ai-intake-chat',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatIconModule,
    IntakePreviewPanelComponent,
  ],
  providers: [IntakeChatStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ai-intake-chat.component.html',
  styleUrl: './ai-intake-chat.component.scss',
})
export class AiIntakeChatComponent implements OnInit {
  @ViewChild('chatContainer') private chatContainer!: ElementRef<HTMLElement>;
  @ViewChild('previewPanel') private previewPanel!: IntakePreviewPanelComponent;

  /**
   * Optional context question injected by IntakePageComponent when the patient
   * switches from Manual → AI mode (AC-2). When set, initWithContext() displays
   * this question as the AI's opening message instead of starting a fresh session.
   */
  @Input() resumeQuestion: string | null = null;

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly aiIntakeService = inject(AiIntakeService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);

  readonly store = inject(IntakeChatStore);

  readonly appointmentId = signal('');
  readonly userInput = signal('');
  readonly isPending = signal(false);
  readonly showConfirmation = signal(false);

  constructor() {
    // Navigate to manual intake when fallback mode activates
    effect(() => {
      if (this.store.chatMode() === 'fallback_manual') {
        // Navigation is handled inside store.activateFallbackMode()
      }
    });
  }

  // ── Lifecycle ────────────────────────────────────────────────────────────

  ngOnInit(): void {
    // Support both standalone route (queryParam) and embedded mode (routeParam).
    const id =
      this.route.snapshot.paramMap.get('appointmentId') ??
      this.route.snapshot.queryParamMap.get('appointmentId') ??
      '';
    this.appointmentId.set(id);

    // If IntakePageComponent injected a resume question (Manual → AI, AC-2),
    // display it directly instead of starting a brand-new AI session.
    if (this.resumeQuestion) {
      this.initWithContext(this.resumeQuestion);
    } else {
      this.startSession(id);
    }
  }

  // ── Mode-switch integration (US_030) ─────────────────────────────────────

  /**
   * Called by IntakePageComponent after a successful Manual → AI session resume.
   * Displays the server-provided context question as the AI's opening message.
   */
  initWithContext(nextQuestion: string): void {
    this.addAssistantMessage(nextQuestion);
    this.isPending.set(false);
  }

  // ── Session ──────────────────────────────────────────────────────────────

  private startSession(appointmentId: string): void {
    if (!appointmentId) {
      return;
    }

    this.isPending.set(true);
    this.aiIntakeService
      .startSession(appointmentId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ sessionId, openingQuestion }) => {
          this.store.setSessionId(sessionId);
          this.addAssistantMessage(openingQuestion);
          this.isPending.set(false);
        },
        error: () => {
          this.isPending.set(false);
          this.store.activateFallbackMode();
        },
      });
  }

  // ── Message send ─────────────────────────────────────────────────────────

  sendMessage(): void {
    const text = this.userInput().trim();
    if (!text || this.isPending()) {
      return;
    }

    const sessionId = this.store.sessionId();
    if (!sessionId) {
      return;
    }

    // Append user message
    const userMsg: ChatMessage = {
      role: 'user',
      content: text,
      timestamp: new Date(),
    };
    this.store.addMessage(userMsg);
    this.userInput.set('');
    this.isPending.set(true);
    this.scrollToBottom();

    this.aiIntakeService
      .sendMessage(sessionId, text)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          if (response.isFallback) {
            this.store.activateFallbackMode();
            return;
          }

          if (response.aiResponse) {
            this.addAssistantMessage(response.aiResponse);
          }

          if (response.extractedFields.length > 0) {
            this.store.updateExtractedFields(response.extractedFields);
          }

          if (response.isSessionComplete) {
            this.showConfirmation.set(true);
          }

          this.isPending.set(false);
          this.scrollToBottom();
        },
        error: () => {
          this.isPending.set(false);
          this.store.activateFallbackMode();
        },
      });
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.sendMessage();
    }
  }

  // ── Confirmation / Submit ────────────────────────────────────────────────

  submitIntake(): void {
    const sessionId = this.store.sessionId();
    if (!sessionId) {
      return;
    }

    const confirmedValues: Record<string, string | undefined> =
      this.previewPanel?.getConfirmedFields() ?? {};
    const confirmedFields: ConfirmedIntakeFields = this.mapToConfirmedFields(
      confirmedValues,
      this.store.extractedFields(),
    );

    this.store.setIsSubmitting(true);

    this.aiIntakeService
      .submitIntake(sessionId, confirmedFields)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.store.setIsSubmitting(false);
          this.router.navigate(['/appointments'], {
            queryParams: { appointmentId: this.appointmentId() },
          });
        },
        error: () => {
          this.store.setIsSubmitting(false);
          this.snackBar.open(
            'Unable to submit intake. Please try again.',
            'Dismiss',
            { duration: 5000 },
          );
        },
      });
  }

  // ── Helpers ──────────────────────────────────────────────────────────────

  private addAssistantMessage(content: string): void {
    const msg: ChatMessage = {
      role: 'assistant',
      content,
      timestamp: new Date(),
    };
    this.store.addMessage(msg);
  }

  private scrollToBottom(): void {
    // Use a microtask to allow the DOM to render the new message first
    queueMicrotask(() => {
      if (this.chatContainer?.nativeElement) {
        const el = this.chatContainer.nativeElement;
        el.scrollTop = el.scrollHeight;
      }
    });
  }

  private mapToConfirmedFields(
    editedValues: Record<string, string | undefined>,
    extractedFields: ExtractedField[],
  ): ConfirmedIntakeFields {
    const merged: Record<string, string> = {};
    for (const field of extractedFields) {
      merged[field.fieldName] = editedValues[field.fieldName] ?? field.value;
    }

    return {
      demographics: merged,
      medicalHistory: [],
      symptoms: [],
      medications: [],
    };
  }
}
