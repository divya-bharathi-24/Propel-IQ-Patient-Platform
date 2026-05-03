---
feature: "Slot Management, Walk-In Queue & Clinical Intelligence"
source: ".propel/context/docs/spec.md"
use_cases: ["UC-004", "UC-005", "UC-006", "UC-007", "UC-008", "UC-009"]
base_url: "http://localhost:4200"
playwright_version: "1.x"
framework: "Angular 18"
generated_date: "2026-04-20"
---

# Test Workflow: Slot Management, Walk-In Queue & Clinical Intelligence

## Metadata

| Field | Value |
|-------|-------|
| Feature | Slot Management, Walk-In Queue & Clinical Intelligence |
| Source | `.propel/context/docs/spec.md` |
| Use Cases | UC-004, UC-005, UC-006, UC-007, UC-008, UC-009 |
| Base URL | `http://localhost:4200` |
| Framework | Angular 18 + Angular Material |
| Playwright Version | 1.x (TypeScript) |

---

## Test Cases

### Test Case Master List

| TC-ID | Summary | Use Case | Type | Priority |
|-------|---------|----------|------|----------|
| TC-UC004-HP-001 | Preferred slot swap executes within 60s after cancellation; email + SMS notifications sent | UC-004 | happy_path | P0 |
| TC-UC004-EC-001 | Multiple waitlisted patients for same slot: FIFO ordering respected | UC-004 | edge_case | P0 |
| TC-UC004-ER-001 | SMS delivery fails during swap notification; retry logged; email confirmed as fallback | UC-004 | error | P1 |
| TC-UC005-HP-001 | Staff creates walk-in booking for known patient and assigns to slot; patient appears in queue | UC-005 | happy_path | P1 |
| TC-UC005-EC-001 | Staff skips account creation during walk-in; anonymous visit tracked with temporary ID | UC-005 | edge_case | P1 |
| TC-UC005-ER-001 | No slots available for walk-in; patient added to overflow queue with wait estimate | UC-005 | error | P1 |
| TC-UC006-HP-001 | Staff marks patient as Arrived; appointment status updates; queue view refreshes in real time | UC-006 | happy_path | P1 |
| TC-UC006-EC-001 | Patient appointment not in today's queue; staff searches by reference number; record retrieved | UC-006 | edge_case | P1 |
| TC-UC006-ER-001 | Patient-role JWT attempts to mark Arrived; HTTP 403 returned | UC-006 | error | P0 |
| TC-UC007-HP-001 | Patient uploads 3 valid PDFs; all encrypted, stored, and queued for AI extraction | UC-007 | happy_path | P0 |
| TC-UC007-EC-001 | File exceeding 25 MB rejected; remaining valid files processed without interruption | UC-007 | edge_case | P1 |
| TC-UC007-ER-001 | Non-PDF file (.docx) rejected with supported format message | UC-007 | error | P1 |
| TC-UC008-HP-001 | 360° view: AI extraction → dedup → conflict detected → staff resolves → profile verified | UC-008 | happy_path | P0 |
| TC-UC008-EC-001 | No conflicts detected in extracted data; direct verification path (no conflict step shown) | UC-008 | edge_case | P1 |
| TC-UC008-ER-001 | Corrupted PDF extraction fails; document marked Extraction Failed; staff notified | UC-008 | error | P1 |
| TC-UC009-HP-001 | Staff confirms ICD-10 and CPT codes; confirmed codes saved with staff ID and timestamp | UC-009 | happy_path | P0 |
| TC-UC009-EC-001 | Staff manually adds ICD-10 code; validated against standard library before saving | UC-009 | edge_case | P1 |
| TC-UC009-ER-001 | Profile verification attempted before all conflicts resolved; blocked with 422 error | UC-009 | error | P0 |

---

### TC-UC004-HP-001: Preferred Slot Swap Executes Within 60 Seconds After Cancellation

**Type:** happy_path | **Priority:** P0

**Preconditions:**
- `swap-patient@propeliq.dev` has confirmed booking on `slot-A` and WaitlistEntry for `slot-B` (currently unavailable)
- `cancel-patient@propeliq.dev` holds `slot-B`
- Notification service mock captures email/SMS delivery with timestamps

**Steps:**

