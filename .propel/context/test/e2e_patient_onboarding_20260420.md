---
journey: "Patient Onboarding to Verified Clinical Profile"
source: ".propel/context/docs/spec.md"
use_cases: ["UC-001", "UC-002", "UC-007", "UC-008"]
priority: "P0"
base_url: "http://localhost:4200"
playwright_version: "1.x"
framework: "Angular 18"
generated_date: "2026-04-20"
---

# E2E Journey: Patient Onboarding to Verified Clinical Profile

## Metadata

| Field | Value |
|-------|-------|
| Journey | Patient Onboarding to Verified Clinical Profile |
| Source | `.propel/context/docs/spec.md` |
| Use Cases | UC-001 → UC-002 → UC-007 → UC-008 |
| Priority | P0 |
| Base URL | `http://localhost:4200` |
| Framework | Angular 18 + Angular Material |
| Playwright Version | 1.x (TypeScript) |
| Total Phases | 4 |

---

## Journey Overview

### Journey Summary

This end-to-end journey covers the complete patient onboarding experience: from initial self-registration and appointment booking (UC-001) through AI-assisted intake (UC-002), clinical document upload (UC-007), and AI extraction with staff verification of the 360-degree patient profile (UC-008).

It validates that data flows correctly across all system layers and that the profile reaches `status=Verified` at the end of the journey.

---

### Journey Data

```yaml
journey_data:
  patient:
    email: "e2e.onboard.patient@propeliq.dev"
    password: "E2eP@ss001!"
    firstName: "E2E"
    lastName: "OnboardPatient"
    dateOfBirth: "1988-03-22"
    phone: "+14155550501"
    verificationToken: "e2e-verify-token-001"

  appointment:
    slotId: "slot-e2e-001"
    slotDate: "2026-05-20"
    slotTime: "10:00"
    insurance:
      provider: "BlueCross"
      memberId: "MBR-E2E-001"

  documents:
    - fixture: "fixtures/e2e_doc_1.pdf"
      size_mb: 3
      contains:
        medications: ["Lisinopril 10mg"]
        bp: "130/85"
    - fixture: "fixtures/e2e_doc_2.pdf"
      size_mb: 4
      contains:
        medications: ["Lisinopril 20mg"]  # dosage conflict
        bp: "128/82"

  staff:
    email: "e2e.staff@propeliq.dev"
    password: "StaffP@ss001!"

  intake:
    chat_message: "I take Lisinopril 10mg daily and I am allergic to sulfa drugs"
    expected_medications: ["Lisinopril 10mg"]
    expected_allergies: ["Sulfa drugs"]

  conflict:
    field: "Lisinopril dosage"
    value_source_1: "10mg (Document 1)"
    value_source_2: "20mg (Document 2)"
    authoritative_value: "Lisinopril 10mg"
```

---

### Phase 1 — Patient Registration and Appointment Booking (UC-001)

```yaml
phase: 1
name: "Patient Registration and Appointment Booking"
use_case: "UC-001"
actor: "Patient"

e2e_steps:
  - step_id: "P1-001"
    action: navigate
    target: "http://localhost:4200/register"
    expect: "Registration page loaded"

  - step_id: "P1-002"
    action: fill
    target: "getByLabel('Email address')"
    value: "{{journey_data.patient.email}}"
    expect: "Email field accepts input"

  - step_id: "P1-003"
    action: fill
    target: "getByLabel('Password')"
    value: "{{journey_data.patient.password}}"
    expect: "Password field accepts input"

  - step_id: "P1-004"
    action: fill
    target: "getByLabel('First name')"
    value: "{{journey_data.patient.firstName}}"
    expect: "Name field accepts input"

  - step_id: "P1-005"
    action: fill
    target: "getByLabel('Last name')"
    value: "{{journey_data.patient.lastName}}"
    expect: "Field accepts input"

  - step_id: "P1-006"
    action: fill
    target: "getByLabel('Date of birth')"
    value: "{{journey_data.patient.dateOfBirth}}"
    expect: "Field accepts date"

  - step_id: "P1-007"
    action: fill
    target: "getByLabel('Phone number')"
    value: "{{journey_data.patient.phone}}"
    expect: "Field accepts phone"

  - step_id: "P1-008"
    action: click
    target: "getByRole('button', {name: 'Create account'})"
    expect: "Account created; verification email dispatched"

  - step_id: "P1-009"
    action: navigate
    target: "http://localhost:4200/verify-email?token={{journey_data.patient.verificationToken}}"
    expect: "Email verified confirmation page loaded"

  - step_id: "P1-010"
    action: verify
    target: "getByRole('heading', {name: 'Email verified'})"
    expect: "visible"

  - step_id: "P1-011"
    action: navigate
    target: "http://localhost:4200/book"
    expect: "Booking page loaded with available slots"

  - step_id: "P1-012"
    action: click
    target: "getByTestId('slot-e2e-001')"
    expect: "Slot selected"

  - step_id: "P1-013"
    action: fill
    target: "getByLabel('Insurance provider')"
    value: "{{journey_data.appointment.insurance.provider}}"
    expect: "Field accepts input"

  - step_id: "P1-014"
    action: fill
    target: "getByLabel('Member ID')"
    value: "{{journey_data.appointment.insurance.memberId}}"
    expect: "Field accepts input"

  - step_id: "P1-015"
    action: click
    target: "getByRole('button', {name: 'Verify insurance'})"
    expect: "Insurance verified"

  - step_id: "P1-016"
    action: click
    target: "getByRole('button', {name: 'Confirm booking'})"
    expect: "Booking confirmed; appointment reference shown"

  - step_id: "P1-017"
    action: store_value
    target: "getByTestId('booking-reference')"
    variable: "bookingReference"
    expect: "Booking reference captured for use in subsequent phases"

checkpoint_1:
  name: "Registration and Booking Complete"
  verifications:
    - "Patient account exists in system with verified email"
    - "Appointment booking confirmed with reference number"
    - "Insurance status = Verified"
    - "Patient can authenticate with registered credentials"
  api_checks:
    - endpoint: "GET /api/users?email={{journey_data.patient.email}}"
      expect: "User with status=Active, emailVerified=true"
    - endpoint: "GET /api/appointments?patientEmail={{journey_data.patient.email}}"
      expect: "1 appointment with status=Confirmed, slotId=slot-e2e-001"
```

