---
journey: "Staff Walk-In to Clinical Code Confirmation"
source: ".propel/context/docs/spec.md"
use_cases: ["UC-005", "UC-006", "UC-009"]
priority: "P0"
base_url: "http://localhost:4200"
playwright_version: "1.x"
framework: "Angular 18"
generated_date: "2026-04-20"
---

# E2E Journey: Staff Walk-In to Clinical Code Confirmation

## Metadata

| Field | Value |
|-------|-------|
| Journey | Staff Walk-In to Clinical Code Confirmation |
| Source | `.propel/context/docs/spec.md` |
| Use Cases | UC-005 → UC-006 → UC-009 |
| Priority | P0 |
| Base URL | `http://localhost:4200` |
| Framework | Angular 18 + Angular Material |
| Playwright Version | 1.x (TypeScript) |
| Total Phases | 3 |

---

## Journey Overview

### Journey Summary

This end-to-end journey covers the full staff-side clinical workflow for a walk-in patient visit: from creating the walk-in booking (UC-005) through marking the patient as Arrived and managing the same-day queue (UC-006), to reviewing AI-suggested medical codes and confirming ICD-10 and CPT codes for billing (UC-009).

It validates that data flows from walk-in creation through to a fully coded clinical visit, all within a single staff session.

---

### Journey Data

```yaml
journey_data:
  staff:
    email: "e2e.walkin.staff@propeliq.dev"
    password: "StaffP@ss001!"
    role: "Staff"

  patient:
    email: "e2e.walkin.patient@propeliq.dev"
    name: "Walk-In Patient E2E"
    searchName: "Walk-In Patient"
    patientId: "walkin-patient-e2e-id"
    profileStatus: "Verified"

  slot:
    id: "slot-walkin-e2e-001"
    date: "today"
    time: "11:00"

  clinical_codes:
    icd10:
      code: "J06.9"
      description: "Acute upper respiratory infection, unspecified"
      source: "AI suggestion"
    cpt:
      code: "99213"
      description: "Office or other outpatient visit, moderate complexity"
      source: "AI suggestion"

  expected_coding_record:
    icd10_status: "Accepted"
    cpt_status: "Accepted"
    verified_by: "e2e.walkin.staff"
```

---

### Phase 1 — Walk-In Booking Creation (UC-005)

```yaml
phase: 1
name: "Walk-In Booking Creation"
use_case: "UC-005"
actor: "Staff"

e2e_steps:
  - step_id: "P1-001"
    action: navigate
    target: "http://localhost:4200/login"
    expect: "Login page loaded"

  - step_id: "P1-002"
    action: fill
    target: "getByLabel('Email address')"
    value: "{{journey_data.staff.email}}"
    expect: "Field accepts input"

  - step_id: "P1-003"
    action: fill
    target: "getByLabel('Password')"
    value: "{{journey_data.staff.password}}"
    expect: "Field accepts input"

  - step_id: "P1-004"
    action: click
    target: "getByRole('button', {name: 'Sign in'})"
    expect: "Staff authenticated; staff dashboard loaded"

  - step_id: "P1-005"
    action: verify
    target: "getByTestId('user-role-badge')"
    expect: "shows 'Staff'"

  - step_id: "P1-006"
    action: navigate
    target: "http://localhost:4200/staff/walkin"
    expect: "Walk-in booking interface loaded"

  - step_id: "P1-007"
    action: fill
    target: "getByLabel('Search patient by name or date of birth')"
    value: "{{journey_data.patient.searchName}}"
    expect: "Search input accepts text"

  - step_id: "P1-008"
    action: click
    target: "getByRole('button', {name: 'Search'})"
    expect: "Patient search results returned"

  - step_id: "P1-009"
    action: click
    target: "getByTestId('patient-result-walkin-patient-e2e')"
    expect: "Patient selected; walk-in booking form shown with patient name"

  - step_id: "P1-010"
    action: verify
    target: "getByTestId('selected-patient-name')"
    expect: "shows '{{journey_data.patient.name}}'"

  - step_id: "P1-011"
    action: click
    target: "getByTestId('slot-walkin-e2e-001')"
    expect: "Slot selected and highlighted"

  - step_id: "P1-012"
    action: verify
    target: "getByTestId('selected-slot-time')"
    expect: "shows '11:00'"

  - step_id: "P1-013"
    action: click
    target: "getByRole('button', {name: 'Confirm walk-in booking'})"
    expect: "Walk-in booking confirmed"

  - step_id: "P1-014"
    action: verify
    target: "getByRole('alert')"
    expect: "contains text 'Walk-in booking confirmed'"

  - step_id: "P1-015"
    action: store_value
    target: "getByTestId('walkin-booking-reference')"
    variable: "walkinBookingRef"
    expect: "Booking reference captured for subsequent phases"

checkpoint_1:
  name: "Walk-In Booking Confirmed"
  verifications:
    - "Walk-in appointment created with status=Booked, type=WalkIn"
    - "Patient appears in today's queue"
    - "Slot slot-walkin-e2e-001 status=Booked"
  api_checks:
    - endpoint: "GET /api/appointments?patientId={{journey_data.patient.patientId}}&type=WalkIn"
      expect: "1 appointment with status=Booked, type=WalkIn, slotId=slot-walkin-e2e-001"
    - endpoint: "GET /api/queue?date=today"
      expect: "Queue entry for walkin-patient-e2e-id with status=PendingArrival"
```