```yaml
steps:
  - step_id: "001"
    action: api_verify
    target: "GET /api/waitlist?patientEmail=swap-patient@propeliq.dev"
    expect: "WaitlistEntry with status=Active, preferredSlot=slot-B"

  - step_id: "002"
    action: api_call
    target: "POST /api/appointments/slot-B-cancel/cancel"
    auth: "cancel-patient JWT"
    expect: "HTTP 200; slot-B released; cancellation event fired"

  - step_id: "003"
    action: wait_for_condition
    target: "GET /api/appointments?patientEmail=swap-patient@propeliq.dev"
    condition: "appointment.slotId == 'slot-B'"
    timeout_seconds: 60
    expect: "Swap patient appointment updated to slot-B within 60 seconds"

  - step_id: "004"
    action: api_verify
    target: "GET /api/appointments/slot-A"
    expect: "slot-A status=Available (released to general pool)"

  - step_id: "005"
    action: api_verify
    target: "GET /api/waitlist?patientEmail=swap-patient@propeliq.dev"
    expect: "WaitlistEntry status=Swapped"

  - step_id: "006"
    action: api_verify
    target: "GET /api/notifications?patientEmail=swap-patient@propeliq.dev&type=slot_swap"
    expect: "1 Email notification + 1 SMS notification both with status=Sent; new appointment date/time present"

  - step_id: "007"
    action: navigate
    target: "http://localhost:4200/dashboard"
    auth: "swap-patient JWT"
    expect: "Patient dashboard loaded"

  - step_id: "008"
    action: verify
    target: "getByTestId('upcoming-appointment-slot')"
    expect: "shows slot-B date/time (not slot-A)"
```

**Test Data:**

```yaml
test_data:
  swap_patient: "swap-patient@propeliq.dev"
  cancel_patient: "cancel-patient@propeliq.dev"
  current_slot: "slot-A"
  preferred_slot: "slot-B"
  swap_timeout_seconds: 60
```

---

### TC-UC004-EC-001: Multiple Waitlisted Patients — FIFO Ordering Respected

**Type:** edge_case | **Priority:** P0

**Scenario:** Three patients (A, B, C) are waitlisted for `slot-Z` with enrollment timestamps T1 < T2 < T3. When slot-Z opens, Patient A (earliest) receives the swap; B and C remain waitlisted.

**Preconditions:**
- Patients A, B, C all waitlisted for `slot-Z` in FIFO order
- Patient D holds `slot-Z` and will cancel

**Steps:**

```yaml
steps:
  - step_id: "EC001"
    action: api_verify
    target: "GET /api/waitlist?slotId=slot-Z&sort=enrolledAt"
    expect: "3 entries ordered: patient-A (T1), patient-B (T2), patient-C (T3)"

  - step_id: "EC002"
    action: api_call
    target: "POST /api/appointments/slot-Z-booking/cancel"
    auth: "patient-D JWT"
    expect: "HTTP 200; slot-Z released"

  - step_id: "EC003"
    action: wait_for_condition
    target: "GET /api/appointments?patientEmail=patient-a@propeliq.dev"
    condition: "appointment.slotId == 'slot-Z'"
    timeout_seconds: 60
    expect: "Patient A receives the swap"

  - step_id: "EC004"
    action: api_verify
    target: "GET /api/waitlist?patientEmail=patient-b@propeliq.dev&slotId=slot-Z"
    expect: "Patient B WaitlistEntry still status=Active (not swapped)"

  - step_id: "EC005"
    action: api_verify
    target: "GET /api/waitlist?patientEmail=patient-c@propeliq.dev&slotId=slot-Z"
    expect: "Patient C WaitlistEntry still status=Active (not swapped)"
```

**Test Data:**

```yaml
test_data:
  waitlisted_patients:
    - email: "patient-a@propeliq.dev"
      enrollment_order: 1
    - email: "patient-b@propeliq.dev"
      enrollment_order: 2
    - email: "patient-c@propeliq.dev"
      enrollment_order: 3
  slot: "slot-Z"
  cancelling_patient: "patient-d@propeliq.dev"
```

---

### TC-UC004-ER-001: SMS Delivery Fails During Swap Notification — Retry Logged; Email Confirmed

**Type:** error | **Priority:** P1

**Trigger:** Twilio SMS endpoint returns 503 during slot swap notification delivery.

**Preconditions:**
- Swap executed for `swap-er-patient@propeliq.dev`
- Twilio mock configured to return 503 for SMS; retry after 5 minutes returns 200

**Steps:**

```yaml
steps:
  - step_id: "ER001"
    action: api_verify
    target: "GET /api/notifications?patientEmail=swap-er-patient@propeliq.dev&channel=SMS"
    expect: "SMS Notification record with status=Failed, retryCount=1"

  - step_id: "ER002"
    action: api_verify
    target: "GET /api/notifications?patientEmail=swap-er-patient@propeliq.dev&channel=Email"
    expect: "Email Notification record with status=Sent (email treated as confirmation fallback)"

  - step_id: "ER003"
    action: api_verify
    target: "GET /api/audit?entityType=Notification&action=Retry"
    expect: "AuditLog entry for SMS retry with timestamp within 5 minutes of initial failure"
```

**Test Data:**

```yaml
test_data:
  patient: "swap-er-patient@propeliq.dev"
  sms_mock: "503 Service Unavailable"
  retry_delay_minutes: 5
```

