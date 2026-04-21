import { type Locator, type Page } from '@playwright/test';

export class WalkInPage {
  constructor(private readonly page: Page) {}

  get searchInput(): Locator {
    return this.page.getByLabel('Search patient by name or date of birth');
  }

  get searchButton(): Locator {
    return this.page.getByRole('button', { name: 'Search' });
  }

  get skipAccountCreationButton(): Locator {
    return this.page.getByRole('button', { name: 'Skip account creation' });
  }

  get confirmWalkInButton(): Locator {
    return this.page.getByRole('button', { name: 'Confirm walk-in booking' });
  }

  get confirmAnonButton(): Locator {
    return this.page.getByRole('button', { name: 'Confirm anonymous walk-in' });
  }

  get successAlert(): Locator {
    return this.page.getByRole('alert');
  }

  get noSlotsBanner(): Locator {
    return this.page.getByTestId('no-slots-banner');
  }

  get overflowQueueButton(): Locator {
    return this.page.getByRole('button', { name: 'Add to overflow queue' });
  }

  get overflowWaitEstimate(): Locator {
    return this.page.getByTestId('overflow-wait-estimate');
  }

  get anonymousVisitId(): Locator {
    return this.page.getByTestId('anonymous-visit-id');
  }

  get selectedPatientName(): Locator {
    return this.page.getByTestId('selected-patient-name');
  }

  get walkinBookingReference(): Locator {
    return this.page.getByTestId('walkin-booking-reference');
  }

  patientResult(patientRef: string): Locator {
    return this.page.getByTestId(`patient-result-${patientRef}`);
  }

  slotCard(slotId: string): Locator {
    return this.page.getByTestId(`slot-${slotId}`);
  }

  async searchPatient(query: string): Promise<void> {
    await this.searchInput.fill(query);
    await this.searchButton.click();
  }

  async createWalkIn(patientRef: string, slotId: string): Promise<void> {
    await this.patientResult(patientRef).click();
    await this.slotCard(slotId).click();
    await this.confirmWalkInButton.click();
  }
}
