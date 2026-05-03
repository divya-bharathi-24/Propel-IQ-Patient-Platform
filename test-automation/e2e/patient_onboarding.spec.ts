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

test.describe('E2E Journey: Patient Onboarding (UC-001 → UC-002 → UC-007 → UC-008)', () => {
  test('Full patient onboarding from registration to verified profile', async ({ page }) => {
    const d = journeyData;

    // Phase 1 ──────────────────────────────────────────────────────────────

    await test.step('Phase 1: Patient registers account', async () => {
      await mockNotificationApi(page);
      await page.goto('/register');
      const reg = new RegistrationPage(page);
      await reg.register(
        d.patient.email,
        d.patient.password,
        d.patient.firstName,
        d.patient.lastName,
        d.patient.dateOfBirth,
        d.patient.phone,
      );
      await expect(reg.errorAlert).toContainText('Verification email sent');
    });

    await test.step('Phase 1: Patient verifies email address', async () => {
      await page.goto(`/verify-email?token=${d.patient.verificationToken}`);
      await expect(page.getByRole('heading', { name: 'Email verified' })).toBeVisible();
    });

    await test.step('Phase 1: Patient selects appointment slot', async () => {
      await page.goto('/book');
      const booking = new BookingPage(page);
      await expect(booking.slotCard(d.appointment.slotId)).toBeVisible();
      await booking.selectSlot(d.appointment.slotId);
    });

    await test.step('Phase 1: Patient completes insurance verification', async () => {
      const booking = new BookingPage(page);
      await booking.verifyInsurance(
        d.appointment.insurance.provider,
        d.appointment.insurance.memberId,
      );
      await expect(booking.insuranceStatusBadge).toContainText('Verified');
    });

    await test.step('Phase 1: Patient confirms booking', async () => {
      const booking = new BookingPage(page);
      await booking.continueToIntakeButton.click();
      await booking.confirmBookingButton.click();
      await expect(booking.bookingReference).not.toBeEmpty();
      await expect(page.getByRole('alert')).toContainText('Confirmation email sent');
    });

    // Phase 2 ──────────────────────────────────────────────────────────────

    await test.step('Phase 2: Patient opens AI intake', async () => {
      await mockAiIntakeApi(
        page,
        d.intake.expectedMedications,
        d.intake.expectedAllergies,
      );
      await page.goto(`/intake/${d.appointment.slotId}`);
      const intake = new IntakePage(page);
      await intake.aiModeButton.click();
      await expect(intake.chatLog).toBeVisible();
    });

    await test.step('Phase 2: Patient sends medication message', async () => {
      const intake = new IntakePage(page);
      await intake.sendChatMessage(d.intake.chatMessage);
      await expect(intake.medicationsPreview).toContainText(d.intake.expectedMedications[0]);
    });

    await test.step('Phase 2: Patient submits AI intake', async () => {
      const intake = new IntakePage(page);
      await intake.submitIntakeButton.click();
      await expect(intake.successAlert).toContainText('Intake submitted successfully');
    });

    // Phase 3 ──────────────────────────────────────────────────────────────

    await test.step('Phase 3: Patient navigates to document upload', async () => {
      await mockDocumentUploadApi(page, d.documents.length);
      await page.goto('/dashboard/documents');
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
      // Simulate staff session — in real test-run, staff storageState is used;
      // here we perform an explicit login to drive the journey from a fresh session
      await page.goto('/login');
      const login = new LoginPage(page);
      await login.login(d.staff.email, d.staff.password);
      await expect(login.roleBadge).toContainText('Staff');
    });

    await test.step('Phase 4: Staff opens patient 360-degree view', async () => {
      await mockProfileVerifyBlocked(page);
      await page.goto(`/staff/patients/${d.patientId}/360`);
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
