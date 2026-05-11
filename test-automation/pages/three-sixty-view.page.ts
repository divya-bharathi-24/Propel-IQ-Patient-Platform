import { type Locator, type Page } from '@playwright/test';

export class ThreeSixtyViewPage {
  constructor(private readonly page: Page) {}

  get heading(): Locator {
    // The h1.page-heading is not consistently in the DOM when navigating via
    // Angular Router from the walkin component (outside-zone CD cycle). Use the
    // "Verify Profile" button as the page-readiness indicator instead — it is
    // inside the @if (loadingState === 'loaded') block, so its presence confirms
    // both the route change and the data fetch completed.
    return this.page.getByRole('button', { name: /Verify [Pp]rofile/i });
  }

  get profileStatusBadge(): Locator {
    return this.page.getByTestId('profile-status-badge');
  }

  get noConflictsBanner(): Locator {
    return this.page.getByTestId('no-conflicts-banner');
  }

  get verifyProfileButton(): Locator {
    return this.page.getByRole('button', { name: 'Verify profile' });
  }

  get errorAlert(): Locator {
    return this.page.getByTestId('verify-error-alert');
  }

  get extractionFailedNotice(): Locator {
    return this.page.getByTestId('extraction-failed-notice');
  }

  conflictIndicator(field: string): Locator {
    return this.page.getByTestId(`conflict-indicator-${field}`);
  }

  conflictValue(index: 1 | 2): Locator {
    return this.page.getByTestId(`conflict-value-${index}`);
  }

  documentStatus(docRef: string): Locator {
    return this.page.getByTestId(`document-status-${docRef}`);
  }

  selectConflictValueButton(value: string): Locator {
    return this.page.getByRole('button', { name: `Select ${value}` });
  }

  async resolveConflict(fieldName: string, authoritativeValue: string): Promise<void> {
    await this.conflictIndicator(fieldName).click();
    await this.selectConflictValueButton(authoritativeValue).click();
  }

  async verifyProfile(): Promise<void> {
    await this.verifyProfileButton.click();
  }
}