---

### Phase 2 — Patient Arrival and Queue Management (UC-006)

```yaml
phase: 2
name: "Patient Arrival and Queue Management"
use_case: "UC-006"
actor: "Staff"
prerequisite: "Phase 1 checkpoint passed; patient in today's queue with status=PendingArrival"

e2e_steps:
  - step_id: "P2-001"
    action: navigate
    target: "http://localhost:4200/staff/queue"
    expect: "Same-day queue view loaded"

  - step_id: "P2-002"
    action: verify
    target: "getByTestId('queue-entry-{{walkinBookingRef}}')"
    expect: "Walk-In Patient E2E visible in queue with status 'Pending Arrival' and type badge 'Walk-in'"

  - step_id: "P2-003"
    action: verify
    target: "getByTestId('walkin-badge-{{walkinBookingRef}}')"
    expect: "Walk-in type badge visible"

  - step_id: "P2-004"
    action: click
    target: "getByTestId('arrived-button-{{walkinBookingRef}}')"
    expect: "Arrival confirmation action executed"

  - step_id: "P2-005"
    action: verify
    target: "getByTestId('queue-entry-{{walkinBookingRef}}')"
    expect: "status updates to 'Arrived' in real time (no page reload required)"

  - step_id: "P2-006"
    action: verify
    target: "getByTestId('arrival-time-{{walkinBookingRef}}')"
    expect: "arrival time timestamp visible in queue row"

  - step_id: "P2-007"
    action: verify
    target: "getByTestId('next-action-badge-{{walkinBookingRef}}')"
    expect: "shows 'Ready for Clinical Review'"

checkpoint_2:
  name: "Patient Marked as Arrived"
  verifications:
    - "Appointment status=Arrived"
    - "arrivalTime non-null UTC timestamp"
    - "AuditLog entry with staffId and action=Arrived"
    - "Queue entry status=Arrived"
  api_checks:
    - endpoint: "GET /api/appointments?patientId={{journey_data.patient.patientId}}&type=WalkIn"
      expect: "appointment with status=Arrived; arrivalTime non-null"
    - endpoint: "GET /api/audit?entityType=Appointment&action=Update&entityRef={{walkinBookingRef}}"
      expect: "AuditLog with staffId, before=Booked, after=Arrived"
```

---

### Phase 3 — Medical Code Review and Confirmation (UC-009)

