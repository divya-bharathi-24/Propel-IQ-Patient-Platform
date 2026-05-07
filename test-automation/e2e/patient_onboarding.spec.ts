/**
 * E2E Journey: Patient Onboarding
 * Journey: UC-001 → UC-002 → UC-007 → UC-008
 * Phases:
 *   Phase 1: Patient registers, verifies email, books slot (UC-001)
 *   Phase 2: Patient completes AI intake (UC-002)
 *   Phase 3: Patient uploads clinical documents (UC-007)
 *   Phase 4: Staff opens 360-degree view, resolves conflicts, verifies profile (UC-008)
 * Source: .propel/context/test/e2e_patient_onboarding_20260420.md
 */
import { test, expect } from '@playwright/test';
import { RegistrationPage } from '../pages/registration.page';
import { BookingPage } from '../pages/booking.page';
import { IntakePage } from '../pages/intake.page';
import { DocumentUploadPage } from '../pages/document-upload.page';
import { ThreeSixtyViewPage } from '../pages/three-sixty-view.page';
import { LoginPage } from '../pages/login.page';
import {
  mockNotificationApi,
  mockAiIntakeApi,
  mockDocumentUploadApi,
  mockProfileVerifyBlocked,
  mockProfileVerifySuccess,
} from '../support/api-mocks';
import journeyData from '../data/patient_onboarding_e2e.json';

/** Mock all backend APIs needed for the full E2E onboarding journey. */
async function mockAllE2EApis(page: import('@playwright/test').Page, d: typeof journeyData) {
  // Slots
  await page.route('**/api/appointments/specialties**', route =>
    route.fulfill({ status: 200, json: [{ id: 'spec-001', name: 'General Practice' }] }),
  );
  await page.route('**/api/appointments/slots**', route =>
    route.fulfill({
      status: 200,
      json: [{
        id: d.appointment.slotId,
        timeSlotStart: `${d.appointment.slotDate}T${d.appointment.slotTime}:00.000Z`,
        timeSlotEnd:   `${d.appointment.slotDate}T10:30:00.000Z`,
        isAvailable: true,
        specialtyId: 'spec-001',
        date: d.appointment.slotDate,
      }],
    }),
  );
  // Insurance
  await page.route('**/api/insurance/pre-check**', route =>
    route.fulfill({ status: 200, json: { status: 'Verified', guidance: 'Coverage confirmed.' } }),
  );
  // Hold slot
  await page.route('**/api/appointments/hold-slot**', route =>
    route.fulfill({ status: 200, json: { held: true } }),
  );
  // Book appointment
  await page.route('**/api/appointments/book**', route =>
    route.fulfill({
      status: 200,
      json: {
        referenceNumber: 'REF-E2E-001',
        timeSlotStart: d.appointment.slotTime,
        specialtyName: 'General Practice',
        date: d.appointment.slotDate,
      },
    }),
  );
  // Generic intake fallback — registered FIRST so specific routes below take LIFO priority
  await page.route('**/api/intake**', route =>
    route.fulfill({ status: 200, json: { submitted: true, message: 'Intake submitted successfully' } }),
  );
  // Intake record fetch — return 404 so AI starts fresh
  await page.route('**/api/intake/e2e-001**', route =>
    route.fulfill({ status: 404, json: { error: 'Not found' } }),
  );
  // AI intake session start
  await page.route('**/api/intake/ai/session**', route =>
    route.fulfill({ status: 200, json: { sessionId: 'mock-session-001', openingQuestion: 'What medications are you currently taking?' } }),
  );
  // AI intake message → returns extracted fields as ExtractedField[]
  await page.route('**/api/intake/ai/message**', route =>
    route.fulfill({
      status: 200,
      json: {
        aiResponse: 'Thank you. I can see you take Lisinopril 10mg and have a sulfa drug allergy.',
        isSessionComplete: true,
        isFallback: false,
        extractedFields: [
          { fieldName: 'medications', value: d.intake.expectedMedications[0], confidence: 0.95 },
          { fieldName: 'allergies', value: d.intake.expectedAllergies[0], confidence: 0.95 },
        ],
      },
    }),
  );
  // AI intake submit
  await page.route('**/api/intake/ai/submit**', route =>
    route.fulfill({ status: 200, json: { submitted: true, message: 'Intake submitted successfully' } }),
  );
  // Dashboard
  await page.route('**/api/patient/dashboard**', route =>
    route.fulfill({
      status: 200,
      json: { upcomingAppointments: [], documents: [], viewVerified: false },
    }),
  );
  // Patient 360 view — must match Patient360ViewDto shape
  await page.route(`**/api/staff/patients/${d.patientId}/360-view**`, route =>
    route.fulfill({
      status: 200,
      json: {
        patientId: d.patientId,
        verificationStatus: 'Unverified',
        unresolvedCriticalConflicts: [{ fieldName: d.conflict.field, reason: 'Values differ across documents' }],
        conflicts: [{
          conflictId: 'conflict-001',
          fieldName: d.conflict.field,
          severity: 'Critical',
          resolutionStatus: 'Unresolved',
          value1: d.conflict.value1,
          sourceDoc1: d.conflict.source1,
          value2: d.conflict.value2,
          sourceDoc2: d.conflict.source2,
        }],
        documents: [],
        sections: [],
      },
    }),
  );
  // Conflict resolve — optimistic update in store; mock ensures no revert
  await page.route('**/api/conflicts/**', route =>
    route.fulfill({ status: 200, json: {} }),
  );
}

