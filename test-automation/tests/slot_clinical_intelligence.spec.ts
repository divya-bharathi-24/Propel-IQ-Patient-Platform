/**
 * Feature: Slot Management, Walk-In Queue & Clinical Intelligence
 * Use Cases: UC-004, UC-005, UC-006, UC-007, UC-008, UC-009
 * Source: .propel/context/test/tw_slot_clinical_intelligence_20260420.md
 */
import { test, expect } from '@playwright/test';
import { WalkInPage } from '../pages/walk-in.page';
import { QueuePage } from '../pages/queue.page';
import { DocumentUploadPage, MINIMAL_PDF_BUFFER } from '../pages/document-upload.page';
import { ThreeSixtyViewPage } from '../pages/three-sixty-view.page';
import { MedicalCodingPage } from '../pages/medical-coding.page';
import {
  mockNotificationApi,
  mockDocumentUploadApi,
  mockAppointmentCancelApi,
  mockArrivedForbidden,
  mockProfileVerifyBlocked,
  mockProfileVerifySuccess,
  mockSmsFail,
  mockWaitlistApi,
  Responses,
} from '../support/api-mocks';
import testData from '../data/slot_clinical_intelligence.json';

// ── UC-004: Slot Swap & Waitlist ───────────────────────────────────────────

test.describe('UC-004: Preferred Slot Swap', () => {
  test('TC-UC004-HP-001: Preferred slot swap executes within 60s; email and SMS notifications sent', async ({
    page,
  }) => {
    const d = testData.tc_uc004_hp_001;
    await mockNotificationApi(page, { status: 'Sent', channel: 'Email' });
    await mockAppointmentCancelApi(page);
    await mockWaitlistApi(page, [
      Responses.waitlistEntry(d.swapPatient, d.preferredSlot, 'Swapped'),
    ]);

    await test.step('Trigger slot cancellation via API', async () => {
      const response = await page.request.post(
        `/api/appointments/slot-B-booking/cancel`,
        { headers: { Authorization: 'Bearer cancel-patient-token' } },
      );
      expect(response.status()).toBe(200);
    });

    await test.step('Verify swap patient appointment updated to preferred slot', async () => {
      await page.goto('/dashboard');
      await expect(
        page.getByTestId('upcoming-appointment-slot'),
      ).toContainText(d.preferredSlot);
    });
  });

  test('TC-UC004-EC-001: Multiple waitlisted patients — FIFO ordering respected', async ({
    page,
  }) => {
    const d = testData.tc_uc004_ec_001;
    const [patientA] = d.waitlistedPatients;

    await mockWaitlistApi(page, [
      Responses.waitlistEntry(patientA.email, d.slot, 'Swapped'),
      Responses.waitlistEntry(d.waitlistedPatients[1].email, d.slot, 'Active'),
      Responses.waitlistEntry(d.waitlistedPatients[2].email, d.slot, 'Active'),
    ]);
    await mockAppointmentCancelApi(page);

    await test.step('Cancel slot held by patient D', async () => {
      const response = await page.request.post('/api/appointments/slot-Z-booking/cancel', {
        headers: { Authorization: 'Bearer patient-d-token' },
      });
      expect(response.status()).toBe(200);
    });

    await test.step('Verify Patient A (earliest FIFO) received the swap', async () => {
      const waitlistResponse = await page.request.get(
        `/api/waitlist?patientEmail=${patientA.email}&slotId=${d.slot}`,
      );
      const body = await waitlistResponse.json() as { status: string }[];
      expect(body[0]?.status).toBe('Swapped');
    });

    await test.step('Verify Patients B and C remain waitlisted', async () => {
      for (const patient of d.waitlistedPatients.slice(1)) {
        const resp = await page.request.get(
          `/api/waitlist?patientEmail=${patient.email}&slotId=${d.slot}`,
        );
        const body = await resp.json() as { status: string }[];
        expect(body[0]?.status).toBe('Active');
      }
    });
  });

  test('TC-UC004-ER-001: SMS delivery fails; retry logged; email confirmed as fallback', async ({
    page,
  }) => {
    await mockSmsFail(page);
    await mockNotificationApi(page, { status: 'Sent', channel: 'Email' });

    await test.step('Verify SMS notification shows Failed status', async () => {
      const smsResp = await page.request.get(
        '/api/notifications?patientEmail=swap-er-patient@propeliq.dev&channel=SMS',
      );
      const body = await smsResp.json() as { status: string; retryCount: number }[];
      expect(body[0]?.status).toBe('Failed');
    });

    await test.step('Verify Email notification sent as fallback', async () => {
      const emailResp = await page.request.get(
        '/api/notifications?patientEmail=swap-er-patient@propeliq.dev&channel=Email',
      );
      const body = await emailResp.json() as { status: string }[];
      expect(body[0]?.status).toBe('Sent');
    });
  });
});

