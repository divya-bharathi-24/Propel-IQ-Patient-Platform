import { type Locator, type Page } from '@playwright/test';

export class RegistrationPage {
  constructor(private readonly page: Page) {}

  get emailInput(): Locator {
    return this.page.getByLabel('Email address');
  }

  get passwordInput(): Locator {
    return this.page.getByLabel('Password');
  }

  get firstNameInput(): Locator {
    return this.page.getByLabel('First name');
  }

  get lastNameInput(): Locator {
    return this.page.getByLabel('Last name');
  }

  get dobInput(): Locator {
    return this.page.getByLabel('Date of birth');
  }

  get phoneInput(): Locator {
    return this.page.getByLabel('Phone number');
  }

  get createAccountButton(): Locator {
    return this.page.getByRole('button', { name: 'Create account' });
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
    await this.emailInput.fill(email);
    await this.passwordInput.fill(password);
    await this.firstNameInput.fill(firstName);
    await this.lastNameInput.fill(lastName);
    await this.dobInput.fill(dateOfBirth);
    await this.phoneInput.fill(phone);
    await this.createAccountButton.click();
  }
}
