---
feature: "Patient Onboarding — Document Upload & Profile Verification"
source: ".propel/context/test/e2e_patient_onboarding_20260420.md"
use_cases: ["UC-007", "UC-008"]
base_url: "http://localhost:4200"
playwright_version: "1.x"
framework: "Angular 18"
generated_date: "2026-05-04"
---

# Test Workflow: Patient Onboarding — Document Upload & Profile Verification

## Metadata

| Field | Value |
|-------|-------|
| Feature | Patient Onboarding — Document Upload & Profile Verification |
| Source | `.propel/context/test/e2e_patient_onboarding_20260420.md` |
| Use Cases | UC-007, UC-008 |
| Base URL | `http://localhost:4200` |
| Framework | Angular 18 + Angular Material |
| Playwright Version | 1.x (TypeScript) |

---

## Test Cases

### Test Case Master List

| TC-ID | Summary | Use Case | Type | Priority |
|-------|---------|----------|------|----------|
| TC-UC007-HP-001 | Patient uploads 2 PDFs; progress shown; success banner confirms count; history shows Processing | UC-007 | happy_path | P0 |
| TC-UC007-EC-001 | Patient uploads file exceeding 10 MB limit; per-file size validation error shown | UC-007 | edge_case | P1 |
| TC-UC007-ER-001 | Patient uploads non-PDF file; invalid type error shown; upload blocked | UC-007 | error | P1 |
| TC-UC008-HP-001 | Staff resolves Lisinopril dosage conflict, verifies profile; status=Verified; intake data visible | UC-008 | happy_path | P0 |
| TC-UC008-EC-001 | Staff attempts profile verification while conflict unresolved; HTTP 422; error banner shown | UC-008 | edge_case | P1 |
| TC-UC008-ER-001 | Patient-role user attempts to access 360° view; HTTP 403 Forbidden | UC-008 | error | P1 |

---

### TC-UC007-HP-001: Patient Uploads 2 Clinical PDFs Successfully

**Type:** happy_path | **Priority:** P0

**Preconditions:**
- Patient authenticated with `uc007.patient@propeliq.dev` (session cookie active)
- Document upload endpoint `POST /api/documents` mocked to return 2 records with `processingStatus=Pending`
- `document-upload-input` accepts `application/pdf`

**Steps:**

```yaml
steps:
  - step_id: "001"
    action: navigate
    target: "http://localhost:4200/dashboard/documents"
    expect: "Document upload page loaded; upload input visible"

  - step_id: "002"
    action: set_input_files
    target: "getByTestId('document-upload-input')"
    value: ["uc007_doc_1.pdf", "uc007_doc_2.pdf"]
    expect: "2 files staged in input"

  - step_id: "003"
    action: verify
    target: "getByTestId('upload-file-list')"
    expect: "contains text 'uc007_doc_1.pdf' and 'uc007_doc_2.pdf'"

  - step_id: "004"
    action: click
    target: "getByRole('button', {name: 'Upload documents'})"
    expect: "Upload initiated"

  - step_id: "005"
    action: verify
    target: "getByRole('progressbar')"
    expect: "visible during upload"

  - step_id: "006"
    action: verify
    target: "getByTestId('upload-success-banner')"
    expect: "contains text '2 documents uploaded successfully'"

  - step_id: "007"
    action: verify
    target: "getByTestId('document-history-list')"
    expect: "visible with 2 entries"

  - step_id: "008"
    action: api_verify
    target: "GET /api/documents?patientEmail=uc007.patient@propeliq.dev"
    expect: "2 ClinicalDocument records; processingStatus=Pending; storagePath non-null"
```

**Test Data:**

```yaml
test_data:
  email: "uc007.patient@propeliq.dev"
  password: "UC007P@ss!"
  files:
    - name: "uc007_doc_1.pdf"
      mimeType: "application/pdf"
    - name: "uc007_doc_2.pdf"
      mimeType: "application/pdf"
  expectedUploadCount: 2
```

---

### TC-UC007-EC-001: Patient Uploads File Exceeding Maximum Size Limit

**Type:** edge_case | **Priority:** P1

**Scenario:** A single file larger than the 10 MB platform limit is staged. The UI should reject it with a per-file error before the upload button is clicked, enforcing client-side and server-side size validation.

**Preconditions:**
- Patient authenticated
- Document upload page loaded

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
    value: ["oversized_doc.pdf"]
    note: "File buffer is 11 MB to exceed 10 MB limit"
    expect: "File staged; validation runs client-side"

  - step_id: "EC003"
    action: verify
    target: "getByTestId('file-error-oversized_doc')"
    expect: "contains text 'exceeds' or 'size limit'"

  - step_id: "EC004"
    action: verify
    target: "getByRole('button', {name: 'Upload documents'})"
    expect: "disabled or absent when invalid file present"
```

**Test Data:**

```yaml
test_data:
  email: "uc007.patient@propeliq.dev"
  password: "UC007P@ss!"
  oversized_file:
    name: "oversized_doc.pdf"
    mimeType: "application/pdf"
    sizeMb: 11
