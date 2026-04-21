/**
 * E2E Journey: Staff Walk-In & Clinical Workflow
 * Journey: UC-005 → UC-006 → UC-009
 * Phases:
 *   Phase 1: Staff creates walk-in booking for patient (UC-005)
 *   Phase 2: Staff marks patient Arrived in queue (UC-006)
 *   Phase 3: Staff reviews medical coding and confirms ICD-10/CPT codes (UC-009)
 * Source: .propel/context/test/e2e_staff_walkin_clinical_workflow_20260420.md
 */
import { test, expect } from '@playwright/test';
import { LoginPage } from '../pages/login.page';
import { WalkInPage } from '../pages/walk-in.page';
import { QueuePage } from '../pages/queue.page';
import { MedicalCodingPage } from '../pages/medical-coding.page';
import journeyData from '../data/staff_walkin_e2e.json';

test.describe('E2E Journey: Staff Walk-In and Clinical Workflow (UC-005 → UC-006 → UC-009)', () => {
  test('Full walk-in clinical workflow from arrival to medical code confirmation', async ({
    page,
  }) => {
    const d = journeyData;

    // Phase 1 ──────────────────────────────────────────────────────────────

    await test.step('Phase 1: Staff authenticates', async () => {
      await page.goto('/login');
      const login = new LoginPage(page);
      await login.login(d.staff.email, d.staff.password);
      await expect(login.roleBadge).toContainText('Staff');
    });

    await test.step('Phase 1: Staff navigates to walk-in registration', async () => {
      await page.goto('/staff/walkin');
    });

    await test.step('Phase 1: Staff searches for existing patient by name', async () => {
      const walkIn = new WalkInPage(page);
      await walkIn.searchPatient(d.patient.searchName);
      await expect(walkIn.patientResult(d.patient.patientRef)).toBeVisible();
    });

    await test.step('Phase 1: Staff selects patient and assigns walk-in slot', async () => {
      const walkIn = new WalkInPage(page);
      await walkIn.createWalkIn(d.patient.patientRef, d.slot.id);
      await expect(walkIn.successAlert).toContainText('Walk-in booking confirmed');
    });

    // Phase 2 ──────────────────────────────────────────────────────────────

    await test.step('Phase 2: Staff navigates to same-day queue', async () => {
      await page.goto('/staff/queue');
      const queue = new QueuePage(page);
      await expect(queue.queueEntry(d.patient.patientRef)).toBeVisible();
    });

    await test.step('Phase 2: Queue entry shows walk-in badge', async () => {
      const queue = new QueuePage(page);
      await expect(queue.walkinBadge(d.patient.patientRef)).toBeVisible();
    });

    await test.step('Phase 2: Staff marks patient as Arrived', async () => {
      const queue = new QueuePage(page);
      await queue.markArrived(d.patient.patientRef);
    });

    await test.step('Phase 2: Queue entry updates to Arrived with timestamp', async () => {
      const queue = new QueuePage(page);
      await expect(queue.queueEntry(d.patient.patientRef)).toContainText('Arrived');
      await expect(queue.arrivalTime(d.patient.patientRef)).toBeVisible();
    });

    // Phase 3 ──────────────────────────────────────────────────────────────

    await test.step('Phase 3: Staff opens medical code review for patient', async () => {
      await page.goto(`/staff/patients/${d.patient.patientId}/codes`);
      const coding = new MedicalCodingPage(page);
      await expect(coding.heading).toBeVisible();
    });

    await test.step('Phase 3: Staff verifies ICD-10 AI suggestion visible', async () => {
      const coding = new MedicalCodingPage(page);
      await expect(coding.icd10Suggestion(d.clinicalCodes.icd10.code)).toBeVisible();
      await expect(coding.icd10Suggestion(d.clinicalCodes.icd10.code)).toContainText(
        d.clinicalCodes.icd10.description,
      );
    });

    await test.step('Phase 3: Staff verifies CPT AI suggestion visible', async () => {
      const coding = new MedicalCodingPage(page);
      await expect(coding.cptSuggestion(d.clinicalCodes.cpt.code)).toBeVisible();
      await expect(coding.cptSuggestion(d.clinicalCodes.cpt.code)).toContainText(
        d.clinicalCodes.cpt.description,
      );
    });

    await test.step('Phase 3: Staff confirms ICD-10 code', async () => {
      const coding = new MedicalCodingPage(page);
      await coding.confirmCode(d.clinicalCodes.icd10.code);
      await expect(coding.confirmedBadge('icd10', d.clinicalCodes.icd10.code)).toBeVisible();
    });

    await test.step('Phase 3: Staff confirms CPT code', async () => {
      const coding = new MedicalCodingPage(page);
      await coding.confirmCode(d.clinicalCodes.cpt.code);
      await expect(coding.confirmedBadge('cpt', d.clinicalCodes.cpt.code)).toBeVisible();
    });

    await test.step('Phase 3: Staff saves confirmed codes and sees Coding Complete', async () => {
      const coding = new MedicalCodingPage(page);
      await coding.saveCodes();
      await expect(coding.successAlert).toContainText('Medical codes confirmed and saved');
      await expect(coding.codingCompleteBadge).toBeVisible();
    });
  });
});
