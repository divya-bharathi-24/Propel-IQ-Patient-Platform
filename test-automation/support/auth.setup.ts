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

setup('authenticate as patient', async ({ page }) => {
  await page.goto(`${BASE_URL}/auth/login`);
  await page.getByLabel('Email address').fill(PATIENT_EMAIL);
  await page.getByLabel('Password').fill(PATIENT_PASSWORD);
  await page.getByRole('button', { name: 'Sign in to your account' }).click();
  await expect(page).toHaveURL(/dashboard/);
  await page.context().storageState({ path: PATIENT_AUTH_FILE });
});

setup('authenticate as staff', async ({ page }) => {
  await page.goto(`${BASE_URL}/auth/login`);
  await page.getByLabel('Email address').fill(STAFF_EMAIL);
  await page.getByLabel('Password').fill(STAFF_PASSWORD);
  await page.getByRole('button', { name: 'Sign in to your account' }).click();
  await expect(page).toHaveURL(/staff/);
  await page.context().storageState({ path: STAFF_AUTH_FILE });
});

setup('authenticate as admin', async ({ page }) => {
  await page.goto(`${BASE_URL}/auth/login`);
  await page.getByLabel('Email address').fill(ADMIN_EMAIL);
  await page.getByLabel('Password').fill(ADMIN_PASSWORD);
  await page.getByRole('button', { name: 'Sign in to your account' }).click();
  await expect(page).toHaveURL(/admin/);
  await page.context().storageState({ path: ADMIN_AUTH_FILE });
});