---

### TC-UC005-HP-001: Staff Creates Walk-In Booking for Known Patient

**Type:** happy_path | **Priority:** P1

**Preconditions:**
- Staff authenticated as `staff@propeliq.dev`
- Patient `john.smith@propeliq.dev` exists in system
- Walk-in slot `slot-walkin-001` available on today's date

**Steps:**

```yaml
steps:
  - step_id: "001"
    action: navigate
    target: "http://localhost:4200/staff/walkin"
    expect: "Walk-in booking interface loaded"

  - step_id: "002"
    action: fill
    target: "getByLabel('Search patient by name or date of birth')"
    value: "John Smith"
    expect: "Search input accepts text"

  - step_id: "003"
    action: click
    target: "getByRole('button', {name: 'Search'})"
    expect: "Search results displayed"

  - step_id: "004"
    action: click
    target: "getByTestId('patient-result-john-smith')"
    expect: "Patient selected; walk-in booking form shown"

  - step_id: "005"
    action: click
    target: "getByTestId('slot-walkin-001')"
    expect: "Slot selected"

  - step_id: "006"
    action: click
    target: "getByRole('button', {name: 'Confirm walk-in booking'})"
    expect: "Walk-in booking confirmed"

  - step_id: "007"
    action: verify
    target: "getByRole('alert')"
    expect: "contains text 'Walk-in booking confirmed'"

  - step_id: "008"
    action: navigate
    target: "http://localhost:4200/staff/queue"
    expect: "Same-day queue view loaded"

  - step_id: "009"
    action: verify
    target: "getByTestId('queue-entry-john-smith')"
    expect: "John Smith visible in queue with status 'Pending Arrival' and booking type 'Walk-in'"
```

**Test Data:**

```yaml
test_data:
  staff_email: "staff@propeliq.dev"
  patient_name: "John Smith"
  patient_email: "john.smith@propeliq.dev"
  slot_id: "slot-walkin-001"
  slot_time: "14:00"
```

---

### TC-UC005-EC-001: Staff Skips Account Creation — Anonymous Walk-In Tracked

**Type:** edge_case | **Priority:** P1

**Scenario:** Staff creates walk-in without creating a patient account. System generates a temporary visit ID for anonymous tracking.

**Steps:**

```yaml
steps:
  - step_id: "EC001"
    action: navigate
    target: "http://localhost:4200/staff/walkin"
    expect: "Walk-in interface loaded"

  - step_id: "EC002"
    action: fill
    target: "getByLabel('Search patient by name or date of birth')"
    value: "Unknown Patient"
    expect: "No results found message shown"

  - step_id: "EC003"
    action: click
    target: "getByRole('button', {name: 'Skip account creation'})"
    expect: "Anonymous walk-in form shown"

  - step_id: "EC004"
    action: click
    target: "getByTestId('slot-walkin-anon-001')"
    expect: "Slot selected for anonymous visit"

  - step_id: "EC005"
    action: click
    target: "getByRole('button', {name: 'Confirm anonymous walk-in'})"
    expect: "Anonymous booking confirmed"

  - step_id: "EC006"
    action: verify
    target: "getByTestId('anonymous-visit-id')"
    expect: "Temporary visit ID displayed (non-empty, formatted as TEMP-XXXXXX)"

  - step_id: "EC007"
    action: navigate
    target: "http://localhost:4200/staff/queue"
    expect: "Queue view loaded"

  - step_id: "EC008"
    action: verify
    target: "getByTestId('queue-entry-anonymous')"
    expect: "Anonymous entry visible with temp visit ID"
```

**Test Data:**

```yaml
test_data:
  slot_id: "slot-walkin-anon-001"
  anonymous_id_pattern: "TEMP-[A-Z0-9]{6}"
```

---

### TC-UC005-ER-001: No Slots Available — Patient Added to Overflow Queue

**Type:** error | **Priority:** P1

**Trigger:** All appointment slots for today are fully booked when staff attempts walk-in.

**Steps:**

```yaml
steps:
  - step_id: "ER001"
    action: navigate
    target: "http://localhost:4200/staff/walkin"
    expect: "Walk-in interface loaded"

  - step_id: "ER002"
    action: fill
    target: "getByLabel('Search patient by name or date of birth')"
    value: "Jane Overflow"
    expect: "Patient found"

  - step_id: "ER003"
    action: click
    target: "getByTestId('patient-result-jane-overflow')"
    expect: "Patient selected"

  - step_id: "ER004"
    action: verify
    target: "getByTestId('no-slots-banner')"
    expect: "banner visible: 'No available slots for today'"

  - step_id: "ER005"
    action: click
    target: "getByRole('button', {name: 'Add to overflow queue'})"
    expect: "Patient added to overflow queue"

  - step_id: "ER006"
    action: verify
    target: "getByTestId('overflow-wait-estimate')"
    expect: "Wait time estimate displayed (e.g., '~45 minutes')"
```