---

### Phase 2 — AI-Assisted Intake Completion (UC-002)

```yaml
phase: 2
name: "AI-Assisted Intake Completion"
use_case: "UC-002"
actor: "Patient"
prerequisite: "Phase 1 checkpoint passed; patient authenticated; appointment confirmed"

e2e_steps:
  - step_id: "P2-001"
    action: navigate
    target: "http://localhost:4200/intake/{{bookingReference}}"
    expect: "Intake mode selection shown"

  - step_id: "P2-002"
    action: click
    target: "getByRole('button', {name: 'AI-Assisted intake'})"
    expect: "Conversational chat interface opens"

  - step_id: "P2-003"
    action: verify
    target: "getByRole('log')"
    expect: "AI opening question visible"

  - step_id: "P2-004"
    action: fill
    target: "getByLabel('Your message')"
    value: "{{journey_data.intake.chat_message}}"
    expect: "Input accepted"

  - step_id: "P2-005"
    action: click
    target: "getByRole('button', {name: 'Send'})"
    expect: "AI parses message; fields auto-populated in preview"

  - step_id: "P2-006"
    action: verify
    target: "getByTestId('intake-preview-medications')"
    expect: "shows 'Lisinopril 10mg'"

  - step_id: "P2-007"
    action: verify
    target: "getByTestId('intake-preview-allergies')"
    expect: "shows 'Sulfa drugs'"

  - step_id: "P2-008"
    action: click
    target: "getByRole('button', {name: 'Submit intake'})"
    expect: "Intake submitted"

  - step_id: "P2-009"
    action: verify
    target: "getByRole('alert')"
    expect: "contains text 'Intake submitted successfully'"

checkpoint_2:
  name: "Intake Complete"
  verifications:
    - "IntakeRecord created with source=AI"
    - "Medications and allergies correctly parsed and stored"
    - "Intake linked to booking reference"
  api_checks:
    - endpoint: "GET /api/intake?appointmentRef={{bookingReference}}"
      expect: "IntakeRecord with source=AI, medications=[Lisinopril 10mg], allergies=[Sulfa drugs], completedAt non-null"
```

---

### Phase 3 — Clinical Document Upload (UC-007)

