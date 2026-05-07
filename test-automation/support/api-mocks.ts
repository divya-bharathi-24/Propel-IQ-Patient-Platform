import { type Page, type Route } from '@playwright/test';

// ── Response builders ──────────────────────────────────────────────────────

export const Responses = {
  notificationSent: (overrides: Record<string, unknown> = {}) => ({
    status: 'Sent',
    channel: 'Email',
    ...overrides,
  }),

  userActive: (email: string, role = 'Patient') => ({
    email,
    role,
    status: 'Active',
    emailVerified: true,
  }),

  appointmentConfirmed: (slotId: string, patientEmail: string) => ({
    status: 'Confirmed',
    slotId,
    patientEmail,
    reference: `REF-${Date.now()}`,
  }),

  appointmentArrived: (ref: string) => ({
    status: 'Arrived',
    reference: ref,
    arrivalTime: new Date().toISOString(),
  }),

  intakeRecord: (source: 'AI' | 'Manual', fields: Record<string, unknown>) => ({
    source,
    completedAt: new Date().toISOString(),
    ...fields,
  }),

  /** Upload batch response envelope: `{ files: UploadFileResult[] }` (POST /api/documents/upload). */
  documentUploadBatch: (count: number) => ({
    files: Array.from({ length: count }, (_, i) => ({
      fileName: `e2e_doc_${i + 1}.pdf`,
      success: true,
      document: {
        id: `doc-${i + 1}`,
        fileName: `e2e_doc_${i + 1}.pdf`,
        fileSize: (i + 1) * 3 * 1024 * 1024,
        uploadedAt: new Date().toISOString(),
        processingStatus: 'Pending',
      },
    })),
  }),

  /** History list: `ClinicalDocumentDto[]` (GET /api/documents). */
  documentHistory: (count: number) =>
    Array.from({ length: count }, (_, i) => ({
      id: `doc-${i + 1}`,
      fileName: `e2e_doc_${i + 1}.pdf`,
      fileSize: (i + 1) * 3 * 1024 * 1024,
      uploadedAt: new Date().toISOString(),
      processingStatus: 'Pending',
    })),

  waitlistEntry: (patientEmail: string, slotId: string, status = 'Active') => ({
    patientEmail,
    slotId,
    status,
    enrolledAt: new Date().toISOString(),
  }),

  patientProfile: (status: 'Verified' | 'Unverified' = 'Verified') => ({
    verificationStatus: status,
    verifiedAt: new Date().toISOString(),
    verifiedByStaffName: 'Staff User',
  }),

  medicalCodes: (icd10: string, cpt: string) => [
    { code: icd10, type: 'ICD10', verificationStatus: 'Accepted', verifiedAt: new Date().toISOString() },
    { code: cpt, type: 'CPT', verificationStatus: 'Accepted', verifiedAt: new Date().toISOString() },
  ],

  forbidden: () => ({ error: 'Forbidden' }),

  http422: () => ({ error: 'Unprocessable Entity', message: 'Resolve all conflicts before verifying' }),

  calendarEventSynced: (appointmentId: string) => ({
    appointmentId,
    provider: 'Google',
    status: 'Synced',
    eventId: `gcal-${Date.now()}`,
  }),
} as const;

// ── Route mock factories ───────────────────────────────────────────────────

export async function mockNotificationApi(page: Page, response = Responses.notificationSent()): Promise<void> {
  await page.route('**/api/notifications**', (route: Route) =>
    route.fulfill({ status: 200, json: response }),
  );
}

export async function mockAiIntakeApi(
  page: Page,
  medications: string[],
  allergies: string[],
): Promise<void> {
  await page.route('**/api/intake/ai**', (route: Route) =>
    route.fulfill({
      status: 200,
      json: {
        medications,
        allergies,
        confidence: 0.95,
        followUpQuestion: null,
      },
    }),
  );
}

export async function mockAiIntakeLowConfidence(page: Page): Promise<void> {
  await page.route('**/api/intake/ai**', (route: Route) =>
    route.fulfill({
      status: 200,
      json: {
        medications: [],
        allergies: [],
        confidence: 0.6,
        followUpQuestion: 'Could you clarify the name and dosage of the blood pressure medication?',
      },
    }),
  );
}

export async function mockDocumentUploadApi(page: Page, count: number): Promise<void> {
  // POST /api/documents/upload — batch upload result envelope { files: UploadFileResult[] }
  await page.route('**/api/documents/upload**', (route: Route) =>
    route.fulfill({ status: 201, json: Responses.documentUploadBatch(count) }),
  );
  // GET /api/documents — upload history ClinicalDocumentDto[]
  await page.route('**/api/documents', (route: Route) =>
    route.fulfill({ status: 200, json: Responses.documentHistory(count) }),
  );
}

export async function mockWaitlistApi(page: Page, entries: unknown[]): Promise<void> {
  await page.route('**/api/waitlist**', (route: Route) =>
    route.fulfill({ status: 200, json: entries }),
  );
}

export async function mockAppointmentCancelApi(page: Page): Promise<void> {
  await page.route('**/api/appointments/**/cancel', (route: Route) =>
    route.fulfill({ status: 200, json: { status: 'Cancelled' } }),
  );
}

export async function mockArrivedForbidden(page: Page): Promise<void> {
  await page.route('**/api/appointments/**/arrived', (route: Route) =>
    route.fulfill({ status: 403, json: Responses.forbidden() }),
  );
}

export async function mockProfileVerifyBlocked(page: Page): Promise<void> {
  await page.route('**/api/staff/patients/**/360-view/verify', (route: Route) =>
    route.fulfill({ status: 422, json: Responses.http422() }),
  );
}

export async function mockProfileVerifySuccess(page: Page): Promise<void> {
  await page.route('**/api/staff/patients/**/360-view/verify', (route: Route) =>
    route.fulfill({ status: 200, json: Responses.patientProfile('Verified') }),
  );
}

export async function mockCalendarOAuthSuccess(page: Page): Promise<void> {
  await page.route('**/api/calendars/**', (route: Route) =>
    route.fulfill({ status: 200, json: Responses.calendarEventSynced('appt-calendar-001') }),
  );
}

export async function mockSmsFail(page: Page): Promise<void> {
  await page.route('**/api/notifications/sms**', (route: Route) =>
    route.fulfill({ status: 503, json: { error: 'Service Unavailable' } }),
  );
}

export async function mockUserDeactivateForbidden(page: Page): Promise<void> {
  await page.route('**/api/users/**', (route: Route) => {
    if (route.request().method() === 'DELETE') {
      return route.fulfill({ status: 403, json: Responses.forbidden() });
    }
    return route.continue();
  });
}
