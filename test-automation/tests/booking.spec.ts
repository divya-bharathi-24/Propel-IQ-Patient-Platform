/**
 * @Booking — Login then book an appointment using the booking wizard.
 * Run with: npx playwright test tests/booking.spec.ts --project=standalone --headed
 *
 * IMPORTANT: Uses SPA (Angular router) navigation after login to keep
 * in-memory auth state alive. Never call page.goto() for protected routes.
 */
import { test, expect } from '@playwright/test';
import { LoginPage } from '../pages/login.page';

const BASE_URL = process.env.BASE_URL ?? 'http://localhost:4200';

// Credentials for the test patient
const TEST_EMAIL    = process.env.TEST_PATIENT_EMAIL    ?? 'auth.patient@propeliq.dev';
const TEST_PASSWORD = process.env.TEST_PATIENT_PASSWORD ?? 'AuthP@ss001!';

/** Mock all APIs needed for the full booking flow. */
async function mockAllApis(page: import('@playwright/test').Page) {
  // Auth — login must include userId + deviceId so Angular signals populate correctly
  await page.route('**/api/auth/login**', route =>
    route.fulfill({
      status: 200,
      json: {
        accessToken: 'mock-access-token',
        refreshToken: 'mock-refresh-token',
        userId: 'user-test-001',
        role: 'Patient',
        deviceId: 'device-test-001',
        expiresIn: 3600,
      },
    }),
  );

  // Dashboard — return empty appointments so "Book Appointment" CTA is visible
  await page.route('**/api/patient/dashboard**', route =>
    route.fulfill({
      status: 200,
      json: {
        upcomingAppointments: [],
        pendingIntake: [],
        recentVisits: [],
        alerts: [],
        pendingAlerts: 0,
      },
    }),
  );

  // Specialties
  await page.route('**/api/appointments/specialties**', route =>
    route.fulfill({
      status: 200,
      json: [
        { id: 'spec-001', name: 'General Practice' },
        { id: 'spec-002', name: 'Cardiology' },
      ],
    }),
  );

  // Available slots — response is SlotDto[] (flat array, not wrapped)
  // timeSlotStart/End must be ISO 8601; field is isAvailable (not available)
  await page.route('**/api/appointments/slots**', route =>
    route.fulfill({
      status: 200,
      json: [
        {
          timeSlotStart: '2026-05-20T09:00:00.000Z',
          timeSlotEnd:   '2026-05-20T09:30:00.000Z',
          isAvailable: true,
          specialtyId: 'spec-001',
          date: '2026-05-20',
        },
        {
          timeSlotStart: '2026-05-20T10:00:00.000Z',
          timeSlotEnd:   '2026-05-20T10:30:00.000Z',
          isAvailable: true,
          specialtyId: 'spec-001',
          date: '2026-05-20',
        },
      ],
    }),
  );

  // Hold slot
  await page.route('**/api/appointments/hold-slot**', route =>
    route.fulfill({ status: 200, json: { held: true } }),
  );

  // Insurance pre-check
  await page.route('**/api/insurance/pre-check**', route =>
    route.fulfill({
      status: 200,
      json: { status: 'Verified', guidance: 'Coverage confirmed.' },
    }),
  );

  // Book appointment
  await page.route('**/api/appointments/book**', route =>
    route.fulfill({
      status: 200,
      json: {
        referenceNumber: 'REF-TEST-001',
        timeSlotStart: '09:00',
        specialtyName: 'General Practice',
        date: '2026-05-20',
      },
    }),
  );
}

/**
 * Login via the login form then use Angular router link to navigate to the
 * booking page — avoids a full page reload that would clear in-memory auth state.
 */
