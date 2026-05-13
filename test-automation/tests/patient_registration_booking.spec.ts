/**
 * Feature: Patient Registration, Booking & Intake
 * Use Cases: UC-001, UC-002, UC-003
 * Source: .propel/context/test/tw_patient_registration_booking_20260420.md
 */
import { test, expect } from '@playwright/test';
import { RegistrationPage } from '../pages/registration.page';
import { BookingPage } from '../pages/booking.page';
import { IntakePage } from '../pages/intake.page';
import {
  mockNotificationApi,
  mockAiIntakeApi,
  mockAiIntakeLowConfidence,
} from '../support/api-mocks';
import testData from '../data/patient_registration_booking.json';

// ── UC-001: Patient Registration & Appointment Booking ────────────────────

test.describe('UC-001: Patient Registration and Appointment Booking', () => {
  test('TC-UC001-HP-001: Patient registers, verifies email, books slot and receives PDF confirmation', async ({
    page,
  }) => {
    const d = testData.tc_uc001_hp_001;
    await mockNotificationApi(page);

    await test.step('Register new patient account', async () => {
      await page.goto('/register');
      const reg = new RegistrationPage(page);
      await reg.register(d.email, d.password, d.firstName, d.lastName, d.dateOfBirth, d.phone);
      await expect(reg.errorAlert).toContainText('Verification email sent');
    });

    await test.step('Verify email address', async () => {
      await page.goto(`/verify-email?token=${d.verificationToken}`);
      await expect(page.getByRole('heading', { name: 'Email verified' })).toBeVisible();
    });

    await test.step('Select appointment slot', async () => {
      await page.goto('/book');
      const booking = new BookingPage(page);
      await expect(booking.slotCard(d.slotId)).toBeVisible();
      await booking.selectSlot(d.slotId);
    });

    await test.step('Complete insurance pre-check', async () => {
      const booking = new BookingPage(page);
      await booking.verifyInsurance(d.insuranceProvider, d.memberId);
      await expect(booking.insuranceStatusBadge).toContainText('Verified');
    });

    await test.step('Confirm booking and verify PDF notification', async () => {
      const booking = new BookingPage(page);
      await booking.continueToIntakeButton.click();
      await booking.confirmBookingButton.click();
      await expect(booking.bookingReference).not.toBeEmpty();
      await expect(page.getByRole('alert')).toContainText('Confirmation email sent');
    });
  });

  test('TC-UC001-EC-001: Patient designates preferred unavailable slot during booking', async ({
    page,
  }) => {
    const d = testData.tc_uc001_ec_001;
    const booking = new BookingPage(page);

    await test.step('Select available slot', async () => {
      await page.goto('/book');
      await expect(booking.slotCard(d.availableSlotId)).toBeVisible();
      await booking.selectSlot(d.availableSlotId);
    });

    await test.step('Designate preferred unavailable slot', async () => {
      await booking.setPreferredSlotButton.click();
      await booking.slotCard(d.preferredSlotId).click();
      await expect(
        page.getByRole('button', { name: 'Notify me when this slot opens' }).or(
          page.getByTestId('preferred-slot-indicator'),
        ),
      ).toBeVisible();
    });

    await test.step('Confirm booking with preferred slot set', async () => {
      await booking.confirmBookingButton.click();
      await expect(booking.preferredSlotIndicator).toBeVisible();
    });
  });

  test('TC-UC001-ER-001: Registration with duplicate email shows error and login redirect', async ({
    page,
  }) => {
    const d = testData.tc_uc001_er_001;

    await test.step('Attempt registration with existing email', async () => {
      await page.goto('/register');
      const reg = new RegistrationPage(page);
      await reg.emailInput.fill(d.duplicateEmail);
      await reg.passwordInput.fill(d.password);
      await reg.firstNameInput.fill(d.firstName);
      await reg.lastNameInput.fill(d.lastName);
      await reg.createAccountButton.click();
    });

    await test.step('Verify error message and login link visible', async () => {
      const reg = new RegistrationPage(page);
      await expect(reg.errorAlert).toContainText('Email already registered');
      await expect(reg.loginLink).toBeVisible();
    });
  });
});

// ── UC-002: AI-Assisted Intake ─────────────────────────────────────────────

