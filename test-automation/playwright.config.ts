import { defineConfig, devices } from '@playwright/test';
import path from 'path';

const BASE_URL = process.env.BASE_URL ?? 'http://localhost:4200';

export const STORAGE_STATE_PATIENT = path.join(__dirname, '.auth', 'patient.json');
export const STORAGE_STATE_STAFF   = path.join(__dirname, '.auth', 'staff.json');
export const STORAGE_STATE_ADMIN   = path.join(__dirname, '.auth', 'admin.json');

export default defineConfig({
  testDir: './',
  timeout: 30_000,
  expect: { timeout: 5_000 },
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 4 : undefined,
  reporter: [['html', { open: 'never' }], ['list']],

  use: {
    baseURL: BASE_URL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },

  projects: [
    // ── Auth setup ──────────────────────────────────────────────────────────
    {
      name: 'setup',
      testMatch: /support\/auth\.setup\.ts/,
    },

    // ── Feature tests (parallel, use stored auth) ───────────────────────────
    {
      name: 'feature-patient',
      testDir: './tests',
      use: {
        ...devices['Desktop Chrome'],
        storageState: STORAGE_STATE_PATIENT,
      },
      dependencies: ['setup'],
      testIgnore: /admin_notifications/,
    },
    {
      name: 'feature-staff',
      testDir: './tests',
      use: {
        ...devices['Desktop Chrome'],
        storageState: STORAGE_STATE_STAFF,
      },
      dependencies: ['setup'],
      testMatch: /slot_clinical/,
    },
    {
      name: 'feature-admin',
      testDir: './tests',
      use: {
        ...devices['Desktop Chrome'],
        storageState: STORAGE_STATE_ADMIN,
      },
      dependencies: ['setup'],
      testMatch: /admin_notifications/,
    },

    // ── E2E journeys (serial, full-session) ─────────────────────────────────
    {
      name: 'e2e',
      testDir: './e2e',
      use: {
        ...devices['Desktop Chrome'],
      },
      fullyParallel: false,
      dependencies: ['setup'],
    },

    // ── Standalone tests (no auth setup required) ────────────────────────────
    // Used for registration, login, and other tests that manage their own sessions.
    {
      name: 'standalone',
      testDir: './tests',
      use: {
        ...devices['Desktop Chrome'],
      },
      testMatch: /registration_login|login\.spec|booking\.spec/,
    },
  ],
});