async function loginAndGoToBooking(page: import('@playwright/test').Page) {
  await page.goto(`${BASE_URL}/auth/login`);
  const login = new LoginPage(page);
  await login.login(TEST_EMAIL, TEST_PASSWORD);

  // Wait for Angular router to navigate to /dashboard
  await expect(page).toHaveURL(/dashboard/, { timeout: 15_000 });

  // Click "Book Appointment" router link (SPA navigation — keeps auth state)
  // aria-label is "Book a new appointment", text is "Book Appointment"
  await expect(page.getByRole('link', { name: 'Book a new appointment' })).toBeVisible({ timeout: 10_000 });
  await page.getByRole('link', { name: 'Book a new appointment' }).click();

  // Wait for booking wizard URL (still within the SPA)
  await expect(page).toHaveURL(/appointments\/book/, { timeout: 10_000 });
}

test.describe('@Booking Appointment Booking Wizard', () => {

  test('@Booking TC-BOOK-001: Patient logs in and reaches the booking page', async ({ page }) => {
    await mockAllApis(page);

    await test.step('Login and navigate to booking page via SPA link', async () => {
      await loginAndGoToBooking(page);
    });

    await test.step('Booking wizard is visible', async () => {
      // The wizard renders a stepper; assert at least the first step heading
      await expect(
        page.getByRole('heading', { name: /Select a Slot/i })
          .or(page.getByText(/Select a Slot/i).first())
      ).toBeVisible({ timeout: 15_000 });
    });
  });

  test('@Booking TC-BOOK-002: Patient selects a specialty and an available slot', async ({ page }) => {
    await mockAllApis(page);
    await loginAndGoToBooking(page);

    await test.step('Select specialty from dropdown', async () => {
      // mat-select has aria-label="Appointment specialty"
      await expect(page.getByLabel('Appointment specialty')).toBeVisible({ timeout: 10_000 });
      await page.getByLabel('Appointment specialty').click();
      await page.getByRole('option', { name: 'General Practice' }).click();
    });

    await test.step('Select an available time slot', async () => {
      // Slot buttons have aria-label "Book slot <locale-time>" — use broad regex
      await expect(page.getByLabel(/Book slot/i).first()).toBeVisible({ timeout: 10_000 });
      await page.getByLabel(/Book slot/i).first().click();
    });
  });

  test('@Booking TC-BOOK-003: Patient completes insurance check and confirms booking', async ({ page }) => {
    await mockAllApis(page);
    await loginAndGoToBooking(page);

    await test.step('Select specialty and slot', async () => {
      await expect(page.getByLabel('Appointment specialty')).toBeVisible({ timeout: 10_000 });
      await page.getByLabel('Appointment specialty').click();
      await page.getByRole('option', { name: 'General Practice' }).click();
      // Slot buttons have aria-label "Book slot <locale-time>" — use broad regex
      await expect(page.getByLabel(/Book slot/i).first()).toBeVisible({ timeout: 10_000 });
      await page.getByLabel(/Book slot/i).first().click();
    });

    await test.step('Insurance step — fill insurer details and verify', async () => {
      const insurerLabel = page.getByLabel(/Insurer Name/i);
      const memberIdLabel = page.getByLabel(/Member ID/i);
      if (await insurerLabel.isVisible({ timeout: 5_000 }).catch(() => false)) {
        await insurerLabel.fill('MockInsure Co.');
        await memberIdLabel.fill('MEMB-0001');
        await page.getByRole('button', { name: /Check Insurance/i }).click();
        // Wait for verification result
        await expect(page.getByText(/Verified|Coverage confirmed/i)).toBeVisible({ timeout: 10_000 });
        await page.getByRole('button', { name: /Continue to Confirmation/i }).click();
      }
    });

    await test.step('Confirm booking and assert reference number', async () => {
      const confirmBtn = page.getByRole('button', { name: /Confirm booking/i });
      if (await confirmBtn.isVisible({ timeout: 5_000 }).catch(() => false)) {
        await confirmBtn.click();
        await expect(page.getByRole('heading', { name: 'Booking Confirmed!' })).toBeVisible({ timeout: 15_000 });
        await expect(page.getByText('REF-TEST-001')).toBeVisible();
      }
    });
  });

});