```

---

### TC-UC007-ER-001: Patient Uploads Non-PDF File — Type Validation Error

**Type:** error | **Priority:** P1

**Trigger:** Patient stages a `.txt` file where only `application/pdf` is accepted. The upload must be blocked and a per-file error displayed.

**Preconditions:**
- Patient authenticated
- Document upload page loaded

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
    value: ["invalid_file.txt"]
    expect: "File staged"

  - step_id: "ER003"
    action: verify
    target: "getByTestId('file-error-invalid_file')"
    expect: "contains text 'must be a PDF' or 'invalid file type'"

  - step_id: "ER004"
    action: verify
    target: "getByRole('button', {name: 'Upload documents'})"
    expect: "disabled or absent"
```

**Test Data:**

```yaml
test_data:
  email: "uc007.patient@propeliq.dev"
  password: "UC007P@ss!"
  invalid_file:
    name: "invalid_file.txt"
    mimeType: "text/plain"
```

---

### TC-UC008-HP-001: Staff Resolves Conflict and Verifies Patient Profile

**Type:** happy_path | **Priority:** P0

**Preconditions:**
- Staff authenticated with `uc008.staff@propeliq.dev` (session cookie active)
- Patient `uc008.patient@propeliq.dev` has 2 documents processed with a `DataConflict` on `medication` field (10mg vs 20mg)
- `GET /api/patients/{patientId}/360` returns profile with `status=Unverified` and 1 conflict
- `POST /api/patients/{patientId}/360/verify` returns HTTP 200 after conflict resolved

**Steps:**

```yaml
steps:
  - step_id: "001"
    action: navigate
    target: "http://localhost:4200/staff/patients/uc008-patient-id/360"
    expect: "360° Patient View heading visible"

  - step_id: "002"
    action: verify
    target: "getByRole('heading', {name: '360° Patient View'})"
    expect: "visible"

  - step_id: "003"
    action: verify
    target: "getByTestId('conflict-indicator-medication')"
    expect: "visible; indicates unresolved conflict on medication field"

  - step_id: "004"
    action: click
    target: "getByTestId('conflict-indicator-medication')"
    expect: "Conflict detail panel opens"

  - step_id: "005"
    action: verify
    target: "getByTestId('conflict-value-1')"
    expect: "contains text 'Lisinopril 10mg'"

  - step_id: "006"
    action: verify
    target: "getByTestId('conflict-value-2')"
    expect: "contains text 'Lisinopril 20mg'"

  - step_id: "007"
    action: click
    target: "getByRole('button', {name: 'Select Lisinopril 10mg'})"
    expect: "Authoritative value selected; conflict indicator hidden"

  - step_id: "008"
    action: verify
    target: "getByTestId('conflict-indicator-medication')"
    expect: "hidden"

  - step_id: "009"
    action: click
    target: "getByRole('button', {name: 'Verify profile'})"
    expect: "HTTP 200; profile verified"

  - step_id: "010"
    action: verify
    target: "getByTestId('profile-status-badge')"
    expect: "contains text 'Verified'"

  - step_id: "011"
    action: verify
    target: "getByTestId('intake-data-medications')"
    expect: "contains text 'Lisinopril 10mg'"

  - step_id: "012"
    action: verify
    target: "getByTestId('intake-data-allergies')"
    expect: "contains text 'Sulfa drugs'"

  - step_id: "013"
    action: api_verify
    target: "GET /api/patients?email=uc008.patient@propeliq.dev/360"
    expect: "status=Verified; conflicts=[{resolutionStatus: Resolved, authoritativeValue: 'Lisinopril 10mg'}]"

  - step_id: "014"
    action: api_verify
    target: "GET /api/audit?entityType=PatientProfile&action=Verified"
    expect: "AuditLog entry with staffId, timestamp within session"
```

**Test Data:**

```yaml
test_data:
  staff:
    email: "uc008.staff@propeliq.dev"
    password: "StaffUC008P@ss!"
  patient:
    id: "uc008-patient-id"
    email: "uc008.patient@propeliq.dev"
  conflict:
    field: "medication"
    value1: "Lisinopril 10mg"
    source1: "Document 1"
    value2: "Lisinopril 20mg"
    source2: "Document 2"
    authoritativeValue: "Lisinopril 10mg"
  expectedAllergies: "Sulfa drugs"
```

---

### TC-UC008-EC-001: Staff Attempts Verification Before Resolving Conflict

**Type:** edge_case | **Priority:** P1

**Scenario:** Staff clicks "Verify profile" while a `DataConflict` with `resolutionStatus=Unresolved` still exists. The system must return HTTP 422 and display an actionable error message without changing profile status.

**Preconditions:**
- Staff authenticated
- Patient profile has 1 unresolved `DataConflict` on `medication` field
- `POST /api/patients/{id}/360/verify` mocked to return HTTP 422

**Steps:**

