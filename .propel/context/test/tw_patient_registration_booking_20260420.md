---
feature: "Patient Registration, Booking & Intake"
source: ".propel/context/docs/spec.md"
use_cases: ["UC-001", "UC-002", "UC-003"]
base_url: "http://localhost:4200"
playwright_version: "1.x"
framework: "Angular 18"
generated_date: "2026-04-20"
---

# Test Workflow: Patient Registration, Booking & Intake

## Metadata

| Field | Value |
|-------|-------|
| Feature | Patient Registration, Booking & Intake |
| Source | `.propel/context/docs/spec.md` |
| Use Cases | UC-001, UC-002, UC-003 |
| Base URL | `http://localhost:4200` |
| Framework | Angular 18 + Angular Material |
| Playwright Version | 1.x (TypeScript) |

---

## Test Cases

### Test Case Master List

| TC-ID | Summary | Use Case | Type | Priority |
|-------|---------|----------|------|----------|
| TC-UC001-HP-001 | Patient self-registers, verifies email, books slot, receives PDF confirmation | UC-001 | happy_path | P0 |
| TC-UC001-EC-001 | Patient books available slot and designates an unavailable slot as preferred | UC-001 | edge_case | P1 |
| TC-UC001-ER-001 | Registration with duplicate email returns error and redirects to login | UC-001 | error | P0 |
| TC-UC002-HP-001 | Patient completes AI-assisted intake via chat, reviews auto-populated fields, submits | UC-002 | happy_path | P1 |
| TC-UC002-EC-001 | Patient switches from AI intake to manual form mid-session; prior data preserved | UC-002 | edge_case | P1 |
| TC-UC002-ER-001 | AI intake NLU confidence below threshold triggers clarifying follow-up question | UC-002 | error | P1 |
| TC-UC003-HP-001 | Patient completes manual intake form with autosave, submits successfully | UC-003 | happy_path | P1 |
| TC-UC003-EC-001 | Manual intake form pre-populated from prior AI intake session data | UC-003 | edge_case | P1 |
| TC-UC003-ER-001 | Manual intake form submission blocked when required fields are missing | UC-003 | error | P1 |

---

### TC-UC001-HP-001: Patient Self-Registers, Books Appointment, and Receives PDF Confirmation

**Type:** happy_path | **Priority:** P0

**Preconditions:**
- Email `hp.patient.001@propeliq.dev` not registered in the system
- At least one appointment slot available (seeded: `slot-hp-001`, 2026-05-15 09:00)
- SendGrid mock configured to capture outbound emails
- Insurance dummy record `BlueCross/MBR-001` present in system

**Steps:**