test.describe('UC-002: AI-Assisted Intake', () => {
  test('TC-UC002-HP-001: Patient completes AI intake; fields auto-populated and submitted', async ({
    page,
  }) => {
    const d = testData.tc_uc002_hp_001;
    await mockAiIntakeApi(page, d.expectedMedications, d.expectedAllergies);

    await test.step('Open AI intake interface', async () => {
      await page.goto(`/intake/${d.appointmentId}`);
      const intake = new IntakePage(page);
      await intake.aiModeButton.click();
      await expect(intake.chatLog).toBeVisible();
    });

    await test.step('Submit medication message and verify preview', async () => {
      const intake = new IntakePage(page);
      await intake.sendChatMessage(d.chatMessage);
      await expect(intake.medicationsPreview).toContainText(d.expectedMedications[0]);
    });

    await test.step('Submit allergy message and verify preview', async () => {
      const intake = new IntakePage(page);
      await intake.sendChatMessage(d.allergyMessage);
      await expect(intake.allergiesPreview).toContainText(d.expectedAllergies[0]);
    });

    await test.step('Submit intake and confirm success', async () => {
      const intake = new IntakePage(page);
      await intake.submitIntakeButton.click();
      await expect(intake.successAlert).toContainText('Intake submitted successfully');
    });
  });

  test('TC-UC002-EC-001: Switch from AI intake to manual form preserves parsed data', async ({
    page,
  }) => {
    const d = testData.tc_uc002_ec_001;
    await mockAiIntakeApi(page, [d.preParsedMedication], []);

    await test.step('Resume AI intake with prior session', async () => {
      await page.goto(`/intake/${d.appointmentId}`);
      const intake = new IntakePage(page);
      await expect(intake.medicationsPreview).toContainText(d.preParsedMedication);
    });

    await test.step('Switch to manual form and verify pre-population', async () => {
      const intake = new IntakePage(page);
      await intake.switchToManual();
      await expect(intake.medicationsInput).toHaveValue(new RegExp(d.preParsedMedication));
      await expect(intake.modeBadge).toContainText('Manual');
    });
  });

  test('TC-UC002-ER-001: AI intake NLU below confidence threshold prompts clarifying question', async ({
    page,
  }) => {
    const d = testData.tc_uc002_er_001;
    await mockAiIntakeLowConfidence(page);

    await test.step('Open AI intake and submit ambiguous message', async () => {
      await page.goto(`/intake/${d.appointmentId}`);
      const intake = new IntakePage(page);
      await intake.aiModeButton.click();
      await intake.sendChatMessage(d.ambiguousInput);
    });

    await test.step('Verify clarifying question shown and field NOT auto-populated', async () => {
      const intake = new IntakePage(page);
      await expect(intake.chatLog).toContainText('clarify');
      await expect(intake.medicationsPreview).toBeEmpty();
    });
  });
});

// ── UC-003: Manual Intake Form ─────────────────────────────────────────────

/** Log in as patient and navigate to the intake page via Angular router to preserve in-memory auth. */
async function loginAndGoToIntake(page: import('@playwright/test').Page, appointmentId: string): Promise<void> {
  await page.route('**/api/auth/login**', route =>
    route.fulfill({
      status: 200,
      json: {
        accessToken: 'mock-access-token-patient',
        refreshToken: 'mock-refresh-token-patient',
        expiresIn: 3600,
        userId: 'mock-patient-001',
        role: 'Patient',
        deviceId: 'mock-device-patient',
      },
    }),
  );
  await page.route('**/api/patient/dashboard**', route =>
    route.fulfill({ status: 200, json: { upcomingAppointments: [], documents: [], viewVerified: false } }),
  );
  await page.goto('/auth/login');
  await page.getByLabel('Email address').fill('auth.patient@propeliq.dev');
  await page.getByLabel('Password').fill('AuthP@ss001!');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(/dashboard/, { timeout: 15_000 });
  // Navigate via Angular router link to preserve in-memory auth state
  await page.evaluate((id: string) => {
    window.history.pushState({}, '', `/intake/${id}`);
    window.dispatchEvent(new PopStateEvent('popstate'));
  }, appointmentId);
  await page.waitForURL(`**/intake/${appointmentId}`, { timeout: 10_000 });
}

