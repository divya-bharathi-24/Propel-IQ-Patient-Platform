import { type Locator, type Page } from '@playwright/test';

export class AdminUsersPage {
  constructor(private readonly page: Page) {}

  get addStaffButton(): Locator {
    return this.page.getByRole('button', { name: 'Add staff member' });
  }

  get emailInput(): Locator {
    return this.page.getByLabel('Email address');
  }

  get firstNameInput(): Locator {
    return this.page.getByLabel('First name');
  }

  get lastNameInput(): Locator {
    return this.page.getByLabel('Last name');
  }

  get roleDropdown(): Locator {
    return this.page.getByLabel('Role');
  }

  get createAccountButton(): Locator {
    return this.page.getByRole('button', { name: 'Create account' });
  }

  get editRoleButton(): Locator {
    return this.page.getByRole('button', { name: 'Edit role' });
  }

  get saveChangesButton(): Locator {
    return this.page.getByRole('button', { name: 'Save changes' });
  }

  get deactivateButton(): Locator {
    return this.page.getByRole('button', { name: 'Deactivate account' });
  }

  get reAuthDialog(): Locator {
    return this.page.getByRole('dialog');
  }

  get cancelDialogButton(): Locator {
    return this.page.getByRole('button', { name: 'Cancel' });
  }

  get successAlert(): Locator {
    return this.page.getByRole('alert');
  }

  userRow(userId: string): Locator {
    return this.page.getByTestId(`user-row-${userId}`);
  }

  roleOption(roleName: string): Locator {
    return this.page.getByRole('option', { name: roleName });
  }

  async createStaff(
    email: string,
    firstName: string,
    lastName: string,
    role: string,
  ): Promise<void> {
    await this.addStaffButton.click();
    await this.emailInput.fill(email);
    await this.firstNameInput.fill(firstName);
    await this.lastNameInput.fill(lastName);
    await this.roleDropdown.click();
    await this.roleOption(role).click();
    await this.createAccountButton.click();
  }

  async changeRole(userId: string, newRole: string): Promise<void> {
    await this.userRow(userId).click();
    await this.editRoleButton.click();
    await this.roleOption(newRole).click();
    await this.saveChangesButton.click();
  }
}
