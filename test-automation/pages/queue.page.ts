import { type Locator, type Page } from '@playwright/test';

export class QueuePage {
  constructor(private readonly page: Page) {}

  /** Queue table row containing the patient's name. */
  queueEntry(patientName: string): Locator {
    return this.page.locator('tr.queue-row').filter({ hasText: patientName });
  }

  /**
   * "Mark as Arrived" button for the given patient.
   * aria-label = 'Mark {patientName} as arrived' (from queue-row template).
   */
  arrivedButton(patientName: string): Locator {
    return this.page.getByRole('button', {
      name: new RegExp(`mark ${patientName} as arrived`, 'i'),
    });
  }

  /**
   * The status chip inside the patient's row (shows 'Arrived' after marking).
   */
  arrivalTime(patientName: string): Locator {
    return this.queueEntry(patientName).locator('app-queue-status-chip');
  }

  /** Walk-In badge rendered by BookingTypeBadgeComponent inside the row. */
  walkinBadge(patientName: string): Locator {
    return this.queueEntry(patientName).getByLabel('Booking type: Walk-In');
  }

  async markArrived(patientName: string): Promise<void> {
    await this.arrivedButton(patientName).click();
  }
}