test.describe('UC-003: Manual Intake Form', () => {
  test('TC-UC003-HP-001: Patient completes manual intake with autosave and submits successfully', async ({
    page,
  }) => {
    const d = testData.tc_uc003_hp_001;

    // Mock the intake form load and backend calls
    await page.route('**/api/intake/form**', route =>
      route.fulfill({ status: 200, json: { appointmentId: d.appointmentId, manualDraft: null, aiExtracted: null } }),
    );
    await page.route('**/api/intake/**/draft**', route =>
      route.fulfill({ status: 200, json: { exists: false } }),
    );
    await page.route('**/api/intake/autosave**', route =>
      route.fulfill({ status: 200, json: {} }),
    );
    await page.route('**/api/intake/submit**', route =>
      route.fulfill({ status: 200, json: {} }),
    );

    await test.step('Login and open manual intake form', async () => {
      await loginAndGoToIntake(page, d.appointmentId);
      const intake = new IntakePage(page);
      await intake.manualModeButton.click();
      await expect(intake.addMedicationButton).toBeVisible({ timeout: 10_000 });
    });

    await test.step('Fill required fields and trigger autosave', async () => {
      const intake = new IntakePage(page);
      await intake.fillManualIntake(d.medications, d.allergies, d.symptoms, d.medicalHistory);
      await intake.medHistoryInput.blur();
      await expect(intake.autosaveIndicator).toContainText('Saved', { timeout: 10_000 });
    });

    await test.step('Submit intake and confirm redirect to dashboard', async () => {
      const intake = new IntakePage(page);
      await intake.submitIntakeButton.click();
      await expect(page).toHaveURL(/dashboard/, { timeout: 15_000 });
    });
  });

  test('TC-UC003-EC-001: Manual form pre-populated from prior AI intake session', async ({
    page,
  }) => {
    const d = testData.tc_uc003_ec_001;

    // Return aiExtracted data so the manual form pre-populates from the prior AI session
    await page.route('**/api/intake/form**', route =>
      route.fulfill({
        status: 200,
        json: {
          appointmentId: d.appointmentId,
          manualDraft: null,
          aiExtracted: {
            demographics: { firstName: '', lastName: '', dateOfBirth: '', gender: '', phone: '', street: '', city: '', postalCode: '', emergencyContactName: '', emergencyContactPhone: '' },
            medicalHistory: { conditions: [], allergies: [], surgeries: [], familyHistory: '' },
            symptoms: [],
            medications: [{ name: d.aiParsedMedication, dosage: '', frequency: '', isOtcSupplement: false }],
          },
        },
      }),
    );
    await page.route('**/api/intake/**/draft**', route =>
      route.fulfill({ status: 200, json: { exists: false } }),
    );

    await test.step('Login and open intake page with prior AI session', async () => {
      await loginAndGoToIntake(page, d.appointmentId);
    });

    await test.step('Switch to manual form and verify AI data pre-populated', async () => {
      const intake = new IntakePage(page);
      await intake.switchToManual();
      // After AI→Manual switch, medications are pre-populated from aiExtracted.
      // The medication name input uses aria-label 'Medication name 1'.
      await expect(intake.medicationsInput).toBeVisible({ timeout: 10_000 });
      const value = await intake.medicationsInput.inputValue();
      expect(value).toMatch(new RegExp(d.aiParsedMedication));
    });
  });

  test('TC-UC003-ER-001: Manual intake submission blocked when required fields empty', async ({
    page,
  }) => {
    const d = testData.tc_uc003_er_001;

    await page.route('**/api/intake/form**', route =>
      route.fulfill({ status: 200, json: { appointmentId: d.appointmentId, manualDraft: null, aiExtracted: null } }),
    );
    await page.route('**/api/intake/**/draft**', route =>
      route.fulfill({ status: 200, json: { exists: false } }),
    );

    await test.step('Login and open manual intake form without filling fields', async () => {
      await loginAndGoToIntake(page, d.appointmentId);
      const intake = new IntakePage(page);
      await intake.manualModeButton.click();
      await expect(intake.addMedicationButton).toBeVisible({ timeout: 10_000 });
    });

    await test.step('Attempt submission and verify validation errors', async () => {
      const intake = new IntakePage(page);
      await intake.submitIntakeButton.click();
      await expect(intake.successAlert).toContainText('Please complete the following required fields', { timeout: 5_000 });
    });
  });
});
