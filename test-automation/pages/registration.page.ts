import { type Locator, type Page } from '@playwright/test';

export class RegistrationPage {
  constructor(private readonly page: Page) {}

  get fullNameInput(): Locator {
    return this.page.getByLabel('Full Name');
  }

  get emailInput(): Locator {
    return this.page.getByLabel('Email Address');
  }

  get passwordInput(): Locator {
    return this.page.getByLabel('Password');
  }

  get phoneInput(): Locator {
    return this.page.getByLabel('Phone Number (optional)');
  }

  get dobInput(): Locator {
    return this.page.getByLabel('Date of Birth');
  }

  get createAccountButton(): Locator {
    return this.page.getByRole('button', { name: 'Create Account' });
  }

  get errorAlert(): Locator {
    return this.page.getByRole('alert');
  }

  get loginLink(): Locator {
    return this.page.getByRole('link', { name: 'Sign in' });
  }

  async register(
    email: string,
    password: string,
    firstName: string,
    lastName: string,
    dateOfBirth: string,
    phone: string,
  ): Promise<void> {
    await this.fullNameInput.fill(`${firstName} ${lastName}`);
    await this.emailInput.fill(email);
    await this.passwordInput.fill(password);
    await this.phoneInput.fill(phone);
    await this.dobInput.fill(dateOfBirth);
    await this.createAccountButton.click();
  }
}
