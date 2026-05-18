/**
 * E2E Journey: Staff Walk-In & Clinical Workflow
 * Journey: UC-005 → UC-006 → UC-009
 * Phases:
 *   Phase 1: Staff logs in, creates walk-in booking for patient (UC-005)
 *   Phase 2: Staff marks patient Arrived in the same-day queue (UC-006)
 *   Phase 3: Staff reviews medical coding and confirms ICD-10/CPT codes (UC-009)
 *
 * All backend API calls are mocked via page.route() so the test runs without
 * a live backend. Auth state is in-memory only (Angular signals, no localStorage),
 * so page.goto() to protected routes re-triggers login — Phase 3 re-authenticates
 * and uses Angular Router navigation (via page.evaluate) to preserve auth state,
 * matching the pattern established in patient_onboarding.spec.ts.
 */
import { test, expect } from '@playwright/test';
import { LoginPage } from '../pages/login.page';
import { WalkInPage } from '../pages/walk-in.page';
import { QueuePage } from '../pages/queue.page';
import { MedicalCodingPage } from '../pages/medical-coding.page';
import journeyData from '../data/staff_walkin_e2e.json';

const APPT_ID = 'walkin-appt-001';

/** Register all API mocks needed for the full staff walk-in journey. */
async function mockAllApis(page: import('@playwright/test').Page, patientName: string) {
  // ── Auth ──────────────────────────────────────────────────────────────────
  await page.route('**/api/auth/login**', route =>
    route.fulfill({
      status: 200,
      json: {
        accessToken: 'mock-staff-access-token',
        refreshToken: 'mock-staff-refresh-token',
        expiresIn: 3600,
        userId: 'staff-e2e-001',
        role: 'Staff',
        deviceId: 'staff-device-e2e',
        name: 'Staff E2E',
      },
    }),
  );

  // ── Specialties (walk-in confirm form dropdown) ───────────────────────────
  await page.route('**/api/appointments/specialties**', route =>
    route.fulfill({
      status: 200,
      json: [{ id: 'spec-001', name: 'General Practice' }],
    }),
  );

  // ── Patient search ────────────────────────────────────────────────────────
  await page.route('**/api/staff/patients/search**', route =>
    route.fulfill({
      status: 200,
      json: [
        {
          patientId: 'walkin-patient-e2e-id',
          name: patientName,
          email: 'e2e.walkin.patient@propeliq.dev',
          dateOfBirth: '1985-03-15',
        },
      ],
    }),
  );

  // ── Create walk-in booking ────────────────────────────────────────────────
  await page.route('**/api/staff/walkin**', route =>
    route.fulfill({
      status: 201,
      json: {
        appointmentId: APPT_ID,
        referenceNumber: 'WALKIN-E2E-001',
        patientName,
        date: new Date().toISOString().split('T')[0],
        status: 'Booked',
        bookingType: 'WalkIn',
      },
    }),
  );

  // ── Same-day queue ────────────────────────────────────────────────────────
  await page.route('**/api/queue/today**', route =>
    route.fulfill({
      status: 200,
      json: [
        {
          appointmentId: APPT_ID,
          patientId: 'walkin-patient-e2e-id',
          patientName,
          queuePosition: 1,
          chiefComplaint: journeyData.patient.chiefComplaint,
          timeSlotStart: '09:00',
          waitTime: null,
          riskLevel: null,
          bookingType: 'WalkIn',
          arrivalStatus: 'Waiting',
          arrivalTimestamp: null,
        },
      ],
    }),
  );

  // ── Mark arrived ─────────────────────────────────────────────────────────
  await page.route('**/api/queue/**/arrived**', route =>
    route.fulfill({ status: 200, json: {} }),
  );

  // ── Medical code suggestions ──────────────────────────────────────────────
  await page.route('**/api/patients/**/medical-codes**', route =>
    route.fulfill({
      status: 200,
      json: {
        suggestions: [
          {
            codeId: 'icd-e2e-001',
            codeType: 'ICD10',
            code: 'J06.9',
            description: 'Acute upper respiratory infection, unspecified',
            confidenceScore: 0.92,
            evidenceText: 'Patient presents with acute respiratory symptoms.',
            lowConfidence: false,
          },
          {
            codeId: 'cpt-e2e-001',
            codeType: 'CPT',
            code: '99213',
            description: 'Office or other outpatient visit, moderate complexity',
            confidenceScore: 0.88,
            evidenceText: 'Standard outpatient consultation documented.',
            lowConfidence: false,
          },
        ],
      },
    }),
  );

  // ── Confirm codes ─────────────────────────────────────────────────────────
  await page.route('**/api/medical-codes/confirm**', route =>
    route.fulfill({ status: 200, json: { confirmed: true } }),
  );

  // ── Patient record page (navigation target after coding submit) ───────────
  // Use the specific patientId path to avoid shadowing the /search endpoint
  // (Playwright routes match LIFO — a broad wildcard would intercept search too).
  await page.route(`**/api/staff/patients/${journeyData.patient.patientId}`, route =>
    route.fulfill({ status: 200, json: { patientId: journeyData.patient.patientId, name: patientName } }),
  );
}