```yaml
steps:
  - step_id: "001"
    action: navigate
    target: "http://localhost:4200/register"
    expect: "Registration page heading visible"

  - step_id: "002"
    action: fill
    target: "getByLabel('Email address')"
    value: "hp.patient.001@propeliq.dev"
    expect: "field accepts valid email"

  - step_id: "003"
    action: fill
    target: "getByLabel('Password')"
    value: "TestP@ss001!"
    expect: "password field accepts input"

  - step_id: "004"
    action: fill
    target: "getByLabel('First name')"
    value: "Happy"
    expect: "field accepts input"

  - step_id: "005"
    action: fill
    target: "getByLabel('Last name')"
    value: "Patient001"
    expect: "field accepts input"

  - step_id: "006"
    action: fill
    target: "getByLabel('Date of birth')"
    value: "1990-05-15"
    expect: "field accepts date"

  - step_id: "007"
    action: fill
    target: "getByLabel('Phone number')"
    value: "+14155550101"
    expect: "field accepts phone number"

  - step_id: "008"
    action: click
    target: "getByRole('button', {name: 'Create account'})"
    expect: "form submitted; success banner or email verification message visible"

  - step_id: "009"
    action: verify
    target: "getByRole('alert')"
    expect: "contains text 'Verification email sent'"

  - step_id: "010"
    action: navigate
    target: "http://localhost:4200/verify-email?token=test-token-001"
    expect: "Email verified confirmation page loaded"

  - step_id: "011"
    action: verify
    target: "getByRole('heading', {name: 'Email verified'})"
    expect: "visible"

  - step_id: "012"
    action: navigate
    target: "http://localhost:4200/book"
    expect: "Booking page with available slots displayed"

  - step_id: "013"
    action: verify
    target: "getByTestId('slot-hp-001')"
    expect: "slot for 2026-05-15 09:00 visible and available"

  - step_id: "014"
    action: click
    target: "getByTestId('slot-hp-001')"
    expect: "slot selected; insurance pre-check form shown"

  - step_id: "015"
    action: fill
    target: "getByLabel('Insurance provider')"
    value: "BlueCross"
    expect: "field accepts input"

  - step_id: "016"
    action: fill
    target: "getByLabel('Member ID')"
    value: "MBR-001"
    expect: "field accepts input"

  - step_id: "017"
    action: click
    target: "getByRole('button', {name: 'Verify insurance'})"
    expect: "Insurance status badge shows 'Verified'"

  - step_id: "018"
    action: verify
    target: "getByTestId('insurance-status')"
    expect: "contains text 'Verified'"

  - step_id: "019"
    action: click
    target: "getByRole('button', {name: 'Continue to intake'})"
    expect: "Intake mode selection screen shown"

  - step_id: "020"
    action: click
    target: "getByRole('button', {name: 'Confirm booking'})"
    expect: "Booking confirmed; appointment reference number displayed"

  - step_id: "021"
    action: verify
    target: "getByTestId('booking-reference')"
    expect: "reference number visible and non-empty"

  - step_id: "022"
    action: verify
    target: "getByRole('alert')"
    expect: "contains text 'Confirmation email sent'"

  - step_id: "023"
    action: api_verify
    target: "GET /api/notifications?patientId=hp001&type=pdf"
    expect: "notification record with status=Sent within 60 seconds of booking"
```

**Test Data:**

```yaml
test_data:
  email: "hp.patient.001@propeliq.dev"
  password: "TestP@ss001!"
  firstName: "Happy"
  lastName: "Patient001"
  dateOfBirth: "1990-05-15"
  phone: "+14155550101"
  slotId: "slot-hp-001"
  slotDate: "2026-05-15"
  slotTime: "09:00"
  insuranceProvider: "BlueCross"
  memberId: "MBR-001"
  verificationToken: "test-token-001"
```

---

### TC-UC001-EC-001: Patient Designates Preferred Unavailable Slot During Booking

**Type:** edge_case | **Priority:** P1

**Scenario:** Patient books an available slot and simultaneously designates an unavailable slot as preferred, creating a WaitlistEntry.

**Preconditions:**
- Patient authenticated as `ec.patient.001@propeliq.dev`
- `slot-ec-available` has status Available
- `slot-ec-preferred` has status Booked (unavailable)

**Steps:**

```yaml
steps:
  - step_id: "EC001"
    action: navigate
    target: "http://localhost:4200/book"
    expect: "Booking page shows available slots"

  - step_id: "EC002"
    action: click
    target: "getByTestId('slot-ec-available')"
    expect: "Slot selected; 'Set preferred slot' option visible"

  - step_id: "EC003"
    action: click
    target: "getByRole('button', {name: 'Set a preferred slot'})"
    expect: "Preferred slot selector opens showing unavailable slots"

  - step_id: "EC004"
    action: click
    target: "getByTestId('slot-ec-preferred')"
    expect: "Preferred slot highlighted; 'Notify me when this slot opens' label visible"

  - step_id: "EC005"
    action: click
    target: "getByRole('button', {name: 'Confirm booking'})"
    expect: "Booking confirmed with both primary and preferred slot indicated"

  - step_id: "EC006"
    action: verify
    target: "getByTestId('preferred-slot-indicator')"
    expect: "visible and shows preferred slot date/time"

  - step_id: "EC007"
    action: api_verify
    target: "GET /api/waitlist?patientId=ec001"
    expect: "WaitlistEntry with status=Active, preferredSlot=slot-ec-preferred"
```

**Test Data:**

```yaml
test_data:
  patient_email: "ec.patient.001@propeliq.dev"
  available_slot: "slot-ec-available"
  preferred_slot: "slot-ec-preferred"
```

---

### TC-UC001-ER-001: Registration with Duplicate Email Returns Error and Redirects to Login

**Type:** error | **Priority:** P0

**Trigger:** Patient attempts to register with an email address already in the system.

