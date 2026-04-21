import { type Locator, type Page } from '@playwright/test';

export class QueuePage {
  constructor(private readonly page: Page) {}

  get searchInput(): Locator {
    return this.page.getByLabel('Search by patient name or reference');
  }

  get searchButton(): Locator {
    return this.page.getByRole('button', { name: 'Search' });
  }

  queueEntry(ref: string): Locator {
    return this.page.getByTestId(`queue-entry-${ref}`);
  }

  arrivedButton(ref: string): Locator {
    return this.page.getByTestId(`arrived-button-${ref}`);
  }

  arrivalTime(ref: string): Locator {
    return this.page.getByTestId(`arrival-time-${ref}`);
  }

  walkinBadge(ref: string): Locator {
    return this.page.getByTestId(`walkin-badge-${ref}`);
  }

  nextActionBadge(ref: string): Locator {
    return this.page.getByTestId(`next-action-badge-${ref}`);
  }

  async markArrived(ref: string): Promise<void> {
    await this.arrivedButton(ref).click();
  }

  async searchByReference(reference: string): Promise<void> {
    await this.searchInput.fill(reference);
    await this.searchButton.click();
  }
}