test.describe('E2E Journey: Staff Walk-In and Clinical Workflow (UC-005 → UC-006 → UC-009)', () => {
  test('Full walk-in clinical workflow from arrival to medical code confirmation', async ({
    page,
  }) => {
    test.setTimeout(120_000);
    const d = journeyData;
    const patientName = d.patient.name;

    await mockAllApis(page, patientName);

    // Phase 1 ──────────────────────────────────────────────────────────────

    await test.step('Phase 1: Staff authenticates', async () => {
      await page.goto('/auth/login');
      const login = new LoginPage(page);
      await login.login(d.staff.email, d.staff.password);
      // Staff role → component navigates to /staff/walkin via Angular Router
      await expect(page).toHaveURL(/staff\/walkin/, { timeout: 15_000 });
      // Sidebar renders the role name via authService.currentRole()
      await expect(login.roleBadge).toContainText('Staff', { timeout: 5_000 });
    });

    await test.step('Phase 1: Staff searches for existing patient by name', async () => {
      const walkIn = new WalkInPage(page);
      await walkIn.searchPatient(d.patient.searchName);
      await expect(walkIn.patientResult(patientName)).toBeVisible({ timeout: 10_000 });
    });

    await test.step('Phase 1: Staff selects patient and confirms walk-in booking', async () => {
      const walkIn = new WalkInPage(page);
      // createWalkIn: clicks patient option → selects specialty → clicks Confirm Walk-In
      // On success the store effect navigates automatically to /staff/queue
      await walkIn.createWalkIn(patientName, 'spec-001');
      await expect(page).toHaveURL(/staff\/queue/, { timeout: 15_000 });
    });

    // Phase 2 ──────────────────────────────────────────────────────────────

    await test.step('Phase 2: Walk-in patient appears in the same-day queue', async () => {
      const queue = new QueuePage(page);
      await expect(queue.queueEntry(patientName)).toBeVisible({ timeout: 10_000 });
    });

    await test.step('Phase 2: Queue entry shows walk-in badge', async () => {
      const queue = new QueuePage(page);
      await expect(queue.walkinBadge(patientName)).toBeVisible({ timeout: 5_000 });
    });

    await test.step('Phase 2: Chief complaint column shows correct value', async () => {
      const row = page.locator('tr.queue-row').filter({ hasText: patientName });
      await expect(row.locator('td.col-complaint')).toHaveText(d.patient.chiefComplaint, { timeout: 5_000 });
    });

    await test.step('Phase 2: Staff marks patient as Arrived', async () => {
      const queue = new QueuePage(page);
      await queue.markArrived(patientName);
    });

    await test.step('Phase 2: Queue entry updates to Arrived status', async () => {
      const queue = new QueuePage(page);
      // Optimistic update: component immediately sets arrivalStatus = 'Arrived'
      await expect(queue.queueEntry(patientName)).toContainText('Arrived', { timeout: 5_000 });
    });

    // Phase 3 ──────────────────────────────────────────────────────────────
    // Auth state is in-memory (Angular signals). Re-authenticating before
    // navigating to the medical codes page preserves auth state via SPA routing.

    await test.step('Phase 3: Staff re-authenticates for medical code review', async () => {
      await page.goto('/auth/login');
      const login = new LoginPage(page);
      await login.login(d.staff.email, d.staff.password);
      await expect(page).toHaveURL(/staff\/walkin/, { timeout: 15_000 });
    });

    await test.step('Phase 3: Staff opens medical code review for patient', async () => {
      // Use Angular Router (in-app SPA navigation) to preserve in-memory auth state.
      // page.goto() would cause a full reload and clear the signals-based auth state.
      await page.evaluate((patientId: string) => {
        const ng = (window as unknown as {
          ng?: { getComponent: (el: Element) => Record<string, unknown> | null }
        }).ng;
        if (!ng) throw new Error('Angular debug utils unavailable (run in dev mode)');
        const el = document.querySelector('app-walkin-booking');
        if (!el) throw new Error('app-walkin-booking not found');
        const comp = ng.getComponent(el);
        if (!comp) throw new Error('WalkInBookingComponent not found');
        const router = comp['router'] as { navigateByUrl: (path: string) => Promise<boolean> };
        void router.navigateByUrl(`/staff/patients/${patientId}/medical-codes`);
      }, d.patient.patientId);
      await expect(page).toHaveURL(
        new RegExp(`patients/${d.patient.patientId}/medical-codes`),
        { timeout: 10_000 },
      );
      const coding = new MedicalCodingPage(page);
      await expect(coding.heading).toBeVisible({ timeout: 15_000 });
    });

    await test.step('Phase 3: Staff verifies ICD-10 AI suggestion visible', async () => {
      const coding = new MedicalCodingPage(page);
      await expect(coding.icd10Suggestion(d.clinicalCodes.icd10.code)).toBeVisible({
        timeout: 10_000,
      });
      await expect(coding.icd10Suggestion(d.clinicalCodes.icd10.code)).toContainText(
        d.clinicalCodes.icd10.description,
      );
    });

    await test.step('Phase 3: Staff verifies CPT AI suggestion visible', async () => {
      const coding = new MedicalCodingPage(page);
      await expect(coding.cptSuggestion(d.clinicalCodes.cpt.code)).toBeVisible({
        timeout: 10_000,
      });
      await expect(coding.cptSuggestion(d.clinicalCodes.cpt.code)).toContainText(
        d.clinicalCodes.cpt.description,
      );
    });

    await test.step('Phase 3: Staff confirms ICD-10 code', async () => {
      const coding = new MedicalCodingPage(page);
      await coding.confirmCode(d.clinicalCodes.icd10.code);
      await expect(coding.confirmedBadge('icd10', d.clinicalCodes.icd10.code)).toBeVisible({
        timeout: 5_000,
      });
    });

    await test.step('Phase 3: Staff confirms CPT code', async () => {
      const coding = new MedicalCodingPage(page);
      await coding.confirmCode(d.clinicalCodes.cpt.code);
      await expect(coding.confirmedBadge('cpt', d.clinicalCodes.cpt.code)).toBeVisible({
        timeout: 5_000,
      });
    });

    await test.step('Phase 3: Staff submits review — navigates to patient record', async () => {
      const coding = new MedicalCodingPage(page);
      await coding.saveCodes();
      // After submitReview() succeeds, the component navigates to /staff/patients/:patientId
      await expect(page).toHaveURL(
        new RegExp(`staff/patients/${d.patient.patientId}`),
        { timeout: 15_000 },
      );
    });
  });
});