```yaml
phase: 3
name: "Clinical Document Upload"
use_case: "UC-007"
actor: "Patient"
prerequisite: "Phase 2 checkpoint passed; patient authenticated"

e2e_steps:
  - step_id: "P3-001"
    action: navigate
    target: "http://localhost:4200/dashboard/documents"
    expect: "Document upload page loaded"

  - step_id: "P3-002"
    action: set_input_files
    target: "getByTestId('document-upload-input')"
    files: ["fixtures/e2e_doc_1.pdf", "fixtures/e2e_doc_2.pdf"]
    expect: "2 files staged"

  - step_id: "P3-003"
    action: verify
    target: "getByTestId('upload-file-list')"
    expect: "2 files listed: e2e_doc_1.pdf, e2e_doc_2.pdf"

  - step_id: "P3-004"
    action: click
    target: "getByRole('button', {name: 'Upload documents'})"
    expect: "Upload initiated"

  - step_id: "P3-005"
    action: verify
    target: "getByRole('progressbar')"
    expect: "Upload progress bar visible"

  - step_id: "P3-006"
    action: verify
    target: "getByTestId('upload-success-banner')"
    expect: "shows '2 documents uploaded successfully'"

  - step_id: "P3-007"
    action: verify
    target: "getByTestId('document-history-list')"
    expect: "2 entries with status 'Processing'"

checkpoint_3:
  name: "Documents Uploaded and Queued for Extraction"
  verifications:
    - "2 ClinicalDocument records created with processingStatus=Pending"
    - "Files stored with encrypted storage paths"
    - "AI extraction jobs queued"
  api_checks:
    - endpoint: "GET /api/documents?patientEmail={{journey_data.patient.email}}"
      expect: "2 ClinicalDocument records; processingStatus=Pending; storagePath non-null"
    - endpoint: "GET /api/extraction-queue?patientEmail={{journey_data.patient.email}}"
      expect: "2 jobs with status=Queued"
```

---

### Phase 4 — 360-Degree View: Conflict Resolution and Profile Verification (UC-008)

```yaml
phase: 4
name: "Conflict Resolution and Profile Verification"
use_case: "UC-008"
actor: "Staff"
prerequisite: "Phase 3 checkpoint passed; AI extraction completed; DataConflict record exists for Lisinopril dosage"

e2e_steps:
  - step_id: "P4-001"
    action: navigate
    target: "http://localhost:4200/login"
    expect: "Login page loaded"

  - step_id: "P4-002"
    action: fill
    target: "getByLabel('Email address')"
    value: "{{journey_data.staff.email}}"
    expect: "field accepts input"

  - step_id: "P4-003"
    action: fill
    target: "getByLabel('Password')"
    value: "{{journey_data.staff.password}}"
    expect: "field accepts input"

  - step_id: "P4-004"
    action: click
    target: "getByRole('button', {name: 'Sign in'})"
    expect: "Staff authenticated; staff dashboard loaded"

  - step_id: "P4-005"
    action: navigate
    target: "http://localhost:4200/staff/patients?search={{journey_data.patient.email}}"
    expect: "Patient search results loaded"

  - step_id: "P4-006"
    action: click
    target: "getByTestId('patient-result-e2e-onboard-patient')"
    expect: "Patient profile opened"

  - step_id: "P4-007"
    action: click
    target: "getByRole('link', {name: '360° View'})"
    expect: "360-degree patient view loaded"

  - step_id: "P4-008"
    action: verify
    target: "getByRole('heading', {name: '360° Patient View'})"
    expect: "heading visible"

  - step_id: "P4-009"
    action: verify
    target: "getByTestId('conflict-indicator-medication')"
    expect: "conflict indicator visible for Lisinopril dosage"

  - step_id: "P4-010"
    action: click
    target: "getByRole('button', {name: 'Verify profile'})"
    expect: "HTTP 422; verification blocked with error message"

  - step_id: "P4-011"
    action: verify
    target: "getByRole('alert')"
    expect: "contains text 'Resolve all conflicts before verifying'"

  - step_id: "P4-012"
    action: click
    target: "getByTestId('conflict-indicator-medication')"
    expect: "Conflict detail panel opens"

  - step_id: "P4-013"
    action: verify
    target: "getByTestId('conflict-value-1')"
    expect: "shows 'Lisinopril 10mg — Document 1'"

  - step_id: "P4-014"
    action: verify
    target: "getByTestId('conflict-value-2')"
    expect: "shows 'Lisinopril 20mg — Document 2'"

  - step_id: "P4-015"
    action: click
    target: "getByRole('button', {name: 'Select Lisinopril 10mg'})"
    expect: "Authoritative value selected; conflict status=Resolved"

  - step_id: "P4-016"
    action: click
    target: "getByRole('button', {name: 'Verify profile'})"
    expect: "HTTP 200; profile verified"

  - step_id: "P4-017"
    action: verify
    target: "getByTestId('profile-status-badge')"
    expect: "shows 'Verified'"

  - step_id: "P4-018"
    action: verify
    target: "getByTestId('intake-data-medications')"
    expect: "Lisinopril 10mg visible in verified profile summary"

  - step_id: "P4-019"
    action: verify
    target: "getByTestId('intake-data-allergies')"
    expect: "Sulfa drugs visible in verified profile summary (from Phase 2 intake)"

checkpoint_4:
  name: "Profile Verified"
  verifications:
    - "360° profile status=Verified"
    - "All DataConflict records resolutionStatus=Resolved"
    - "Authoritative Lisinopril value=10mg"
    - "Intake data (allergies, medications) from Phase 2 visible in profile"
    - "AuditLog entry for verification with staffId and timestamp"
  api_checks:
    - endpoint: "GET /api/patients?email={{journey_data.patient.email}}/360"
      expect: "status=Verified; conflicts=[{resolutionStatus: Resolved, authoritativeValue: 'Lisinopril 10mg'}]"
    - endpoint: "GET /api/audit?entityType=PatientProfile&action=Verified"
      expect: "AuditLog entry with staffId={{e2e.staff.id}}, timestamp within session"
```

