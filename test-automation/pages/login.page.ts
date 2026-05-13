import { type Locator, type Page } from '@playwright/test';

export class LoginPage {
  constructor(private readonly page: Page) {}

  get emailInput(): Locator {
    return this.page.getByLabel('Email address');
  }

  get passwordInput(): Locator {
    return this.page.getByLabel('Password');
  }

  get signInButton(): Locator {
    return this.page.getByRole('button', { name: 'Sign in' });
  }

  get errorAlert(): Locator {
    return this.page.locator('.alert--error[role="alert"]');
  }

  get roleBadge(): Locator {
    // Sidebar renders the current role in .sidebar__user-role
    return this.page.locator('.sidebar__user-role');
  }

  async login(email: string, password: string): Promise<void> {
    await this.emailInput.fill(email);
    await this.passwordInput.fill(password);
    await this.signInButton.click();
  }
}