**Preconditions:**
- Email `existing@propeliq.dev` already registered and verified

**Steps:**

```yaml
steps:
  - step_id: "ER001"
    action: navigate
    target: "http://localhost:4200/register"
    expect: "Registration page visible"

  - step_id: "ER002"
    action: fill
    target: "getByLabel('Email address')"
    value: "existing@propeliq.dev"
    expect: "field accepts input"

  - step_id: "ER003"
    action: fill
    target: "getByLabel('Password')"
    value: "TestP@ss001!"
    expect: "field accepts input"

  - step_id: "ER004"
    action: fill
    target: "getByLabel('First name')"
    value: "Existing"
    expect: "field accepts input"

  - step_id: "ER005"
    action: fill
    target: "getByLabel('Last name')"
    value: "User"
    expect: "field accepts input"

  - step_id: "ER006"
    action: click
    target: "getByRole('button', {name: 'Create account'})"
    expect: "Error message displayed; no account created"

  - step_id: "ER007"
    action: verify
    target: "getByRole('alert')"
    expect: "contains text 'Email already registered'"

  - step_id: "ER008"
    action: verify
    target: "getByRole('link', {name: 'Sign in'})"
    expect: "Link to login page visible as recovery action"

  - step_id: "ER009"
    action: api_verify
    target: "GET /api/users?email=existing@propeliq.dev"
    expect: "Only one user record with this email (no duplicate created)"
```

**Test Data:**

```yaml
test_data:
  duplicate_email: "existing@propeliq.dev"
  password: "TestP@ss001!"
  invalid_passwords:
    - value: "password"
      expect_error: "Password must have uppercase, digit, and special character"
    - value: "Pass1234"
      expect_error: "Password must include a special character"
    - value: "P@ss"
      expect_error: "Password must be at least 8 characters"
  boundary_valid_password: "P@ssw0rd"
```

---

### TC-UC002-HP-001: Patient Completes AI-Assisted Intake via Conversational Chat

**Type:** happy_path | **Priority:** P1

**Preconditions:**
- Patient authenticated and has confirmed appointment `appt-ai-001`
- AI intake endpoint mocked with deterministic NLU responses
- Intake not yet started for `appt-ai-001`

**Steps:**

```yaml
steps:
  - step_id: "001"
    action: navigate
    target: "http://localhost:4200/intake/appt-ai-001"
    expect: "Intake mode selection screen shown"

  - step_id: "002"
    action: click
    target: "getByRole('button', {name: 'AI-Assisted intake'})"
    expect: "Conversational chat interface opens; welcome message visible"

  - step_id: "003"
    action: verify
    target: "getByRole('log')"
    expect: "AI opening question visible (e.g., 'What medications are you currently taking?')"

  - step_id: "004"
    action: fill
    target: "getByLabel('Your message')"
    value: "I take Metformin 500mg daily and Aspirin 81mg"
    expect: "Input accepted"

  - step_id: "005"
    action: click
    target: "getByRole('button', {name: 'Send'})"
    expect: "Message submitted; AI processes response"

  - step_id: "006"
    action: verify
    target: "getByTestId('intake-preview-medications')"
    expect: "Shows 'Metformin 500mg' and 'Aspirin 81mg' auto-populated in live preview"

  - step_id: "007"
    action: verify
    target: "getByRole('log')"
    expect: "AI follow-up question about allergies visible"

  - step_id: "008"
    action: fill
    target: "getByLabel('Your message')"
    value: "I am allergic to penicillin"
    expect: "Input accepted"

  - step_id: "009"
    action: click
    target: "getByRole('button', {name: 'Send'})"
    expect: "Allergy parsed; preview field updated"

  - step_id: "010"
    action: verify
    target: "getByTestId('intake-preview-allergies')"
    expect: "Shows 'Penicillin' in allergies preview"

  - step_id: "011"
    action: click
    target: "getByRole('button', {name: 'Submit intake'})"
    expect: "Intake submitted successfully"

  - step_id: "012"
    action: verify
    target: "getByRole('alert')"
    expect: "contains text 'Intake submitted successfully'"

  - step_id: "013"
    action: api_verify
    target: "GET /api/intake/appt-ai-001"
    expect: "IntakeRecord with source=AI, medications=[Metformin 500mg, Aspirin 81mg], allergies=[Penicillin]"
```

