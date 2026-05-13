import { type Locator, type Page } from '@playwright/test';

export class MedicalCodingPage {
  constructor(private readonly page: Page) {}

  /**
   * The page's <main> landmark — aria-label="Medical code review".
   * The component has no <h1>; the main element carries the accessible name.
   */
  get heading(): Locator {
    return this.page.locator('main[aria-label="Medical code review"]');
  }

  /**
   * Submit Review button (aria-label from MedicalCodeReviewPageComponent template).
   */
  get saveCodesButton(): Locator {
    return this.page.getByRole('button', { name: 'Submit code review decisions' });
  }

  /**
   * mat-card for a given code. The card's aria-label is
   * "{code} — {description}" (set in MedicalCodeCardComponent).
   */
  icd10Suggestion(code: string): Locator {
    return this.page.locator(`mat-card[aria-label*="${code}"]`);
  }

  cptSuggestion(code: string): Locator {
    return this.page.locator(`mat-card[aria-label*="${code}"]`);
  }

  /**
   * The Confirm button inside a code card.
   * aria-label = 'Confirm code {code}' (MedicalCodeCardComponent template).
   */
  confirmCodeButton(code: string): Locator {
    return this.page.getByRole('button', { name: `Confirm code ${code}` });
  }

  /**
   * Decision banner shown inside the card after confirming.
   * role="status" aria-label="Decision: Accepted"
   */
  confirmedBadge(_prefix: 'icd10' | 'cpt', code: string): Locator {
    return this.page
      .locator(`mat-card[aria-label*="${code}"]`)
      .locator('[role="status"][aria-label="Decision: Accepted"]');
  }

  async confirmCode(code: string): Promise<void> {
    await this.confirmCodeButton(code).click();
  }

  async saveCodes(): Promise<void> {
    await this.saveCodesButton.click();
  }
}
