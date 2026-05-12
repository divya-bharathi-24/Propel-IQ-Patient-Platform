import { type Locator, type Page } from '@playwright/test';

export class IntakePage {
  constructor(private readonly page: Page) {}

  get aiModeButton(): Locator {
    return this.page.getByRole('button', { name: 'AI-Assisted intake' });
  }

  /** Button shown when current mode is AI — switches to manual form. */
  get manualModeButton(): Locator {
    return this.page.getByRole('button', { name: 'Switch to manual intake form' });
  }

  get chatLog(): Locator {
    return this.page.getByRole('log');
  }

  get messageInput(): Locator {
    return this.page.getByLabel('Your message');
  }

  get sendButton(): Locator {
    return this.page.getByRole('button', { name: 'Send message' });
  }

  get medicationsPreview(): Locator {
    return this.page.getByRole('complementary', { name: /Live form preview/i });
  }

  /**
   * The extracted medications value field.
   * Once the AI session completes, the preview switches to edit mode
   * and values are rendered inside <input> elements (not <span> text).
   * Use toHaveValue() on this locator instead of toContainText() on the aside.
   */
  get medicationsValueField(): Locator {
    return this.page.getByRole('textbox', { name: 'Medications' });
  }

  get allergiesPreview(): Locator {
    return this.page.getByTestId('intake-preview-allergies');
  }

  /** Same button as manualModeButton — shown when mode is AI. */
  get switchToManualButton(): Locator {
    return this.page.getByRole('button', { name: 'Switch to manual intake form' });
  }

  /** Submit button inside manual-intake-form component. */
  get submitIntakeButton(): Locator {
    return this.page.getByRole('button', { name: 'Submit completed intake form' });
  }

  /** Submit button inside ai-intake-chat component ("Confirm & Submit"). */
  get aiSubmitButton(): Locator {
    return this.page.getByRole('button', { name: 'Confirm and submit intake form' });
  }

  /** Autosave status chip inside the manual intake form. */
  get autosaveIndicator(): Locator {
    return this.page.getByTestId('autosave-indicator');
  }

  get modeBadge(): Locator {
    return this.page.getByTestId('intake-mode-badge');
  }

  /** First medication name input (present after addMedicationButton is clicked). */
  get medicationsInput(): Locator {
    return this.page.getByRole('textbox', { name: 'Medication name 1' });
  }

  /** First allergy substance input (present after addAllergyButton is clicked). */
  get allergiesInput(): Locator {
    return this.page.getByRole('textbox', { name: 'Allergen 1', exact: true });
  }

  /** Family health history textarea in the Medical History section. */
  get medHistoryInput(): Locator {
    return this.page.getByLabel('Family health history');
  }

  /** Button to add a new medication item to the form array. */
  get addMedicationButton(): Locator {
    return this.page.getByRole('button', { name: 'Add a current medication' });
  }

  /** Button to add a new allergy item to the form array. */
  get addAllergyButton(): Locator {
    return this.page.getByRole('button', { name: 'Add a known allergy' });
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

  /** Switch to manual intake form — no confirmation dialog required. */
  async switchToManual(): Promise<void> {
    await this.switchToManualButton.click();
  }

  /**
   * Fill the manual intake form with the provided values.
   * Adds one medication item and one allergy item via the "Add" buttons,
   * fills required demographics with test defaults, and fills family history.
   */
  async fillManualIntake(
    medications: string,
    allergies: string,
    _symptoms: string,
    medicalHistory: string,
  ): Promise<void> {
    // Fill required demographics fields
    await this.page.getByLabel(/First Name/i).first().fill('Test');
    await this.page.getByLabel(/Last Name/i).first().fill('Patient');
    await this.page.locator('#dateOfBirth').fill('1990-01-01');
    await this.page.getByLabel(/Gender/i).click();
    await this.page.getByRole('option', { name: 'Other' }).click();
    await this.page.locator('#phone').fill('+15550000000');

    // Add and fill one medication
    await this.addMedicationButton.click();
    await this.medicationsInput.fill(medications);

    // Add and fill one allergy
    await this.addAllergyButton.click();
    await this.allergiesInput.fill(allergies);

    // Fill family health history
    await this.medHistoryInput.fill(medicalHistory);
  }
}