```yaml
steps:
  - step_id: "EC001"
    action: navigate
    target: "http://localhost:4200/staff/patients/uc008-patient-id/360"
    expect: "360° Patient View heading visible"

  - step_id: "EC002"
    action: verify
    target: "getByTestId('conflict-indicator-medication')"
    expect: "visible"

  - step_id: "EC003"
    action: click
    target: "getByRole('button', {name: 'Verify profile'})"
    expect: "HTTP 422 received from API"

  - step_id: "EC004"
    action: verify
    target: "getByRole('alert')"
    expect: "contains text 'Resolve all conflicts before verifying'"

  - step_id: "EC005"
    action: verify
    target: "getByTestId('profile-status-badge')"
    expect: "does not contain 'Verified'"
```

**Test Data:**

```yaml
test_data:
  staff:
    email: "uc008.staff@propeliq.dev"
    password: "StaffUC008P@ss!"
  patient:
    id: "uc008-patient-id"
  mock_response:
    status: 422
    body: "Resolve all conflicts before verifying"
```

---

### TC-UC008-ER-001: Patient-Role User Cannot Access 360° Profile View

**Type:** error | **Priority:** P1

**Trigger:** A user with `role=Patient` navigates directly to the staff 360° profile URL. The system must return HTTP 403 and deny access to PHI.

**Preconditions:**
- User authenticated with `role=Patient` (not Staff)
- Direct navigation to `/staff/patients/{id}/360`
- `GET /api/patients/{id}/360` mocked to return HTTP 403

**Steps:**

```yaml
steps:
  - step_id: "ER001"
    action: navigate
    target: "http://localhost:4200/staff/patients/uc008-patient-id/360"
    expect: "Access denied or redirect triggered"

  - step_id: "ER002"
    action: verify
    target: "getByRole('alert').or(page.getByRole('heading', {name: 'Access Denied'}))"
    expect: "visible; indicates forbidden access"

  - step_id: "ER003"
    action: verify
    target: "getByRole('heading', {name: '360° Patient View'})"
    expect: "not visible (access denied)"
```

**Test Data:**

```yaml
test_data:
  patient_user:
    email: "uc008.patient@propeliq.dev"
    password: "PatientUC008P@ss!"
    role: "Patient"
  mock_response:
    status: 403
    body: "Forbidden"
```

---

## Page Objects

```yaml
pages:
  - name: "DocumentUploadPage"
    file: "pages/document-upload.page.ts"
    elements:
      - fileInput: "getByTestId('document-upload-input')"
      - uploadButton: "getByRole('button', {name: 'Upload documents'})"
      - progressBar: "getByRole('progressbar')"
      - fileList: "getByTestId('upload-file-list')"
      - successBanner: "getByTestId('upload-success-banner')"
      - documentHistory: "getByTestId('document-history-list')"
      - fileError(baseName): "getByTestId('file-error-{baseName}')"
    actions:
      - uploadPdfs(fileNames): "Set input files with PDF buffers and click upload"
      - uploadBufferedFiles(files): "Set raw buffer array and click upload"

  - name: "ThreeSixtyViewPage"
    file: "pages/three-sixty-view.page.ts"
    elements:
      - heading: "getByRole('heading', {name: '360° Patient View'})"
      - conflictIndicator(field): "getByTestId('conflict-indicator-{field}')"
      - conflictValue(1|2): "getByTestId('conflict-value-{index}')"
      - verifyButton: "getByRole('button', {name: 'Verify profile'})"
      - profileStatusBadge: "getByTestId('profile-status-badge')"
      - selectConflictValueButton(value): "getByRole('button', {name: 'Select {value}'})"
      - errorAlert: "getByRole('alert')"
      - intakeDataMedications: "getByTestId('intake-data-medications')"
      - intakeDataAllergies: "getByTestId('intake-data-allergies')"
    actions:
      - resolveConflict(fieldName, authoritativeValue): "Click conflict indicator, click select button"
      - verifyProfile(): "Click Verify profile button"
```

## Success Criteria

- [ ] TC-UC007-HP-001: 2 files upload successfully; progress bar shown; success banner contains count; history visible
- [ ] TC-UC007-EC-001: File exceeding 10 MB limit shows per-file size error; upload button blocked
- [ ] TC-UC007-ER-001: Non-PDF file shows invalid type error; upload button blocked
- [ ] TC-UC008-HP-001: Staff resolves conflict; profile reaches status=Verified; intake medications and allergies visible in verified summary
- [ ] TC-UC008-EC-001: HTTP 422 on premature verification; error message shown; profile status unchanged
- [ ] TC-UC008-ER-001: Patient-role user denied access to 360° view; forbidden state visible
- [ ] All assertions use web-first patterns (`toBeVisible`, `toContainText`, `toBeHidden`)
- [ ] No `waitForTimeout` used anywhere
- [ ] Each test case is independent; no cross-test state dependencies
- [ ] Locator priority: `getByRole` > `getByTestId` > `getByLabel`

## Locator Reference

| Priority | Method | Example |
|----------|--------|---------|
| 1st | `getByRole` | `getByRole('button', {name: 'Verify profile'})` |
| 2nd | `getByTestId` | `getByTestId('profile-status-badge')` |
| 3rd | `getByLabel` | `getByLabel('Email address')` |
| AVOID | CSS | `.mat-card`, `#dynamic-id`, `nth-child` |

---

*Template: automated-testing-template.md | Output: `.propel/context/test/tw_patient_onboarding_20260420.md`*
