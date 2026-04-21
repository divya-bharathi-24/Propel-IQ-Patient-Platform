import { type Locator, type Page } from '@playwright/test';

export class IntakePage {
  constructor(private readonly page: Page) {}

  get aiModeButton(): Locator {
    return this.page.getByRole('button', { name: 'AI-Assisted intake' });
  }

  get manualModeButton(): Locator {
    return this.page.getByRole('button', { name: 'Manual form' });
  }

  get chatLog(): Locator {
    return this.page.getByRole('log');
  }

  get messageInput(): Locator {
    return this.page.getByLabel('Your message');
  }

  get sendButton(): Locator {
    return this.page.getByRole('button', { name: 'Send' });
  }

  get medicationsPreview(): Locator {
    return this.page.getByTestId('intake-preview-medications');
  }

  get allergiesPreview(): Locator {
    return this.page.getByTestId('intake-preview-allergies');
  }

  get switchToManualButton(): Locator {
    return this.page.getByRole('button', { name: 'Switch to manual form' });
  }

  get switchConfirmButton(): Locator {
    return this.page.getByRole('button', { name: 'Continue' });
  }

  get submitIntakeButton(): Locator {
    return this.page.getByRole('button', { name: 'Submit intake' });
  }

  get autosaveIndicator(): Locator {
    return this.page.getByTestId('autosave-indicator');
  }

  get modeBadge(): Locator {
    return this.page.getByTestId('intake-mode-badge');
  }

  get medicationsInput(): Locator {
    return this.page.getByLabel('Current medications');
  }

  get allergiesInput(): Locator {
    return this.page.getByLabel('Known allergies');
  }

  get symptomsInput(): Locator {
    return this.page.getByLabel('Primary symptoms');
  }

  get medHistoryInput(): Locator {
    return this.page.getByLabel('Medical history');
  }

  get confidenceWarning(): Locator {
    return this.page.getByTestId('confidence-warning');
  }

  get prepopulationNotice(): Locator {
    return this.page.getByTestId('pre-population-notice');
  }

  get successAlert(): Locator {
    return this.page.getByRole('alert');
  }

  async sendChatMessage(message: string): Promise<void> {
    await this.messageInput.fill(message);
    await this.sendButton.click();
  }

  async switchToManual(): Promise<void> {
    await this.switchToManualButton.click();
    await this.switchConfirmButton.click();
  }

  async fillManualIntake(
    medications: string,
    allergies: string,
    symptoms: string,
    medicalHistory: string,
  ): Promise<void> {
    await this.medicationsInput.fill(medications);
    await this.allergiesInput.fill(allergies);
    await this.symptomsInput.fill(symptoms);
    await this.medHistoryInput.fill(medicalHistory);
  }
}