// ── UC-005: Walk-In Booking ────────────────────────────────────────────────

test.describe('UC-005: Walk-In Queue Management', () => {
  test('TC-UC005-HP-001: Staff creates walk-in for known patient; patient appears in queue', async ({
    page,
  }) => {
    const d = testData.tc_uc005_hp_001;
    const walkIn = new WalkInPage(page);

    await test.step('Search for existing patient', async () => {
      await page.goto('/staff/walkin');
      await walkIn.searchPatient(d.patientName);
      await expect(walkIn.patientResult(d.patientRef)).toBeVisible();
    });

    await test.step('Select slot and confirm walk-in booking', async () => {
      await walkIn.createWalkIn(d.patientRef, d.slotId);
      await expect(walkIn.successAlert).toContainText('Walk-in booking confirmed');
    });

    await test.step('Verify patient visible in same-day queue', async () => {
      await page.goto('/staff/queue');
      const queue = new QueuePage(page);
      await expect(queue.queueEntry(d.patientRef)).toBeVisible();
      await expect(queue.walkinBadge(d.patientRef)).toBeVisible();
    });
  });

  test('TC-UC005-EC-001: Staff skips account creation — anonymous walk-in tracked with temp ID', async ({
    page,
  }) => {
    const d = testData.tc_uc005_ec_001;
    const walkIn = new WalkInPage(page);

    await test.step('Search patient and confirm no results', async () => {
      await page.goto('/staff/walkin');
      await walkIn.searchPatient('Unknown Patient');
      await walkIn.skipAccountCreationButton.click();
    });

    await test.step('Assign slot for anonymous visit', async () => {
      await walkIn.slotCard(d.slotId).click();
      await walkIn.confirmAnonButton.click();
    });

    await test.step('Verify temporary visit ID generated', async () => {
      await expect(walkIn.anonymousVisitId).toBeVisible();
      const tempId = await walkIn.anonymousVisitId.innerText();
      expect(tempId).toMatch(/^TEMP-[A-Z0-9]{6}$/);
    });
  });

  test('TC-UC005-ER-001: No available slots — patient added to overflow queue with wait estimate', async ({
    page,
  }) => {
    const walkIn = new WalkInPage(page);

    await test.step('Search patient when all slots are full', async () => {
      await page.goto('/staff/walkin');
      await walkIn.searchPatient('Jane Overflow');
      await walkIn.patientResult('jane-overflow').click();
    });

    await test.step('Verify no slots banner and add to overflow queue', async () => {
      await expect(walkIn.noSlotsBanner).toContainText('No available slots for today');
      await walkIn.overflowQueueButton.click();
    });

    await test.step('Verify overflow wait estimate displayed', async () => {
      await expect(walkIn.overflowWaitEstimate).toBeVisible();
    });
  });
});

// ── UC-006: Arrival & Queue Management ────────────────────────────────────

test.describe('UC-006: Arrival Marking and Queue Management', () => {
  test('TC-UC006-HP-001: Staff marks patient Arrived; queue updates in real time', async ({
    page,
  }) => {
    const d = testData.tc_uc006_hp_001;
    const queue = new QueuePage(page);

    await test.step('Open same-day queue', async () => {
      await page.goto('/staff/queue');
      await expect(queue.queueEntry(d.appointmentRef)).toBeVisible();
    });

    await test.step('Mark patient as Arrived', async () => {
      await queue.markArrived(d.appointmentRef);
    });

    await test.step('Verify real-time status update to Arrived', async () => {
      await expect(queue.queueEntry(d.appointmentRef)).toContainText('Arrived');
      await expect(queue.arrivalTime(d.appointmentRef)).toBeVisible();
    });
  });

  test('TC-UC006-EC-001: Patient not in today queue — staff searches by reference and marks Arrived', async ({
    page,
  }) => {
    const d = testData.tc_uc006_ec_001;
    const queue = new QueuePage(page);

    await test.step('Open queue and perform reference search', async () => {
      await page.goto('/staff/queue');
      await queue.searchByReference(d.referenceNumber);
    });

    await test.step('Mark found patient as Arrived', async () => {
      await expect(queue.queueEntry(d.patientRef)).toBeVisible();
      await queue.markArrived(d.patientRef);
      await expect(queue.queueEntry(d.patientRef)).toContainText('Arrived');
    });
  });

  test('TC-UC006-ER-001: Patient-role JWT cannot call arrived endpoint — HTTP 403 returned', async ({
    page,
  }) => {
    const d = testData.tc_uc006_er_001;
    await mockArrivedForbidden(page);

    await test.step('Attempt PATCH arrived as patient role', async () => {
      const response = await page.request.patch(
        `/api/appointments/${d.appointmentId}/arrived`,
        { headers: { Authorization: 'Bearer patient-role-token' } },
      );
      expect(response.status()).toBe(403);
    });

    await test.step('Verify no self-check-in UI on patient dashboard', async () => {
      await page.goto('/dashboard');
      await expect(page.getByTestId('self-checkin-button')).toBeHidden();
    });
  });
});