**Test Data:**

```yaml
test_data:
  patient: "jane.overflow@propeliq.dev"
  slot_availability: "all_full"
```

---

### TC-UC006-HP-001: Staff Marks Patient as Arrived; Queue Updates in Real Time

**Type:** happy_path | **Priority:** P1

**Preconditions:**
- Staff authenticated; same-day queue has `john.smith@propeliq.dev` with appointment `appt-queue-001`
- Appointment status is `Booked`

**Steps:**

```yaml
steps:
  - step_id: "001"
    action: navigate
    target: "http://localhost:4200/staff/queue"
    expect: "Same-day queue view loaded with today's appointments"

  - step_id: "002"
    action: verify
    target: "getByTestId('queue-entry-appt-queue-001')"
    expect: "visible with status 'Pending Arrival'"

  - step_id: "003"
    action: click
    target: "getByTestId('arrived-button-appt-queue-001')"
    expect: "Confirmation prompt or immediate update"

  - step_id: "004"
    action: verify
    target: "getByTestId('queue-entry-appt-queue-001')"
    expect: "status updates to 'Arrived' with arrival timestamp (no full page reload)"

  - step_id: "005"
    action: api_verify
    target: "GET /api/appointments/appt-queue-001"
    expect: "status=Arrived; arrivalTime is non-null UTC timestamp"

  - step_id: "006"
    action: api_verify
    target: "GET /api/audit?entityType=Appointment&entityId=appt-queue-001&action=Update"
    expect: "AuditLog entry with staffId, action=Update, before=Booked, after=Arrived"
```

**Test Data:**

```yaml
test_data:
  staff_email: "staff@propeliq.dev"
  appointment_id: "appt-queue-001"
  patient_name: "John Smith"
  expected_status: "Arrived"
```

---

### TC-UC006-EC-001: Patient Not in Today's Queue — Staff Searches by Reference Number

**Type:** edge_case | **Priority:** P1

**Scenario:** Patient `ref-search-patient` has an appointment but is not visible in the default today queue view. Staff searches by reference number and marks Arrived.

**Steps:**

```yaml
steps:
  - step_id: "EC001"
    action: navigate
    target: "http://localhost:4200/staff/queue"
    expect: "Today's queue loaded; patient 'ref-search-patient' not visible in list"

  - step_id: "EC002"
    action: fill
    target: "getByLabel('Search by patient name or reference')"
    value: "REF-20260420-999"
    expect: "Input accepted"

  - step_id: "EC003"
    action: click
    target: "getByRole('button', {name: 'Search'})"
    expect: "Appointment with REF-20260420-999 retrieved and displayed"

  - step_id: "EC004"
    action: click
    target: "getByTestId('arrived-button-ref-search')"
    expect: "Status updated to Arrived"

  - step_id: "EC005"
    action: verify
    target: "getByTestId('queue-entry-ref-search')"
    expect: "now visible in queue with status 'Arrived'"
```

**Test Data:**

```yaml
test_data:
  reference_number: "REF-20260420-999"
  patient: "ref-search-patient@propeliq.dev"
```

---

### TC-UC006-ER-001: Patient-Role JWT Attempts to Mark Arrived — HTTP 403 Returned

**Type:** error | **Priority:** P0

**Trigger:** A Patient-role user attempts to call the arrived-marking endpoint directly (no patient self-check-in permitted per FR-027).

**Steps:**

```yaml
steps:
  - step_id: "ER001"
    action: api_call
    target: "PATCH /api/appointments/appt-queue-001/arrived"
    auth: "patient JWT"
    expect: "HTTP 403 Forbidden"

  - step_id: "ER002"
    action: verify_response
    expect: "body contains 'Forbidden' with no appointment data leaked"

  - step_id: "ER003"
    action: verify
    target: "no self-check-in UI element visible on patient dashboard"
    expect: "getByTestId('self-checkin-button') does NOT exist"
```

**Test Data:**

```yaml
test_data:
  patient_jwt: "patient-role token"
  appointment_id: "appt-queue-001"
```

---

### TC-UC007-HP-001: Patient Uploads 3 Valid PDFs — Encrypted, Stored, Queued

**Type:** happy_path | **Priority:** P0

**Preconditions:**
- Patient authenticated as `upload.patient@propeliq.dev`
- 3 test PDFs prepared: `clinical_doc_1.pdf` (2MB), `clinical_doc_2.pdf` (5MB), `clinical_doc_3.pdf` (10MB)
- All < 25MB limit

**Steps:**