**Test Data:**

```yaml
test_data:
  appointment_id: "appt-ai-001"
  chat_inputs:
    - message: "I take Metformin 500mg daily and Aspirin 81mg"
      expected_fields:
        medications: ["Metformin 500mg", "Aspirin 81mg"]
    - message: "I am allergic to penicillin"
      expected_fields:
        allergies: ["Penicillin"]
  expected_source: "AI"
```

---

### TC-UC002-EC-001: Patient Switches from AI Intake to Manual Form — Data Preserved

**Type:** edge_case | **Priority:** P1

**Scenario:** Patient starts AI intake, enters some data, then switches to manual form. Previously parsed field values should be pre-populated in the manual form without data loss.

**Preconditions:**
- Patient authenticated; appointment `appt-switch-001` exists
- AI intake started; `Metformin 500mg` already parsed into medications field

**Steps:**

```yaml
steps:
  - step_id: "EC001"
    action: navigate
    target: "http://localhost:4200/intake/appt-switch-001"
    expect: "AI intake chat interface visible with prior session resumed"

  - step_id: "EC002"
    action: verify
    target: "getByTestId('intake-preview-medications')"
    expect: "Shows 'Metformin 500mg' already pre-populated from prior AI session"

  - step_id: "EC003"
    action: click
    target: "getByRole('button', {name: 'Switch to manual form'})"
    expect: "Confirmation dialog shown"

  - step_id: "EC004"
    action: click
    target: "getByRole('button', {name: 'Continue'})"
    expect: "Manual form rendered with Metformin 500mg visible in medications field"

  - step_id: "EC005"
    action: verify
    target: "getByLabel('Current medications')"
    expect: "value contains 'Metformin 500mg' (pre-populated)"

  - step_id: "EC006"
    action: verify
    target: "getByTestId('intake-mode-badge')"
    expect: "shows 'Manual'"
```

**Test Data:**

```yaml
test_data:
  appointment_id: "appt-switch-001"
  pre_populated_medication: "Metformin 500mg"
```

---

### TC-UC002-ER-001: AI Intake NLU Confidence Below Threshold Triggers Clarifying Question

**Type:** error | **Priority:** P1

**Trigger:** Patient provides an ambiguous response; NLU confidence falls below 80% for a field.

**Preconditions:**
- Patient authenticated; AI intake active for appointment `appt-nlu-001`
- AI mock configured to return confidence=0.6 for ambiguous medication input

**Steps:**

```yaml
steps:
  - step_id: "ER001"
    action: navigate
    target: "http://localhost:4200/intake/appt-nlu-001"
    expect: "AI chat interface open"

  - step_id: "ER002"
    action: fill
    target: "getByLabel('Your message')"
    value: "I take some kind of blood pressure pill"
    expect: "Input accepted"

  - step_id: "ER003"
    action: click
    target: "getByRole('button', {name: 'Send'})"
    expect: "AI response received"

  - step_id: "ER004"
    action: verify
    target: "getByRole('log')"
    expect: "AI follow-up question visible (e.g., 'Could you clarify the name and dosage of the blood pressure medication?')"

  - step_id: "ER005"
    action: verify
    target: "getByTestId('intake-preview-medications')"
    expect: "Field NOT auto-populated (remains empty due to low confidence)"

  - step_id: "ER006"
    action: verify
    target: "getByTestId('confidence-warning')"
    expect: "optional low-confidence indicator visible near medications field"
```

**Test Data:**

```yaml
test_data:
  appointment_id: "appt-nlu-001"
  ambiguous_input: "I take some kind of blood pressure pill"
  expected_confidence: 0.6
  confidence_threshold: 0.8
```

---

### TC-UC003-HP-001: Patient Completes Manual Intake Form with Autosave and Submits

**Type:** happy_path | **Priority:** P1

**Preconditions:**
- Patient authenticated; appointment `appt-manual-001` confirmed
- Manual intake form available; no prior intake record for this appointment

**Steps:**

