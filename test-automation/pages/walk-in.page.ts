import { type Locator, type Page } from '@playwright/test';

export class WalkInPage {
  constructor(private readonly page: Page) {}

  /** Search input in the patient-search component (id is stable in the template). */
  get searchInput(): Locator {
    return this.page.locator('#patient-search-input');
  }

  /** Confirm button on the Step 3 confirm section (aria-label from HTML). */
  get confirmWalkInButton(): Locator {
    return this.page.getByRole('button', { name: 'Confirm walk-in booking' });
  }

  /** Specialty dropdown on the confirm step. */
  get specialtySelect(): Locator {
    return this.page.locator('#conf-specialty');
  }

  /**
   * A patient result option rendered by mat-selection-list.
   * Angular Material renders mat-list-option with role=option and
   * aria-label="Select patient {name}".
   */
  patientResult(name: string): Locator {
    return this.page.getByRole('option', { name: new RegExp(name, 'i') });
  }

  /**
   * Fills the search input. Results appear reactively (valueChanges, minLength 2)
   * — no search button click needed.
   */
  async searchPatient(query: string): Promise<void> {
    await this.searchInput.fill(query);
  }

  /**
   * Selects the patient from results, chooses specialty, and confirms.
   * @param patientName  Visible patient name (matches mat-list-option aria-label)
   * @param specialtyId  <option> value in the specialty dropdown
   */
  async createWalkIn(patientName: string, specialtyId: string): Promise<void> {
    await this.patientResult(patientName).click();
    // Clicking a patient advances the wizard to the confirm step (Step 3)
    await this.specialtySelect.waitFor({ state: 'visible', timeout: 10_000 });
    await this.specialtySelect.selectOption(specialtyId);
    await this.confirmWalkInButton.click();
  }
}