```yaml
steps:
  - step_id: "001"
    action: navigate
    target: "http://localhost:4200/dashboard"
    expect: "Patient dashboard loaded"

  - step_id: "002"
    action: click
    target: "getByRole('link', {name: 'Upload documents'})"
    expect: "Document upload interface loaded"

  - step_id: "003"
    action: set_input_files
    target: "getByTestId('document-upload-input')"
    files: ["fixtures/clinical_doc_1.pdf", "fixtures/clinical_doc_2.pdf", "fixtures/clinical_doc_3.pdf"]
    expect: "3 files staged for upload"

  - step_id: "004"
    action: verify
    target: "getByTestId('upload-file-list')"
    expect: "3 files listed with sizes and PDF icon indicators"

  - step_id: "005"
    action: click
    target: "getByRole('button', {name: 'Upload documents'})"
    expect: "Upload initiated"

  - step_id: "006"
    action: verify
    target: "getByRole('progressbar')"
    expect: "Upload progress bar visible during upload"

  - step_id: "007"
    action: verify
    target: "getByTestId('upload-success-banner')"
    expect: "Success message: '3 documents uploaded successfully'"

  - step_id: "008"
    action: verify
    target: "getByTestId('document-history-list')"
    expect: "3 new entries with status 'Processing'"

  - step_id: "009"
    action: api_verify
    target: "GET /api/documents?patientEmail=upload.patient@propeliq.dev"
    expect: "3 ClinicalDocument records with processingStatus=Pending; storagePath non-null and encrypted"
```

**Test Data:**

```yaml
test_data:
  patient_email: "upload.patient@propeliq.dev"
  files:
    - path: "fixtures/clinical_doc_1.pdf"
      size_mb: 2
    - path: "fixtures/clinical_doc_2.pdf"
      size_mb: 5
    - path: "fixtures/clinical_doc_3.pdf"
      size_mb: 10
  max_file_size_mb: 25
  max_batch_files: 20
```

---

### TC-UC007-EC-001: File Exceeding 25 MB Rejected; Valid Files Proceed

**Type:** edge_case | **Priority:** P1

**Scenario:** Patient uploads 3 files; 1 exceeds 25 MB limit. The oversized file is rejected with an error; the other 2 files proceed to upload successfully.

**Steps:**

```yaml
steps:
  - step_id: "EC001"
    action: navigate
    target: "http://localhost:4200/dashboard/documents"
    expect: "Document upload page loaded"

  - step_id: "EC002"
    action: set_input_files
    target: "getByTestId('document-upload-input')"
    files: ["fixtures/large_doc_30mb.pdf", "fixtures/clinical_doc_1.pdf", "fixtures/clinical_doc_2.pdf"]
    expect: "Files staged"

  - step_id: "EC003"
    action: click
    target: "getByRole('button', {name: 'Upload documents'})"
    expect: "Upload initiated"

  - step_id: "EC004"
    action: verify
    target: "getByTestId('file-error-large_doc_30mb')"
    expect: "Error: 'File exceeds 25 MB limit and was not uploaded'"

  - step_id: "EC005"
    action: verify
    target: "getByTestId('upload-success-banner')"
    expect: "contains '2 documents uploaded successfully'"

  - step_id: "EC006"
    action: api_verify
    target: "GET /api/documents?patientEmail=upload.ec.patient@propeliq.dev"
    expect: "2 ClinicalDocument records (not 3); no record for large_doc_30mb"
```

**Test Data:**

```yaml
test_data:
  files:
    - path: "fixtures/large_doc_30mb.pdf"
      size_mb: 30
      expect_rejected: true
    - path: "fixtures/clinical_doc_1.pdf"
      size_mb: 2
      expect_rejected: false
    - path: "fixtures/clinical_doc_2.pdf"
      size_mb: 5
      expect_rejected: false
```

---

### TC-UC007-ER-001: Non-PDF File Rejected with Supported Format Message

**Type:** error | **Priority:** P1

**Trigger:** Patient selects a .docx file instead of PDF.

**Steps:**

```yaml
steps:
  - step_id: "ER001"
    action: navigate
    target: "http://localhost:4200/dashboard/documents"
    expect: "Document upload page loaded"

  - step_id: "ER002"
    action: set_input_files
    target: "getByTestId('document-upload-input')"
    files: ["fixtures/clinical_notes.docx"]
    expect: "File staged (or rejected by client-side validation)"

  - step_id: "ER003"
    action: verify
    target: "getByTestId('file-error-clinical_notes')"
    expect: "Error: 'Only PDF files are supported. Please convert to PDF and try again.'"

  - step_id: "ER004"
    action: api_verify
    target: "GET /api/documents?fileName=clinical_notes.docx"
    expect: "No ClinicalDocument record created for the rejected file"
```

**Test Data:**

```yaml
test_data:
  rejected_file: "fixtures/clinical_notes.docx"
  supported_format: "PDF"
```

---

