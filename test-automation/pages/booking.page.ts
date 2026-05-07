import { type Locator, type Page } from '@playwright/test';

export class BookingPage {
  constructor(private readonly page: Page) {}

  slotCard(slotId: string): Locator {
    return this.page.getByTestId(`slot-${slotId}`);
  }

  get insuranceProviderInput(): Locator {
    return this.page.getByLabel('Insurer name');
  }

  get memberIdInput(): Locator {
    return this.page.getByLabel('Insurance member ID');
  }

  get verifyInsuranceButton(): Locator {
    return this.page.getByRole('button', { name: /Run insurance pre-check/i });
  }

  get insuranceStatusBadge(): Locator {
    return this.page.getByTestId('insurance-status');
  }

  get continueToIntakeButton(): Locator {
    return this.page.getByRole('button', { name: 'Continue to intake' });
  }

  get confirmBookingButton(): Locator {
    return this.page.getByRole('button', { name: 'Confirm booking' });
  }

  get bookingReference(): Locator {
    return this.page.getByTestId('booking-reference');
  }

  get setPreferredSlotButton(): Locator {
    return this.page.getByRole('button', { name: 'Set a preferred slot' });
  }

  get preferredSlotIndicator(): Locator {
    return this.page.getByTestId('preferred-slot-indicator');
  }

  get confirmButton(): Locator {
    return this.page.getByRole('button', { name: 'Confirm booking' });
  }

  async selectSlot(slotId: string): Promise<void> {
    await this.slotCard(slotId).click();
  }

  async verifyInsurance(provider: string, memberId: string): Promise<void> {
    await this.insuranceProviderInput.fill(provider);
    await this.memberIdInput.fill(memberId);
    await this.verifyInsuranceButton.click();
  }

  async confirmBooking(): Promise<string> {
    await this.confirmBookingButton.click();
    return this.bookingReference.innerText();
  }
}
