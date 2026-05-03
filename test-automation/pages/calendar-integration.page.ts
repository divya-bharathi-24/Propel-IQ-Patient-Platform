import { type Locator, type Page } from '@playwright/test';

export class CalendarIntegrationPage {
  constructor(private readonly page: Page) {}

  get addToGoogleButton(): Locator {
    return this.page.getByRole('button', { name: 'Add to Google Calendar' });
  }

  get downloadIcsButton(): Locator {
    return this.page.getByRole('button', { name: 'Download ICS file' });
  }

  get syncBadge(): Locator {
    return this.page.getByTestId('calendar-sync-badge');
  }

  get icsFallbackLink(): Locator {
    return this.page.getByRole('link', { name: 'Download ICS instead' });
  }

  get successAlert(): Locator {
    return this.page.getByRole('alert');
  }

  appointmentCard(appointmentId: string): Locator {
    return this.page.getByTestId(`appointment-${appointmentId}`);
  }

  async openAppointment(appointmentId: string): Promise<void> {
    await this.appointmentCard(appointmentId).click();
  }
}