### TC-UC008-HP-001: 360-Degree View — Extract, Conflict Detection, Staff Verification

**Type:** happy_path | **Priority:** P0

**Preconditions:**
- Patient `view.patient@propeliq.dev` has 2 uploaded and extracted documents
- Document 1: BP=130/85, Metformin 500mg
- Document 2: BP=128/82, Metformin 1000mg (dosage conflict)
- AI extraction completed; `DataConflict` record exists for Metformin dosage
- Staff authenticated as `staff@propeliq.dev`

**Steps:**

```yaml
steps:
  - step_id: "001"
    action: navigate
    target: "http://localhost:4200/staff/patients/view-patient-id/360"
    expect: "360-degree patient view loaded within 2 minutes (FR-048 SLA)"

  - step_id: "002"
    action: verify
    target: "getByRole('heading', {name: '360° Patient View'})"
    expect: "heading visible"

  - step_id: "003"
    action: verify
    target: "getByTestId('conflict-indicator-medication')"
    expect: "conflict indicator visible with severity badge 'Critical'"

  - step_id: "004"
    action: click
    target: "getByRole('button', {name: 'Verify profile'})"
    expect: "HTTP 422; verification blocked"

  - step_id: "005"
    action: verify
    target: "getByRole('alert')"
    expect: "contains text 'Resolve all conflicts before verifying'"

  - step_id: "006"
    action: click
    target: "getByTestId('conflict-indicator-medication')"
    expect: "Conflict detail panel opens showing both values and source documents"

  - step_id: "007"
    action: verify
    target: "getByTestId('conflict-value-1')"
    expect: "shows 'Metformin 500mg — Document 1'"

  - step_id: "008"
    action: verify
    target: "getByTestId('conflict-value-2')"
    expect: "shows 'Metformin 1000mg — Document 2'"

  - step_id: "009"
    action: click
    target: "getByRole('button', {name: 'Select Metformin 500mg'})"
    expect: "Authoritative value selected; conflict panel updates to 'Resolved'"

  - step_id: "010"
    action: click
    target: "getByRole('button', {name: 'Verify profile'})"
    expect: "HTTP 200; profile marked as Verified"

  - step_id: "011"
    action: verify
    target: "getByTestId('profile-status-badge')"
    expect: "shows 'Verified'"

  - step_id: "012"
    action: api_verify
    target: "GET /api/patients/view-patient-id/360"
    expect: "status=Verified; all conflicts resolutionStatus=Resolved"
```

**Test Data:**

```yaml
test_data:
  staff_email: "staff@propeliq.dev"
  patient_id: "view-patient-id"
  conflict:
    field: "Metformin dosage"
    value_1: "500mg"
    source_1: "Document 1"
    value_2: "1000mg"
    source_2: "Document 2"
  authoritative_value: "Metformin 500mg"
```

---

### TC-UC008-EC-001: No Conflicts Detected — Direct Verification Path

**Type:** edge_case | **Priority:** P1

**Scenario:** All extracted data is consistent across documents. No DataConflict records exist. Staff proceeds directly to verification without conflict resolution step.

**Steps:**

```yaml
steps:
  - step_id: "EC001"
    action: navigate
    target: "http://localhost:4200/staff/patients/noconflict-patient-id/360"
    expect: "360-degree view loaded"

  - step_id: "EC002"
    action: verify
    target: "getByTestId('no-conflicts-banner')"
    expect: "visible: 'No data conflicts detected'"

  - step_id: "EC003"
    action: verify
    target: "getByTestId('conflict-indicator-medication')"
    expect: "does NOT exist in DOM"

  - step_id: "EC004"
    action: click
    target: "getByRole('button', {name: 'Verify profile'})"
    expect: "HTTP 200; profile verified immediately"

  - step_id: "EC005"
    action: verify
    target: "getByTestId('profile-status-badge')"
    expect: "shows 'Verified'"
```

**Test Data:**

```yaml
test_data:
  patient_id: "noconflict-patient-id"
  conflict_count: 0
```

---

### TC-UC008-ER-001: Corrupted PDF Extraction Fails — Document Marked Extraction Failed

**Type:** error | **Priority:** P1

**Trigger:** An uploaded PDF is corrupted or password-protected; AI extraction cannot process it.

**Steps:**

```yaml
steps:
  - step_id: "ER001"
    action: navigate
    target: "http://localhost:4200/staff/patients/corrupt-patient-id/360"
    expect: "360-degree view loaded"

  - step_id: "ER002"
    action: verify
    target: "getByTestId('document-status-corrupted_doc')"
    expect: "shows 'Extraction Failed'"

  - step_id: "ER003"
    action: verify
    target: "getByTestId('extraction-failed-notice')"
    expect: "notice visible: 'One document could not be processed. Please review manually.'"

  - step_id: "ER004"
    action: api_verify
    target: "GET /api/documents?patientId=corrupt-patient-id"
    expect: "ClinicalDocument with processingStatus=Failed; other documents unaffected"
```