test.describe('E2E Journey: Patient Onboarding (UC-001 → UC-002 → UC-007 → UC-008)', () => {
  test('Full patient onboarding from registration to verified profile', async ({ page }) => {
    test.setTimeout(120_000);
    const d = journeyData;
    await mockAllE2EApis(page, d);

    // Phase 1 ──────────────────────────────────────────────────────────────

    await test.step('Phase 1: Patient registers account', async () => {
      await mockNotificationApi(page);
      await page.route('**/api/auth/register**', route =>
        route.fulfill({ status: 201, json: { message: 'Verification email sent', emailVerified: false } }),
      );
      await page.goto('/auth/register');
      const reg = new RegistrationPage(page);
      await reg.register(
        d.patient.email,
        d.patient.password,
        d.patient.firstName,
        d.patient.lastName,
        d.patient.dateOfBirth,
        d.patient.phone,
      );
      await expect(page.getByRole('heading', { name: 'Check Your Email' })).toBeVisible({ timeout: 15_000 });
    });

    await test.step('Phase 1: Patient verifies email address', async () => {
      await page.route('**/api/auth/verify**', route =>
        route.fulfill({ status: 200, json: { emailVerified: true } }),
      );
      await page.goto(`/auth/verify?token=${d.patient.verificationToken}`);
      await expect(page.getByRole('heading', { name: 'Email Verified!' })).toBeVisible({ timeout: 15_000 });
    });

    await test.step('Phase 1: Patient logs in after email verification', async () => {
      await page.route('**/api/auth/login**', route =>
        route.fulfill({
          status: 200,
          json: {
            accessToken: 'mock-access-token-patient',
            refreshToken: 'mock-refresh-token-patient',
            expiresIn: 3600,
            userId: d.patientId,
            role: 'Patient',
            deviceId: 'mock-device-patient',
          },
        }),
      );
      await page.route('**/api/patient/dashboard**', route =>
        route.fulfill({
          status: 200,
          json: { upcomingAppointments: [], documents: [], viewVerified: false },
        }),
      );
      await page.goto('/auth/login');
      const login = new LoginPage(page);
      await login.login(d.patient.email, d.patient.password);
      await expect(page).toHaveURL(/dashboard/, { timeout: 15_000 });
    });

    await test.step('Phase 1: Patient selects appointment slot', async () => {
      // Navigate via SPA link to preserve in-memory auth state (page.goto() would reload and clear it)
      await expect(page.getByRole('link', { name: 'Book a new appointment' })).toBeVisible({ timeout: 10_000 });
      await page.getByRole('link', { name: 'Book a new appointment' }).click();
      await expect(page).toHaveURL(/appointments\/book/, { timeout: 10_000 });
      // Select specialty
      await expect(page.getByLabel('Appointment specialty')).toBeVisible({ timeout: 15_000 });
      await page.getByLabel('Appointment specialty').click();
      await page.getByRole('option', { name: 'General Practice' }).click();
      // Select the available slot by aria-label (slots are gridcells, not buttons)
      await expect(page.getByLabel(/Book slot/i).first()).toBeVisible({ timeout: 10_000 });
      await page.getByLabel(/Book slot/i).first().click();
    });

    await test.step('Phase 1: Patient completes insurance verification', async () => {
      // Step 2: Skip "Preferred Slot" — wait for it then click
      await expect(page.getByRole('button', { name: /Skip preferred slot designation/i })).toBeVisible({ timeout: 10_000 });
      await page.getByRole('button', { name: /Skip preferred slot designation/i }).click();

      // Step 3: Intake Mode — wait for Continue button, select AI radio, then continue
      await expect(page.getByRole('button', { name: /Continue to insurance step/i })).toBeVisible({ timeout: 10_000 });
      await page.getByLabel('AI-Assisted intake mode').click();
      await page.getByRole('button', { name: /Continue to insurance step/i }).click();

      // Step 4: Insurance — wait for form to appear, fill and verify
      await expect(page.getByLabel('Insurer name')).toBeVisible({ timeout: 10_000 });
      const booking = new BookingPage(page);
      await booking.verifyInsurance(
        d.appointment.insurance.provider,
        d.appointment.insurance.memberId,
      );
      await expect(
        page.getByTestId('insurance-status').or(page.getByText('Verified').first())
      ).toBeVisible({ timeout: 10_000 });
    });

    await test.step('Phase 1: Patient confirms booking', async () => {
      // Click "Continue to confirmation step" from insurance step
      await expect(page.getByRole('button', { name: /Continue to confirmation step/i })).toBeVisible({ timeout: 10_000 });
      await page.getByRole('button', { name: /Continue to confirmation step/i }).click();
      // Booking auto-confirms when the confirmation step loads
      await expect(page.getByRole('heading', { name: 'Booking Confirmed!' })).toBeVisible({ timeout: 15_000 });
      // Verify reference number is shown
      await expect(page.locator('.reference-number').or(page.getByText(/REF-/i).first())).toBeVisible({ timeout: 10_000 });
    });

    // Phase 2 ──────────────────────────────────────────────────────────────

    await test.step('Phase 2: Patient opens AI intake', async () => {
      // Re-login and navigate via "Complete Intake" SPA link (keeps auth state, properly sets route params)
      await page.route('**/api/patient/dashboard**', route =>
        route.fulfill({
          status: 200,
          json: {
            upcomingAppointments: [{
              id: d.appointment.slotId,
              date: d.appointment.slotDate,
              timeSlotStart: `${d.appointment.slotDate}T${d.appointment.slotTime}:00.000Z`,
              specialty: 'General Practice',
              status: 'Booked',
              hasPendingIntake: true,
              hasSubmittedIntake: false,
            }],
            documents: [],
            viewVerified: false,
          },
        }),
      );
      await page.goto('/auth/login');
      const loginForIntake = new LoginPage(page);
      await loginForIntake.login(d.patient.email, d.patient.password);
      await expect(page).toHaveURL(/dashboard/, { timeout: 15_000 });
      // Click "Complete Intake" router link (SPA navigation — keeps auth and sets route params)
      await expect(page.getByRole('link', { name: /Complete intake form/i }).first()).toBeVisible({ timeout: 10_000 });
      await page.getByRole('link', { name: /Complete intake form/i }).first().click();
      await expect(page).toHaveURL(new RegExp(`intake/${d.appointment.slotId}`), { timeout: 10_000 });
      const intake = new IntakePage(page);
      await expect(intake.chatLog).toBeVisible({ timeout: 15_000 });
      // Wait for opening question to confirm session started
      await expect(page.getByRole('log', { name: /Conversation messages/i })).not.toBeEmpty({ timeout: 10_000 });
    });

    await test.step('Phase 2: Patient sends medication message', async () => {
      const intake = new IntakePage(page);
      await intake.sendChatMessage(d.intake.chatMessage);
      // Once the AI session completes (isSessionComplete: true), the preview panel
      // switches to editMode and values are rendered inside <input> elements.
      // toHaveValue() reads the input's .value property, not innerText.
      await expect(intake.medicationsValueField).toHaveValue(d.intake.expectedMedications[0], { timeout: 10_000 });
    });

    await test.step('Phase 2: Patient submits AI intake', async () => {
      const intake = new IntakePage(page);
      await expect(intake.submitIntakeButton).toBeVisible({ timeout: 10_000 });
      await intake.submitIntakeButton.click();
      // After submission, Angular navigates to /appointments
      await expect(page).toHaveURL(/appointments/, { timeout: 15_000 });
    });

    // Phase 3 ──────────────────────────────────────────────────────────────

    await test.step('Phase 3: Patient navigates to document upload', async () => {
      await mockDocumentUploadApi(page, d.documents.length);
      // Re-login to restore auth state
      await page.goto('/auth/login');
      const loginForDocs = new LoginPage(page);
      await loginForDocs.login(d.patient.email, d.patient.password);
      await expect(page).toHaveURL(/dashboard/, { timeout: 15_000 });
      // Click the "Upload Documents" SPA link on the dashboard (keeps auth state
      // and properly activates Angular's router + authGuard for /documents)
      await expect(page.getByRole('link', { name: 'Upload medical documents' })).toBeVisible({ timeout: 10_000 });
      await page.getByRole('link', { name: 'Upload medical documents' }).click();
      await expect(page).toHaveURL(/documents/, { timeout: 10_000 });
    });

    await test.step('Phase 3: Patient uploads clinical PDFs', async () => {
      const uploader = new DocumentUploadPage(page);
      await uploader.uploadPdfs(d.documents.map((doc) => doc.name));
      await expect(uploader.successBanner).toContainText('documents uploaded successfully');
    });

    await test.step('Phase 3: Verify documents appear in history', async () => {
      const uploader = new DocumentUploadPage(page);
      await expect(uploader.documentHistory).toBeVisible();
    });

    // Phase 4 ──────────────────────────────────────────────────────────────

    await test.step('Phase 4: Staff logs in to review patient 360 view', async () => {
      // Mock login for staff role
      await page.route('**/api/auth/login**', route =>
        route.fulfill({
          status: 200,
          json: {
            accessToken: 'mock-access-token-staff',
            refreshToken: 'mock-refresh-token-staff',
            expiresIn: 3600,
            userId: 'mock-staff-001',
            role: 'Staff',
            deviceId: 'mock-device-staff',
          },
        }),
      );
      // Also mock staff dashboard
      await page.route('**/api/staff/dashboard**', route =>
        route.fulfill({ status: 200, json: { patients: [], pendingAlerts: 0 } }),
      );
      await page.goto('/auth/login');
      const login = new LoginPage(page);
      await login.login(d.staff.email, d.staff.password);
      // Wait for redirect to staff area after login
      await expect(page).toHaveURL(/walkin|dashboard|staff/, { timeout: 15_000 });
    });

    await test.step('Phase 4: Staff opens patient 360-degree view', async () => {
      await mockProfileVerifyBlocked(page);
      // Auth is in-memory only (never written to localStorage), so page.goto() would
      // trigger a full reload, destroy auth state, and cause authGuard to redirect to login.
      // Instead, trigger an in-app Angular Router navigation from the walkin component's
      // injected Router service — this preserves the in-memory auth state.
      await page.evaluate((patientId: string) => {
        const ng = (window as unknown as { ng?: { getComponent: (el: Element) => Record<string, unknown> | null } }).ng;
        if (!ng) throw new Error('Angular debug utilities not available (app must run in dev mode)');
        const walkinEl = document.querySelector('app-walkin-booking');
        if (!walkinEl) throw new Error('app-walkin-booking element not found on page');
        const comp = ng.getComponent(walkinEl);
        if (!comp) throw new Error('WalkInBookingComponent instance not found');
        const router = comp['router'] as { navigateByUrl: (path: string) => void } | undefined;
        if (!router) throw new Error('Router not found on WalkInBookingComponent');
        void router.navigateByUrl(`/staff/patients/${patientId}/360-view`);
      }, d.patientId);
      await expect(page).toHaveURL(new RegExp(`patients/${d.patientId}`), { timeout: 10_000 });
      const view = new ThreeSixtyViewPage(page);
      await expect(view.heading).toBeVisible();
    });

    await test.step('Phase 4: Staff sees medication conflict indicator', async () => {
      const view = new ThreeSixtyViewPage(page);
      await expect(view.conflictIndicator(d.conflict.field)).toBeVisible();
    });

    await test.step('Phase 4: Verification blocked until conflict resolved', async () => {
      const view = new ThreeSixtyViewPage(page);
      await view.verifyProfile();
      await expect(view.errorAlert).toContainText('Resolve all conflicts before verifying');
    });

    await test.step('Phase 4: Staff opens conflict and views both source values', async () => {
      const view = new ThreeSixtyViewPage(page);
      await view.conflictIndicator(d.conflict.field).click();
      await expect(view.conflictValue(1)).toContainText(d.conflict.value1);
      await expect(view.conflictValue(2)).toContainText(d.conflict.value2);
    });

    await test.step('Phase 4: Staff selects authoritative value', async () => {
      const view = new ThreeSixtyViewPage(page);
      await view.selectConflictValueButton(d.conflict.authoritativeValue).click();
      await expect(view.conflictIndicator(d.conflict.field)).toBeHidden();
    });

    await test.step('Phase 4: Staff verifies profile successfully', async () => {
      const view = new ThreeSixtyViewPage(page);
      await mockProfileVerifySuccess(page);
      await view.verifyProfile();
      await expect(view.profileStatusBadge).toContainText('Verified');
    });
  });
});