```yaml
steps:
  - step_id: "001"
    action: navigate
    target: "http://localhost:4200/intake/appt-manual-001"
    expect: "Intake mode selection shown"

  - step_id: "002"
    action: click
    target: "getByRole('button', {name: 'Manual form'})"
    expect: "Manual intake form rendered with all required fields"

  - step_id: "003"
    action: fill
    target: "getByLabel('Current medications')"
    value: "Lisinopril 10mg daily"
    expect: "field accepts input"

  - step_id: "004"
    action: fill
    target: "getByLabel('Known allergies')"
    value: "Sulfa drugs"
    expect: "field accepts input"

  - step_id: "005"
    action: fill
    target: "getByLabel('Primary symptoms')"
    value: "Mild headache, fatigue"
    expect: "field accepts input"

  - step_id: "006"
    action: fill
    target: "getByLabel('Medical history')"
    value: "Hypertension (diagnosed 2020)"
    expect: "field accepts input"

  - step_id: "007"
    action: blur
    target: "getByLabel('Medical history')"
    expect: "autosave triggered on field blur"

  - step_id: "008"
    action: verify
    target: "getByTestId('autosave-indicator')"
    expect: "shows 'Draft saved'"

  - step_id: "009"
    action: click
    target: "getByRole('button', {name: 'Submit intake'})"
    expect: "Form submitted; required field validation passes"

  - step_id: "010"
    action: verify
    target: "getByRole('alert')"
    expect: "contains text 'Intake submitted successfully'"

  - step_id: "011"
    action: api_verify
    target: "GET /api/intake/appt-manual-001"
    expect: "IntakeRecord with source=Manual, completedAt is non-null"
```

**Test Data:**

```yaml
test_data:
  appointment_id: "appt-manual-001"
  medications: "Lisinopril 10mg daily"
  allergies: "Sulfa drugs"
  symptoms: "Mild headache, fatigue"
  medicalHistory: "Hypertension (diagnosed 2020)"
```

---

### TC-UC003-EC-001: Manual Intake Form Pre-Populated from Prior AI Session

**Type:** edge_case | **Priority:** P1

**Scenario:** Patient completed partial AI intake (Metformin 500mg parsed), switches to manual. Manual form is pre-populated with AI-parsed values.

**Preconditions:**
- Patient authenticated; AI intake partially completed for `appt-prepop-001`
- IntakeRecord in `source=AI` state with `medications=[Metformin 500mg]`

**Steps:**

```yaml
steps:
  - step_id: "EC001"
    action: navigate
    target: "http://localhost:4200/intake/appt-prepop-001"
    expect: "AI chat interface shown with resume option"

  - step_id: "EC002"
    action: click
    target: "getByRole('button', {name: 'Switch to manual form'})"
    expect: "Manual form rendered"

  - step_id: "EC003"
    action: verify
    target: "getByLabel('Current medications')"
    expect: "value is 'Metformin 500mg' (pre-populated from AI session)"

  - step_id: "EC004"
    action: verify
    target: "getByTestId('pre-population-notice')"
    expect: "notice visible: 'Fields pre-filled from your AI session'"
```

**Test Data:**

```yaml
test_data:
  appointment_id: "appt-prepop-001"
  ai_parsed_medication: "Metformin 500mg"
```

---

### TC-UC003-ER-001: Manual Intake Submission Blocked When Required Fields Are Missing

**Type:** error | **Priority:** P1

**Trigger:** Patient clicks "Submit intake" without filling required fields.

**Preconditions:**
- Patient authenticated; manual intake form rendered for `appt-validate-001`
- Required fields: Current medications, Known allergies, Primary symptoms

**Steps:**

```yaml
steps:
  - step_id: "ER001"
    action: navigate
    target: "http://localhost:4200/intake/appt-validate-001"
    expect: "Manual intake form shown"

  - step_id: "ER002"
    action: click
    target: "getByRole('button', {name: 'Manual form'})"
    expect: "Form rendered with empty fields"

  - step_id: "ER003"
    action: click
    target: "getByRole('button', {name: 'Submit intake'})"
    expect: "Validation errors shown; submission blocked"

  - step_id: "ER004"
    action: verify
    target: "getByRole('alert')"
    expect: "contains text 'Please complete all required fields'"

  - step_id: "ER005"
    action: verify
    target: "getByTestId('field-error-medications')"
    expect: "error message visible: 'Current medications is required'"

  - step_id: "ER006"
    action: verify
    target: "getByTestId('field-error-allergies')"
    expect: "error message visible: 'Known allergies is required'"

  - step_id: "ER007"
    action: api_verify
    target: "GET /api/intake/appt-validate-001"
    expect: "No IntakeRecord with completedAt set (submission not saved)"
```