```yaml
phase: 3
name: "Medical Code Review and Confirmation"
use_case: "UC-009"
actor: "Staff"
prerequisite: "Phase 2 checkpoint passed; patient profile status=Verified; AI code suggestions generated"

e2e_steps:
  - step_id: "P3-001"
    action: navigate
    target: "http://localhost:4200/staff/patients/{{journey_data.patient.patientId}}/codes"
    expect: "Medical code review interface loaded"

  - step_id: "P3-002"
    action: verify
    target: "getByRole('heading', {name: 'Medical Code Review'})"
    expect: "heading visible"

  - step_id: "P3-003"
    action: verify
    target: "getByTestId('icd10-suggestion-J06.9')"
    expect: "ICD-10 J06.9 visible with description 'Acute upper respiratory infection, unspecified'"

  - step_id: "P3-004"
    action: verify
    target: "getByTestId('icd10-evidence-J06.9')"
    expect: "supporting evidence from extracted documents shown"

  - step_id: "P3-005"
    action: verify
    target: "getByTestId('cpt-suggestion-99213')"
    expect: "CPT 99213 visible with description 'Office or other outpatient visit, moderate complexity'"

  - step_id: "P3-006"
    action: verify
    target: "getByTestId('cpt-evidence-99213')"
    expect: "supporting evidence shown"

  - step_id: "P3-007"
    action: click
    target: "getByRole('button', {name: 'Confirm J06.9'})"
    expect: "ICD-10 J06.9 confirmed; confirmation indicator shown"

  - step_id: "P3-008"
    action: verify
    target: "getByTestId('icd10-confirmed-J06.9')"
    expect: "checkmark or 'Confirmed' badge visible"

  - step_id: "P3-009"
    action: click
    target: "getByRole('button', {name: 'Confirm 99213'})"
    expect: "CPT 99213 confirmed; confirmation indicator shown"

  - step_id: "P3-010"
    action: verify
    target: "getByTestId('cpt-confirmed-99213')"
    expect: "checkmark or 'Confirmed' badge visible"

  - step_id: "P3-011"
    action: click
    target: "getByRole('button', {name: 'Save confirmed codes'})"
    expect: "Codes saved with staff ID and timestamp"

  - step_id: "P3-012"
    action: verify
    target: "getByRole('alert')"
    expect: "contains text 'Medical codes confirmed and saved'"

  - step_id: "P3-013"
    action: verify
    target: "getByTestId('coding-completion-badge')"
    expect: "shows 'Coding Complete'"

  - step_id: "P3-014"
    action: verify
    target: "getByTestId('appointment-disposition')"
    expect: "appointment disposition shows 'Coding Complete — Ready for Billing'"

checkpoint_3:
  name: "Medical Codes Confirmed"
  verifications:
    - "ICD-10 J06.9 verificationStatus=Accepted"
    - "CPT 99213 verificationStatus=Accepted"
    - "Both codes have verifiedBy=staffId and verifiedAt non-null"
    - "Visit disposition updated to Coding Complete"
    - "AuditLog entry for code confirmation"
  api_checks:
    - endpoint: "GET /api/medicalcodes?patientId={{journey_data.patient.patientId}}&appointmentRef={{walkinBookingRef}}"
      expect: "2 MedicalCode records: J06.9 status=Accepted, 99213 status=Accepted; verifiedAt non-null; verifiedBy non-null"
    - endpoint: "GET /api/audit?entityType=MedicalCode&action=Confirm"
      expect: "AuditLog entries for both codes with staffId"
    - endpoint: "GET /api/appointments?patientId={{journey_data.patient.patientId}}&type=WalkIn"
      expect: "appointment disposition=CodingComplete"
```

---

## Page Objects