---

## Page Objects

```yaml
pages:
  - name: "RegistrationPage"
    file: "pages/registration.page.ts"
    elements:
      - emailInput: "getByLabel('Email address')"
      - passwordInput: "getByLabel('Password')"
      - firstNameInput: "getByLabel('First name')"
      - lastNameInput: "getByLabel('Last name')"
      - dobInput: "getByLabel('Date of birth')"
      - phoneInput: "getByLabel('Phone number')"
      - submitButton: "getByRole('button', {name: 'Create account'})"

  - name: "BookingPage"
    file: "pages/booking.page.ts"
    elements:
      - slotCard: "getByTestId('slot-{slotId}')"
      - insuranceProviderInput: "getByLabel('Insurance provider')"
      - memberIdInput: "getByLabel('Member ID')"
      - verifyInsuranceButton: "getByRole('button', {name: 'Verify insurance'})"
      - confirmButton: "getByRole('button', {name: 'Confirm booking'})"
      - bookingReference: "getByTestId('booking-reference')"

  - name: "IntakePage"
    file: "pages/intake.page.ts"
    elements:
      - aiModeButton: "getByRole('button', {name: 'AI-Assisted intake'})"
      - chatLog: "getByRole('log')"
      - messageInput: "getByLabel('Your message')"
      - sendButton: "getByRole('button', {name: 'Send'})"
      - medicationsPreview: "getByTestId('intake-preview-medications')"
      - allergiesPreview: "getByTestId('intake-preview-allergies')"
      - submitButton: "getByRole('button', {name: 'Submit intake'})"

  - name: "DocumentUploadPage"
    file: "pages/document-upload.page.ts"
    elements:
      - fileInput: "getByTestId('document-upload-input')"
      - uploadButton: "getByRole('button', {name: 'Upload documents'})"
      - progressBar: "getByRole('progressbar')"
      - successBanner: "getByTestId('upload-success-banner')"

  - name: "ThreeSixtyViewPage"
    file: "pages/three-sixty-view.page.ts"
    elements:
      - conflictIndicator: "getByTestId('conflict-indicator-{fieldName}')"
      - conflictValue1: "getByTestId('conflict-value-1')"
      - conflictValue2: "getByTestId('conflict-value-2')"
      - verifyButton: "getByRole('button', {name: 'Verify profile'})"
      - profileStatusBadge: "getByTestId('profile-status-badge')"
```

## Success Criteria

- [ ] Patient successfully registers and verifies email (Phase 1)
- [ ] Appointment booking completed with insurance verification (Phase 1)
- [ ] AI intake parses medications and allergies correctly (Phase 2)
- [ ] 2 documents uploaded and queued for extraction (Phase 3)
- [ ] Staff cannot verify profile while conflict exists (Phase 4)
- [ ] Lisinopril dosage conflict resolved to 10mg (Phase 4)
- [ ] Profile reaches status=Verified (Phase 4)
- [ ] Intake data from Phase 2 visible in verified profile (Phase 4 cross-phase data integrity)
- [ ] All checkpoints pass API verification
- [ ] Journey runs without `waitForTimeout`; all assertions use web-first patterns

### Journey Traceability

| Phase | Use Case | Functional Requirements |
|-------|----------|------------------------|
| 1 | UC-001 | FR-001, FR-002, FR-003, FR-004, FR-005, FR-006 |
| 2 | UC-002 | FR-007, FR-008, FR-009, FR-010 |
| 3 | UC-007 | FR-035, FR-036, FR-037, FR-038 |
| 4 | UC-008 | FR-039, FR-040, FR-041, FR-042, FR-043 |

## Locator Reference

| Priority | Method | Example |
|----------|--------|---------|
| 1st | `getByRole` | `getByRole('button', {name: 'Verify profile'})` |
| 2nd | `getByTestId` | `getByTestId('profile-status-badge')` |
| 3rd | `getByLabel` | `getByLabel('Email address')` |
| AVOID | CSS | `.mat-card`, `#dynamic-id`, `nth-child` |

---

*Template: automated-e2e-template.md | Output: `.propel/context/test/e2e_patient_onboarding_20260420.md`*