**Test Data:**

```yaml
test_data:
  patient_id: "corrupt-patient-id"
  corrupted_file: "corrupted_doc.pdf"
```

---

### TC-UC009-HP-001: Staff Confirms ICD-10 and CPT Codes; Saved with Staff Timestamp

**Type:** happy_path | **Priority:** P0

**Preconditions:**
- Staff authenticated; patient `code-patient-id` has verified 360° profile
- AI has suggested: ICD-10 `I10` (Hypertension) and CPT `99213` (Office visit)

**Steps:**

```yaml
steps:
  - step_id: "001"
    action: navigate
    target: "http://localhost:4200/staff/patients/code-patient-id/codes"
    expect: "Medical code review interface loaded"

  - step_id: "002"
    action: verify
    target: "getByTestId('icd10-suggestion-I10')"
    expect: "ICD-10 code I10 visible with description 'Essential hypertension' and supporting evidence"

  - step_id: "003"
    action: verify
    target: "getByTestId('cpt-suggestion-99213')"
    expect: "CPT code 99213 visible with description 'Office/outpatient visit' and evidence"

  - step_id: "004"
    action: click
    target: "getByRole('button', {name: 'Confirm I10'})"
    expect: "ICD-10 I10 confirmed"

  - step_id: "005"
    action: click
    target: "getByRole('button', {name: 'Confirm 99213'})"
    expect: "CPT 99213 confirmed"

  - step_id: "006"
    action: click
    target: "getByRole('button', {name: 'Save confirmed codes'})"
    expect: "Codes saved with confirmation timestamp"

  - step_id: "007"
    action: verify
    target: "getByRole('alert')"
    expect: "contains text 'Medical codes confirmed and saved'"

  - step_id: "008"
    action: api_verify
    target: "GET /api/medicalcodes?patientId=code-patient-id"
    expect: "MedicalCode records: I10 verificationStatus=Accepted, 99213 verificationStatus=Accepted; verifiedBy=staffId; verifiedAt non-null"
```

**Test Data:**

```yaml
test_data:
  staff_email: "staff@propeliq.dev"
  patient_id: "code-patient-id"
  icd10_code: "I10"
  cpt_code: "99213"
```

---

### TC-UC009-EC-001: Staff Manually Adds ICD-10 Code Validated Against Standard Library

**Type:** edge_case | **Priority:** P1

**Scenario:** Staff adds a code not suggested by AI. System validates it against the ICD-10 standard library before saving.

**Steps:**

```yaml
steps:
  - step_id: "EC001"
    action: navigate
    target: "http://localhost:4200/staff/patients/code-patient-id/codes"
    expect: "Code review interface loaded"

  - step_id: "EC002"
    action: click
    target: "getByRole('button', {name: 'Add code manually'})"
    expect: "Manual code entry form shown"

  - step_id: "EC003"
    action: fill
    target: "getByLabel('ICD-10 code')"
    value: "E11.9"
    expect: "Input accepted"

  - step_id: "EC004"
    action: click
    target: "getByRole('button', {name: 'Validate code'})"
    expect: "Code validated against library; description displayed"

  - step_id: "EC005"
    action: verify
    target: "getByTestId('code-validation-result')"
    expect: "shows 'E11.9 — Type 2 diabetes mellitus without complications (Valid)'"

  - step_id: "EC006"
    action: click
    target: "getByRole('button', {name: 'Add to confirmed codes'})"
    expect: "Code added to confirmed set"

  - step_id: "EC007"
    action: fill
    target: "getByLabel('ICD-10 code')"
    value: "INVALID-999"
    expect: "Input accepted"

  - step_id: "EC008"
    action: click
    target: "getByRole('button', {name: 'Validate code'})"
    expect: "Validation fails"

  - step_id: "EC009"
    action: verify
    target: "getByTestId('code-validation-result')"
    expect: "shows 'INVALID-999 — Code not found in ICD-10 library'"
```

**Test Data:**

```yaml
test_data:
  valid_manual_code: "E11.9"
  valid_code_description: "Type 2 diabetes mellitus without complications"
  invalid_code: "INVALID-999"
```

---

### TC-UC009-ER-001: Profile Verification Blocked When Unresolved Conflicts Exist

**Type:** error | **Priority:** P0

**Trigger:** Staff attempts to verify 360° profile while DataConflict records with resolutionStatus=Unresolved exist.

**Steps:**