```yaml
pages:
  - name: "LoginPage"
    file: "pages/login.page.ts"
    elements:
      - emailInput: "getByLabel('Email address')"
      - passwordInput: "getByLabel('Password')"
      - signInButton: "getByRole('button', {name: 'Sign in'})"
      - roleBadge: "getByTestId('user-role-badge')"
    actions:
      - login(email, password): "Fill credentials and submit"

  - name: "WalkInPage"
    file: "pages/walk-in.page.ts"
    elements:
      - searchInput: "getByLabel('Search patient by name or date of birth')"
      - searchButton: "getByRole('button', {name: 'Search'})"
      - patientResult: "getByTestId('patient-result-{patientId}')"
      - selectedPatientName: "getByTestId('selected-patient-name')"
      - slotCard: "getByTestId('slot-{slotId}')"
      - selectedSlotTime: "getByTestId('selected-slot-time')"
      - confirmButton: "getByRole('button', {name: 'Confirm walk-in booking'})"
      - bookingReference: "getByTestId('walkin-booking-reference')"
    actions:
      - createWalkIn(patientSearchName, slotId): "Search patient, select slot, confirm"

  - name: "QueuePage"
    file: "pages/queue.page.ts"
    elements:
      - queueEntry: "getByTestId('queue-entry-{ref}')"
      - arrivedButton: "getByTestId('arrived-button-{ref}')"
      - arrivalTime: "getByTestId('arrival-time-{ref}')"
      - walkinBadge: "getByTestId('walkin-badge-{ref}')"
      - nextActionBadge: "getByTestId('next-action-badge-{ref}')"
    actions:
      - markArrived(bookingRef): "Click arrived button and verify status update"

  - name: "MedicalCodingPage"
    file: "pages/medical-coding.page.ts"
    elements:
      - icd10Suggestion: "getByTestId('icd10-suggestion-{code}')"
      - icd10Evidence: "getByTestId('icd10-evidence-{code}')"
      - cptSuggestion: "getByTestId('cpt-suggestion-{code}')"
      - cptEvidence: "getByTestId('cpt-evidence-{code}')"
      - confirmIcd10: "getByRole('button', {name: 'Confirm {code}'})"
      - confirmCpt: "getByRole('button', {name: 'Confirm {code}'})"
      - saveCodesButton: "getByRole('button', {name: 'Save confirmed codes'})"
      - codingCompleteBadge: "getByTestId('coding-completion-badge')"
    actions:
      - confirmCodes(icd10, cpt): "Confirm ICD-10 and CPT codes and save"
```

## Success Criteria

- [ ] Staff successfully authenticates and accesses walk-in interface (Phase 1)
- [ ] Walk-in booking created with correct patient, slot, and type=WalkIn (Phase 1)
- [ ] Patient visible in same-day queue with status=PendingArrival (Phase 1 → Phase 2)
- [ ] Arrived status updates in real time without page reload (Phase 2)
- [ ] AuditLog entry created for arrival action with staffId (Phase 2)
- [ ] Medical codes interface shows AI suggestions with supporting evidence (Phase 3)
- [ ] ICD-10 J06.9 and CPT 99213 confirmed and saved with staff timestamp (Phase 3)
- [ ] Visit disposition updated to CodingComplete (Phase 3)
- [ ] All API checkpoint verifications pass
- [ ] Journey executes without `waitForTimeout`; all assertions use web-first patterns
- [ ] Booking reference flows across all 3 phases without manual re-entry

### Journey Traceability

| Phase | Use Case | Functional Requirements |
|-------|----------|------------------------|
| 1 | UC-005 | FR-022, FR-023, FR-024, FR-025, FR-026 |
| 2 | UC-006 | FR-027, FR-028, FR-029, FR-030 |
| 3 | UC-009 | FR-044, FR-045, FR-046, FR-047 |

## Locator Reference

| Priority | Method | Example |
|----------|--------|---------|
| 1st | `getByRole` | `getByRole('button', {name: 'Confirm walk-in booking'})` |
| 2nd | `getByTestId` | `getByTestId('queue-entry-{ref}')` |
| 3rd | `getByLabel` | `getByLabel('Search patient by name or date of birth')` |
| AVOID | CSS | `.mat-card`, `#dynamic-id`, `nth-child` |

---

*Template: automated-e2e-template.md | Output: `.propel/context/test/e2e_staff_walkin_clinical_workflow_20260420.md`*
