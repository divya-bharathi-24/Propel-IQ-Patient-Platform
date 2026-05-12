/**
 * Auth setup project: creates stored authentication state files used by
 * feature and E2E tests that require pre-authenticated sessions.
 *
 * Runs once before all other test projects (declared as a dependency in
 * playwright.config.ts).
 */
import { test as setup, expect } from '@playwright/test';
import path from 'path';

const BASE_URL = process.env.BASE_URL ?? 'http://localhost:4200';

const PATIENT_EMAIL    = process.env.TEST_PATIENT_EMAIL    ?? 'auth.patient@propeliq.dev';
const PATIENT_PASSWORD = process.env.TEST_PATIENT_PASSWORD ?? 'AuthP@ss001!';
const STAFF_EMAIL      = process.env.TEST_STAFF_EMAIL      ?? 'auth.staff@propeliq.dev';
const STAFF_PASSWORD   = process.env.TEST_STAFF_PASSWORD   ?? 'StaffP@ss001!';
const ADMIN_EMAIL      = process.env.TEST_ADMIN_EMAIL      ?? 'auth.admin@propeliq.dev';
const ADMIN_PASSWORD   = process.env.TEST_ADMIN_PASSWORD   ?? 'AdminP@ss001!';

const PATIENT_AUTH_FILE = path.join(__dirname, '..', '.auth', 'patient.json');
const STAFF_AUTH_FILE   = path.join(__dirname, '..', '.auth', 'staff.json');
const ADMIN_AUTH_FILE   = path.join(__dirname, '..', '.auth', 'admin.json');

/** Mock the login endpoint to return a valid TokenResponse for the given role. */
async function mockLoginForRole(
  page: import('@playwright/test').Page,
  role: 'Patient' | 'Staff' | 'Admin',
  userId: string,
) {
  await page.route('**/api/auth/login**', route =>
    route.fulfill({
      status: 200,
      json: {
        accessToken: `mock-access-token-${role.toLowerCase()}`,
        refreshToken: `mock-refresh-token-${role.toLowerCase()}`,
        expiresIn: 3600,
        userId,
        role,
        deviceId: `mock-device-${role.toLowerCase()}`,
      },
    }),
  );
  // Mock dashboard so the Angular router can settle after redirect
  await page.route('**/api/patient/dashboard**', route =>
    route.fulfill({
      status: 200,
      json: { upcomingAppointments: [], pendingIntake: [], recentVisits: [], alerts: [], pendingAlerts: 0 },
    }),
  );
}

setup('authenticate as patient', async ({ page }) => {
  await mockLoginForRole(page, 'Patient', 'mock-patient-001');
  await page.goto(`${BASE_URL}/auth/login`);
  await page.getByLabel('Email address').fill(PATIENT_EMAIL);
  await page.getByLabel('Password').fill(PATIENT_PASSWORD);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(/dashboard/, { timeout: 15_000 });
  await page.context().storageState({ path: PATIENT_AUTH_FILE });
});

setup('authenticate as staff', async ({ page }) => {
  await mockLoginForRole(page, 'Staff', 'mock-staff-001');
  await page.goto(`${BASE_URL}/auth/login`);
  await page.getByLabel('Email address').fill(STAFF_EMAIL);
  await page.getByLabel('Password').fill(STAFF_PASSWORD);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(/walkin|dashboard/, { timeout: 15_000 });
  await page.context().storageState({ path: STAFF_AUTH_FILE });
});

setup('authenticate as admin', async ({ page }) => {
  await mockLoginForRole(page, 'Admin', 'mock-admin-001');
  await page.goto(`${BASE_URL}/auth/login`);
  await page.getByLabel('Email address').fill(ADMIN_EMAIL);
  await page.getByLabel('Password').fill(ADMIN_PASSWORD);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(/admin|dashboard/, { timeout: 15_000 });
  await page.context().storageState({ path: ADMIN_AUTH_FILE });
});
