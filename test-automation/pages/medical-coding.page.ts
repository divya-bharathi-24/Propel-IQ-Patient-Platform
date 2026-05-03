import { type Locator, type Page } from '@playwright/test';

export class MedicalCodingPage {
  constructor(private readonly page: Page) {}

  get heading(): Locator {
    return this.page.getByRole('heading', { name: 'Medical Code Review' });
  }

  get saveCodesButton(): Locator {
    return this.page.getByRole('button', { name: 'Save confirmed codes' });
  }

  get addManuallyButton(): Locator {
    return this.page.getByRole('button', { name: 'Add code manually' });
  }

  get codeInput(): Locator {
    return this.page.getByLabel('ICD-10 code');
  }

  get validateCodeButton(): Locator {
    return this.page.getByRole('button', { name: 'Validate code' });
  }

  get validationResult(): Locator {
    return this.page.getByTestId('code-validation-result');
  }

  get addToConfirmedButton(): Locator {
    return this.page.getByRole('button', { name: 'Add to confirmed codes' });
  }

  get successAlert(): Locator {
    return this.page.getByRole('alert');
  }

  get codingCompleteBadge(): Locator {
    return this.page.getByTestId('coding-completion-badge');
  }

  icd10Suggestion(code: string): Locator {
    return this.page.getByTestId(`icd10-suggestion-${code}`);
  }

  cptSuggestion(code: string): Locator {
    return this.page.getByTestId(`cpt-suggestion-${code}`);
  }

  confirmCodeButton(code: string): Locator {
    return this.page.getByRole('button', { name: `Confirm ${code}` });
  }

  confirmedBadge(prefix: 'icd10' | 'cpt', code: string): Locator {
    return this.page.getByTestId(`${prefix}-confirmed-${code}`);
  }

  async confirmCode(code: string): Promise<void> {
    await this.confirmCodeButton(code).click();
  }

  async saveCodes(): Promise<void> {
    await this.saveCodesButton.click();
  }

  async addManualCode(code: string): Promise<void> {
    await this.addManuallyButton.click();
    await this.codeInput.fill(code);
    await this.validateCodeButton.click();
  }
}