**Test Data:**

```yaml
test_data:
  appointment_id: "appt-validate-001"
  required_fields: ["medications", "allergies", "symptoms"]
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
      - errorAlert: "getByRole('alert')"
    actions:
      - register(email, password, firstName, lastName, dob, phone): "Fill registration form and submit"
      - getErrorMessage(): "Return text of error alert"

  - name: "BookingPage"
    file: "pages/booking.page.ts"
    elements:
      - slotCard: "getByTestId('slot-{slotId}')"
      - insuranceProviderInput: "getByLabel('Insurance provider')"
      - memberIdInput: "getByLabel('Member ID')"
      - verifyInsuranceButton: "getByRole('button', {name: 'Verify insurance'})"
      - insuranceStatusBadge: "getByTestId('insurance-status')"
      - confirmButton: "getByRole('button', {name: 'Confirm booking'})"
      - bookingReference: "getByTestId('booking-reference')"
      - setPreferredSlotButton: "getByRole('button', {name: 'Set a preferred slot'})"
      - preferredSlotIndicator: "getByTestId('preferred-slot-indicator')"
    actions:
      - selectSlot(slotId): "Click slot card by test ID"
      - verifyInsurance(provider, memberId): "Fill and submit insurance pre-check"
      - confirmBooking(): "Click confirm and wait for reference number"

  - name: "IntakePage"
    file: "pages/intake.page.ts"
    elements:
      - aiModeButton: "getByRole('button', {name: 'AI-Assisted intake'})"
      - manualModeButton: "getByRole('button', {name: 'Manual form'})"
      - chatLog: "getByRole('log')"
      - messageInput: "getByLabel('Your message')"
      - sendButton: "getByRole('button', {name: 'Send'})"
      - medicationsPreview: "getByTestId('intake-preview-medications')"
      - allergiesPreview: "getByTestId('intake-preview-allergies')"
      - switchToManualButton: "getByRole('button', {name: 'Switch to manual form'}"
      - submitButton: "getByRole('button', {name: 'Submit intake'})"
      - autosaveIndicator: "getByTestId('autosave-indicator')"
      - modeBadge: "getByTestId('intake-mode-badge')"
      - medicationsInput: "getByLabel('Current medications')"
      - allergiesInput: "getByLabel('Known allergies')"
      - symptomsInput: "getByLabel('Primary symptoms')"
      - medHistoryInput: "getByLabel('Medical history')"
    actions:
      - selectAiMode(): "Click AI-Assisted intake button"
      - selectManualMode(): "Click Manual form button"
      - sendChatMessage(message): "Fill message input and click Send"
      - switchToManual(): "Click switch and confirm dialog"
      - submitIntake(): "Click Submit intake button"
```

## Success Criteria

- [ ] All happy path steps execute without errors
- [ ] Edge case validations pass (preferred slot designated, manual pre-population)
- [ ] Error scenarios handled correctly (duplicate email, missing fields, low-confidence NLU)
- [ ] Tests run independently — no shared state between test cases
- [ ] All assertions use web-first patterns (no `waitForTimeout`)
- [ ] Selectors use `getByRole`, `getByLabel`, or `getByTestId` exclusively
- [ ] Test data sourced from YAML fixtures, not hard-coded literals
- [ ] External API calls (SendGrid, AI provider) intercepted with Playwright route mocks

## Locator Reference

| Priority | Method | Example |
|----------|--------|---------|
| 1st | `getByRole` | `getByRole('button', {name: 'Confirm booking'})` |
| 2nd | `getByTestId` | `getByTestId('slot-hp-001')` |
| 3rd | `getByLabel` | `getByLabel('Email address')` |
| 4th | `getByText` | `getByText('Verified')` — for status badges only |
| AVOID | CSS selectors | `.mat-button`, `#dynamic-123`, `nth-child` |

---

*Template: automated-testing-template.md | Output: `.propel/context/test/tw_patient_registration_booking_20260420.md`*