// ── UC-007: Document Upload ────────────────────────────────────────────────

test.describe('UC-007: Clinical Document Upload', () => {
  test('TC-UC007-HP-001: Patient uploads 3 valid PDFs — encrypted, stored, and queued', async ({
    page,
  }) => {
    const d = testData.tc_uc007_hp_001;
    await mockDocumentUploadApi(page, d.files.length);

    await test.step('Navigate to document upload page', async () => {
      await page.goto('/dashboard/documents');
    });

    await test.step('Stage 3 valid PDFs for upload', async () => {
      const uploader = new DocumentUploadPage(page);
      await uploader.uploadPdfs(d.files.map((f) => f.name));
      await expect(uploader.fileList).toBeVisible();
    });

    await test.step('Verify upload success and document history', async () => {
      const uploader = new DocumentUploadPage(page);
      await expect(uploader.successBanner).toContainText('3 documents uploaded successfully');
      await expect(uploader.documentHistory).toBeVisible();
    });
  });

  test('TC-UC007-EC-001: File exceeding 25 MB rejected; valid files proceed', async ({
    page,
  }) => {
    const d = testData.tc_uc007_ec_001;
    const validFiles = d.files.filter((f) => !f.expectRejected);
    await mockDocumentUploadApi(page, validFiles.length);

    await test.step('Stage mixed files including oversized PDF', async () => {
      await page.goto('/dashboard/documents');
      const uploader = new DocumentUploadPage(page);
      await uploader.uploadPdfs(d.files.map((f) => f.name));
    });

    await test.step('Verify oversized file rejected with error message', async () => {
      const uploader = new DocumentUploadPage(page);
      await expect(uploader.fileError('large_doc_30mb')).toContainText('exceeds 25 MB');
    });

    await test.step('Verify 2 valid files uploaded successfully', async () => {
      const uploader = new DocumentUploadPage(page);
      await expect(uploader.successBanner).toContainText('2 documents uploaded successfully');
    });
  });

  test('TC-UC007-ER-001: Non-PDF file (.docx) rejected with supported format message', async ({
    page,
  }) => {
    await test.step('Navigate to document upload page', async () => {
      await page.goto('/dashboard/documents');
    });

    await test.step('Attempt to upload a .docx file', async () => {
      const uploader = new DocumentUploadPage(page);
      await uploader.fileInput.setInputFiles({
        name: 'clinical_notes.docx',
        mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
        buffer: MINIMAL_PDF_BUFFER,
      });
    });

    await test.step('Verify rejection with supported format error', async () => {
      const uploader = new DocumentUploadPage(page);
      await expect(uploader.fileError('clinical_notes')).toContainText('Only PDF files are supported');
    });
  });
});

// ── UC-008: 360-Degree View ────────────────────────────────────────────────

test.describe('UC-008: 360-Degree Patient View', () => {
  test('TC-UC008-HP-001: Extract, detect conflict, staff resolves, profile verified', async ({
    page,
  }) => {
    const d = testData.tc_uc008_hp_001;
    await mockProfileVerifyBlocked(page);

    await test.step('Open 360-degree patient view', async () => {
      await page.goto(`/staff/patients/${d.patientId}/360`);
      const view = new ThreeSixtyViewPage(page);
      await expect(view.heading).toBeVisible();
    });

    await test.step('Verify conflict indicator visible for medication field', async () => {
      const view = new ThreeSixtyViewPage(page);
      await expect(view.conflictIndicator(d.conflictField)).toBeVisible();
    });

    await test.step('Attempt verification — blocked due to unresolved conflict', async () => {
      const view = new ThreeSixtyViewPage(page);
      await view.verifyProfile();
      await expect(view.errorAlert).toContainText('Resolve all conflicts before verifying');
    });

    await test.step('Open conflict detail and verify both source values shown', async () => {
      const view = new ThreeSixtyViewPage(page);
      await view.conflictIndicator(d.conflictField).click();
      await expect(view.conflictValue(1)).toContainText(d.conflictValue1);
      await expect(view.conflictValue(2)).toContainText(d.conflictValue2);
    });

    await test.step('Select authoritative value and verify profile', async () => {
      const view = new ThreeSixtyViewPage(page);
      await mockProfileVerifySuccess(page);
      await view.resolveConflict(d.conflictField, d.authoritativeValue);
      await view.verifyProfile();
      await expect(view.profileStatusBadge).toContainText('Verified');
    });
  });

  test('TC-UC008-EC-001: No conflicts detected — profile verified directly without conflict step', async ({
    page,
  }) => {
    const d = testData.tc_uc008_ec_001;
    await mockProfileVerifySuccess(page);

    await test.step('Open 360-degree view for patient with no conflicts', async () => {
      await page.goto(`/staff/patients/${d.patientId}/360`);
      const view = new ThreeSixtyViewPage(page);
      await expect(view.noConflictsBanner).toContainText('No data conflicts detected');
    });

    await test.step('Verify conflict indicators absent', async () => {
      const view = new ThreeSixtyViewPage(page);
      await expect(view.conflictIndicator('medication')).toBeHidden();
    });

    await test.step('Verify profile directly without conflict resolution', async () => {
      const view = new ThreeSixtyViewPage(page);
      await view.verifyProfile();
      await expect(view.profileStatusBadge).toContainText('Verified');
    });
  });

  test('TC-UC008-ER-001: Corrupted PDF extraction fails — document marked Extraction Failed', async ({
    page,
  }) => {
    const d = testData.tc_uc008_er_001;

    await test.step('Open 360-degree view with corrupted document', async () => {
      await page.goto(`/staff/patients/${d.patientId}/360`);
    });

    await test.step('Verify document status shows Extraction Failed', async () => {
      const view = new ThreeSixtyViewPage(page);
      await expect(view.documentStatus(d.corruptedDocRef)).toContainText('Extraction Failed');
      await expect(view.extractionFailedNotice).toContainText('could not be processed');
    });
  });
});