```yaml
steps:
  - step_id: "ER001"
    action: navigate
    target: "http://localhost:4200/staff/patients/conflict-patient-id/360"
    expect: "360-degree view loaded; conflict indicators visible"

  - step_id: "ER002"
    action: click
    target: "getByRole('button', {name: 'Verify profile'})"
    expect: "Request blocked"

  - step_id: "ER003"
    action: verify
    target: "getByRole('alert')"
    expect: "contains text 'Resolve all conflicts before verifying'"

  - step_id: "ER004"
    action: api_verify
    target: "GET /api/patients/conflict-patient-id/360"
    expect: "status=Unverified (not changed)"
```

**Test Data:**

```yaml
test_data:
  patient_id: "conflict-patient-id"
  unresolved_conflicts: 1
```

---

## Page Objects

```yaml
pages:
  - name: "WalkInPage"
    file: "pages/walk-in.page.ts"
    elements:
      - searchInput: "getByLabel('Search patient by name or date of birth')"
      - searchButton: "getByRole('button', {name: 'Search'})"
      - patientResult: "getByTestId('patient-result-{patientId}')"
      - slotCard: "getByTestId('slot-{slotId}')"
      - confirmButton: "getByRole('button', {name: 'Confirm walk-in booking'})"
      - skipAccountButton: "getByRole('button', {name: 'Skip account creation'})"
      - anonymousVisitId: "getByTestId('anonymous-visit-id')"
    actions:
      - searchPatient(name): "Fill search and click Search"
      - confirmWalkIn(): "Click confirm walk-in button"

  - name: "QueuePage"
    file: "pages/queue.page.ts"
    elements:
      - queueEntry: "getByTestId('queue-entry-{appointmentId}')"
      - arrivedButton: "getByTestId('arrived-button-{appointmentId}')"
      - searchInput: "getByLabel('Search by patient name or reference')"
    actions:
      - markArrived(appointmentId): "Click arrived button for appointment"
      - searchByReference(ref): "Fill search and submit"

  - name: "DocumentUploadPage"
    file: "pages/document-upload.page.ts"
    elements:
      - fileInput: "getByTestId('document-upload-input')"
      - uploadButton: "getByRole('button', {name: 'Upload documents'})"
      - progressBar: "getByRole('progressbar')"
      - fileList: "getByTestId('upload-file-list')"
      - successBanner: "getByTestId('upload-success-banner')"
      - documentHistory: "getByTestId('document-history-list')"
    actions:
      - uploadFiles(files): "Set input files and click upload"

  - name: "ThreeSixtyViewPage"
    file: "pages/three-sixty-view.page.ts"
    elements:
      - profileStatusBadge: "getByTestId('profile-status-badge')"
      - conflictIndicator: "getByTestId('conflict-indicator-{fieldName}')"
      - conflictValue1: "getByTestId('conflict-value-1')"
      - conflictValue2: "getByTestId('conflict-value-2')"
      - verifyButton: "getByRole('button', {name: 'Verify profile'})"
    actions:
      - resolveConflict(fieldName, value): "Click conflict indicator and select authoritative value"
      - verifyProfile(): "Click verify profile button"

  - name: "MedicalCodingPage"
    file: "pages/medical-coding.page.ts"
    elements:
      - icd10Suggestion: "getByTestId('icd10-suggestion-{code}')"
      - cptSuggestion: "getByTestId('cpt-suggestion-{code}')"
      - confirmCodeButton: "getByRole('button', {name: 'Confirm {code}'})"
      - saveCodesButton: "getByRole('button', {name: 'Save confirmed codes'})"
      - addManuallyButton: "getByRole('button', {name: 'Add code manually'})"
      - codeInput: "getByLabel('ICD-10 code')"
      - validateButton: "getByRole('button', {name: 'Validate code'})"
      - validationResult: "getByTestId('code-validation-result')"
    actions:
      - confirmCode(code): "Click confirm button for given code"
      - addManualCode(code): "Enter code, validate, and add to confirmed set"
```

## Success Criteria

- [ ] All happy path steps execute without errors
- [ ] Slot swap FIFO ordering validated with concurrent waitlist scenario
- [ ] Walk-in anonymous tracking generates valid temporary IDs
- [ ] File size and type validations enforced client-side and server-side
- [ ] 360° view conflict gate prevents premature verification
- [ ] Medical code confirmation recorded with staff ID and timestamp
- [ ] All tests run independently with no shared state
- [ ] External APIs (Twilio, AI provider) intercepted with route mocks

## Locator Reference

| Priority | Method | Example |
|----------|--------|---------|
| 1st | `getByRole` | `getByRole('button', {name: 'Verify profile'})` |
| 2nd | `getByTestId` | `getByTestId('conflict-indicator-medication')` |
| 3rd | `getByLabel` | `getByLabel('Search patient by name or date of birth')` |
| AVOID | CSS | `.mat-card`, `#dynamic-id`, `nth-child` |

---

*Template: automated-testing-template.md | Output: `.propel/context/test/tw_slot_clinical_intelligence_20260420.md`*