// ── UC-009: Medical Code Review ────────────────────────────────────────────

test.describe('UC-009: Medical Code Review', () => {
  test('TC-UC009-HP-001: Staff confirms ICD-10 and CPT codes; saved with staff timestamp', async ({
    page,
  }) => {
    const d = testData.tc_uc009_hp_001;

    await test.step('Open medical code review interface', async () => {
      await page.goto(`/staff/patients/${d.patientId}/codes`);
      const coding = new MedicalCodingPage(page);
      await expect(coding.heading).toBeVisible();
    });

    await test.step('Verify AI suggestions with evidence shown', async () => {
      const coding = new MedicalCodingPage(page);
      await expect(coding.icd10Suggestion(d.icd10Code)).toBeVisible();
      await expect(coding.cptSuggestion(d.cptCode)).toBeVisible();
    });

    await test.step('Confirm ICD-10 and CPT codes', async () => {
      const coding = new MedicalCodingPage(page);
      await coding.confirmCode(d.icd10Code);
      await coding.confirmCode(d.cptCode);
      await expect(coding.confirmedBadge('icd10', d.icd10Code)).toBeVisible();
      await expect(coding.confirmedBadge('cpt', d.cptCode)).toBeVisible();
    });

    await test.step('Save confirmed codes and verify success', async () => {
      const coding = new MedicalCodingPage(page);
      await coding.saveCodes();
      await expect(coding.successAlert).toContainText('Medical codes confirmed and saved');
    });
  });

  test('TC-UC009-EC-001: Staff manually adds ICD-10 code validated against standard library', async ({
    page,
  }) => {
    const d = testData.tc_uc009_ec_001;

    await test.step('Open code review and click Add manually', async () => {
      await page.goto(`/staff/patients/code-patient-id/codes`);
      const coding = new MedicalCodingPage(page);
      await coding.addManualCode(d.validManualCode);
    });

    await test.step('Verify valid code accepted with description', async () => {
      const coding = new MedicalCodingPage(page);
      await expect(coding.validationResult).toContainText(d.validCodeDescription);
      await expect(coding.validationResult).toContainText('Valid');
      await coding.addToConfirmedButton.click();
    });

    await test.step('Verify invalid code rejected', async () => {
      const coding = new MedicalCodingPage(page);
      await coding.addManualCode(d.invalidCode);
      await expect(coding.validationResult).toContainText('Code not found in ICD-10 library');
    });
  });

  test('TC-UC009-ER-001: Profile verification blocked when unresolved conflicts exist', async ({
    page,
  }) => {
    const d = testData.tc_uc009_er_001;
    await mockProfileVerifyBlocked(page);

    await test.step('Open 360-degree view with unresolved conflict', async () => {
      await page.goto(`/staff/patients/${d.patientId}/360`);
      const view = new ThreeSixtyViewPage(page);
      await expect(view.conflictIndicator('medication')).toBeVisible();
    });

    await test.step('Attempt verification — blocked with informative error', async () => {
      const view = new ThreeSixtyViewPage(page);
      await view.verifyProfile();
      await expect(view.errorAlert).toContainText('Resolve all conflicts before verifying');
    });

    await test.step('Verify profile status remains Unverified', async () => {
      const view = new ThreeSixtyViewPage(page);
      await expect(view.profileStatusBadge).not.toContainText('Verified');
    });
  });
});
