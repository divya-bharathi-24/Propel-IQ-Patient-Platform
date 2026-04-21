---
feature: "Admin User Management, Notifications & Calendar Integration"
source: ".propel/context/docs/spec.md"
use_cases: ["UC-010", "UC-011", "UC-012"]
base_url: "http://localhost:4200"
playwright_version: "1.x"
framework: "Angular 18"
generated_date: "2026-04-20"
---

# Test Workflow: Admin User Management, Notifications & Calendar Integration

## Metadata

| Field | Value |
|-------|-------|
| Feature | Admin User Management, Notifications & Calendar Integration |
| Source | `.propel/context/docs/spec.md` |
| Use Cases | UC-010, UC-011, UC-012 |
| Base URL | `http://localhost:4200` |
| Framework | Angular 18 + Angular Material |
| Playwright Version | 1.x (TypeScript) |

---

## Test Cases

### Test Case Master List

| TC-ID | Summary | Use Case | Type | Priority |
|-------|---------|----------|------|----------|
| TC-UC010-HP-001 | Admin creates staff account; credential email sent; staff logs in successfully | UC-010 | happy_path | P0 |
| TC-UC010-EC-001 | Admin changes staff role; updated permissions take effect on next session | UC-010 | edge_case | P1 |
| TC-UC010-ER-001 | Admin attempts to deactivate account without re-authenticating; request rejected | UC-010 | error | P0 |
| TC-UC011-HP-001 | Automated reminders sent at 48h, 24h, and 2h before appointment | UC-011 | happy_path | P0 |
| TC-UC011-EC-001 | Admin sends manual ad-hoc reminder to a specific patient | UC-011 | edge_case | P1 |
| TC-UC011-ER-001 | Cancelled appointment does not trigger reminder notifications | UC-011 | error | P1 |
| TC-UC012-HP-001 | Patient syncs appointment to Google Calendar; event created via OAuth | UC-012 | happy_path | P0 |
| TC-UC012-EC-001 | Patient downloads appointment as ICS file for offline calendar import | UC-012 | edge_case | P1 |
| TC-UC012-ER-001 | Patient denies OAuth consent; guidance message shown without crash | UC-012 | error | P1 |

---

### TC-UC010-HP-001: Admin Creates Staff Account; Credential Email Sent; Staff Logs In

**Type:** happy_path | **Priority:** P0

**Preconditions:**
- Admin authenticated as `admin@propeliq.dev`
- Email `new.staff@propeliq.dev` not yet registered
- SendGrid mock captures outbound emails

**Steps:**

```yaml
steps:
  - step_id: "001"
    action: navigate
    target: "http://localhost:4200/admin/users"
    expect: "User management panel loaded"

  - step_id: "002"
    action: click
    target: "getByRole('button', {name: 'Add staff member'})"
    expect: "Staff creation form dialog shown"

  - step_id: "003"
    action: fill
    target: "getByLabel('Email address')"
    value: "new.staff@propeliq.dev"
    expect: "field accepts valid email"

  - step_id: "004"
    action: fill
    target: "getByLabel('First name')"
    value: "New"
    expect: "field accepts input"

  - step_id: "005"
    action: fill
    target: "getByLabel('Last name')"
    value: "Staff"
    expect: "field accepts input"

  - step_id: "006"
    action: click
    target: "getByLabel('Role')"
    expect: "role dropdown opens"

  - step_id: "007"
    action: click
    target: "getByRole('option', {name: 'Staff'})"
    expect: "Staff role selected"

  - step_id: "008"
    action: click
    target: "getByRole('button', {name: 'Create account'})"
    expect: "Account created; credential email dispatched"

  - step_id: "009"
    action: verify
    target: "getByRole('alert')"
    expect: "contains text 'Staff account created. Credential email sent.'"

  - step_id: "010"
    action: api_verify
    target: "GET /api/users?email=new.staff@propeliq.dev"
    expect: "User with role=Staff, status=PendingActivation"

  - step_id: "011"
    action: api_verify
    target: "GET /api/notifications?email=new.staff@propeliq.dev&type=CredentialEmail"
    expect: "Notification with status=Sent"

  - step_id: "012"
    action: navigate
    target: "http://localhost:4200/activate?token=staff-activation-token-001"
    expect: "Account activation page loaded"

  - step_id: "013"
    action: fill
    target: "getByLabel('New password')"
    value: "StaffP@ss001!"
    expect: "field accepts input"

  - step_id: "014"
    action: fill
    target: "getByLabel('Confirm password')"
    value: "StaffP@ss001!"
    expect: "field accepts input"

  - step_id: "015"
    action: click
    target: "getByRole('button', {name: 'Activate account'})"
    expect: "Account activated; redirected to login page"

  - step_id: "016"
    action: fill
    target: "getByLabel('Email address')"
    value: "new.staff@propeliq.dev"
    expect: "field accepts input"

  - step_id: "017"
    action: fill
    target: "getByLabel('Password')"
    value: "StaffP@ss001!"
    expect: "field accepts input"

  - step_id: "018"
    action: click
    target: "getByRole('button', {name: 'Sign in'})"
    expect: "Login successful; staff dashboard loaded"

  - step_id: "019"
    action: verify
    target: "getByTestId('user-role-badge')"
    expect: "shows 'Staff'"
```

**Test Data:**

```yaml
test_data:
  admin_email: "admin@propeliq.dev"
  new_staff_email: "new.staff@propeliq.dev"
  staff_first_name: "New"
  staff_last_name: "Staff"
  role: "Staff"
  staff_password: "StaffP@ss001!"
  activation_token: "staff-activation-token-001"
```

---

### TC-UC010-EC-001: Admin Changes Staff Role; Updated Permissions Take Effect on Next Session

**Type:** edge_case | **Priority:** P1

**Scenario:** Admin promotes `staff@propeliq.dev` from Staff to Admin. Staff is already logged in. The role change does NOT affect the current session but applies on the next login.

**Preconditions:**
- Admin authenticated
- `existing.staff@propeliq.dev` has role=Staff, currently logged in (separate browser context)
- Staff has an active JWT with role=Staff claims

**Steps:**

```yaml
steps:
  - step_id: "EC001"
    action: navigate
    target: "http://localhost:4200/admin/users"
    expect: "User management panel loaded"

  - step_id: "EC002"
    action: click
    target: "getByTestId('user-row-existing-staff')"
    expect: "User detail panel shown"

  - step_id: "EC003"
    action: click
    target: "getByRole('button', {name: 'Edit role'})"
    expect: "Role edit dropdown shown"

  - step_id: "EC004"
    action: click
    target: "getByRole('option', {name: 'Admin'})"
    expect: "Admin role selected"

  - step_id: "EC005"
    action: click
    target: "getByRole('button', {name: 'Save changes'})"
    expect: "Role updated in system; confirmation shown"

  - step_id: "EC006"
    action: verify
    target: "getByRole('alert')"
    expect: "contains text 'Role updated. Changes take effect on next sign-in.'"

  - step_id: "EC007"
    action: api_verify
    target: "GET /api/users?email=existing.staff@propeliq.dev"
    expect: "User role=Admin in database"

  - step_id: "EC008"
    action: verify_in_context
    context: "existing-staff-session"
    target: "getByTestId('user-role-badge')"
    expect: "still shows 'Staff' (current session not invalidated immediately)"

  - step_id: "EC009"
    action: logout_and_login
    context: "existing-staff-session"
    credentials: { email: "existing.staff@propeliq.dev", password: "ExistingP@ss001!" }
    expect: "After re-login, staff dashboard shows 'Admin' role badge"
```

**Test Data:**

```yaml
test_data:
  admin_email: "admin@propeliq.dev"
  target_staff_email: "existing.staff@propeliq.dev"
  old_role: "Staff"
  new_role: "Admin"
```

---

### TC-UC010-ER-001: Admin Deactivates Account Without Re-Auth — Rejected

**Type:** error | **Priority:** P0

**Trigger:** Admin attempts to deactivate a staff account without completing the mandatory re-authentication (step-up verification). Deactivation requires re-auth per FR-055.

**Preconditions:**
- Admin authenticated, re-auth challenge NOT completed for this session
- `deactivate.staff@propeliq.dev` has role=Staff, status=Active

**Steps:**

```yaml
steps:
  - step_id: "ER001"
    action: navigate
    target: "http://localhost:4200/admin/users"
    expect: "User management panel loaded"

  - step_id: "ER002"
    action: click
    target: "getByTestId('user-row-deactivate-staff')"
    expect: "User detail panel shown"

  - step_id: "ER003"
    action: click
    target: "getByRole('button', {name: 'Deactivate account'})"
    expect: "Re-authentication challenge shown (not direct deactivation)"

  - step_id: "ER004"
    action: verify
    target: "getByRole('dialog')"
    expect: "dialog with title 'Confirm your identity to continue' visible"

  - step_id: "ER005"
    action: click
    target: "getByRole('button', {name: 'Cancel'})"
    expect: "Dialog closed; no deactivation executed"

  - step_id: "ER006"
    action: api_call
    target: "DELETE /api/users/deactivate-staff-id"
    auth: "admin JWT without re-auth header"
    expect: "HTTP 403 Forbidden"

  - step_id: "ER007"
    action: api_verify
    target: "GET /api/users?email=deactivate.staff@propeliq.dev"
    expect: "User status still=Active (not deactivated)"
```

**Test Data:**

```yaml
test_data:
  admin_email: "admin@propeliq.dev"
  target_staff_email: "deactivate.staff@propeliq.dev"
  target_staff_id: "deactivate-staff-id"
```

---

### TC-UC011-HP-001: Automated Reminders Sent at 48h, 24h, and 2h Before Appointment

**Type:** happy_path | **Priority:** P0

**Preconditions:**
- Patient `reminder.patient@propeliq.dev` has confirmed appointment `appt-remind-001`
- Appointment scheduled at T+48h from current time
- Notification scheduler mock advances time to T-48h, T-24h, T-2h

**Steps:**

```yaml
steps:
  - step_id: "001"
    action: api_verify
    target: "GET /api/appointments/appt-remind-001"
    expect: "appointment status=Confirmed; appointedAt=T+48h"

  - step_id: "002"
    action: advance_time
    value: "T-48h"
    expect: "48h reminder window triggered"

  - step_id: "003"
    action: api_verify
    target: "GET /api/notifications?appointmentId=appt-remind-001&window=48h"
    expect: "Notification status=Sent; channel=Email or SMS; sentAt within acceptable window"

  - step_id: "004"
    action: advance_time
    value: "T-24h"
    expect: "24h reminder window triggered"

  - step_id: "005"
    action: api_verify
    target: "GET /api/notifications?appointmentId=appt-remind-001&window=24h"
    expect: "Notification status=Sent"

  - step_id: "006"
    action: advance_time
    value: "T-2h"
    expect: "2h reminder window triggered"

  - step_id: "007"
    action: api_verify
    target: "GET /api/notifications?appointmentId=appt-remind-001&window=2h"
    expect: "Notification status=Sent"

  - step_id: "008"
    action: navigate
    target: "http://localhost:4200/patient/notifications"
    auth: "reminder.patient JWT"
    expect: "Notification history page loaded"

  - step_id: "009"
    action: verify
    target: "getByTestId('notification-list')"
    expect: "3 appointment reminder entries visible (48h, 24h, 2h)"
```

**Test Data:**

```yaml
test_data:
  patient_email: "reminder.patient@propeliq.dev"
  appointment_id: "appt-remind-001"
  reminder_windows: [48, 24, 2]
  channels: ["Email", "SMS"]
```

---

### TC-UC011-EC-001: Admin Sends Manual Ad-Hoc Reminder to Patient

**Type:** edge_case | **Priority:** P1

**Scenario:** Admin manually triggers an out-of-schedule reminder for a specific appointment.

**Preconditions:**
- Admin authenticated
- Patient `adhoc.patient@propeliq.dev` has appointment `appt-adhoc-001`

**Steps:**

```yaml
steps:
  - step_id: "EC001"
    action: navigate
    target: "http://localhost:4200/admin/notifications"
    expect: "Notifications management page loaded"

  - step_id: "EC002"
    action: fill
    target: "getByLabel('Search appointment')"
    value: "appt-adhoc-001"
    expect: "Appointment found and displayed"

  - step_id: "EC003"
    action: click
    target: "getByTestId('appt-adhoc-001-send-reminder')"
    expect: "Send reminder panel opens"

  - step_id: "EC004"
    action: click
    target: "getByRole('button', {name: 'Send reminder now'})"
    expect: "Reminder dispatched immediately"

  - step_id: "EC005"
    action: verify
    target: "getByRole('alert')"
    expect: "contains text 'Reminder sent'"

  - step_id: "EC006"
    action: api_verify
    target: "GET /api/notifications?appointmentId=appt-adhoc-001&type=AdHoc"
    expect: "Notification with type=AdHoc, status=Sent, triggeredBy=adminId"
```

**Test Data:**

```yaml
test_data:
  admin_email: "admin@propeliq.dev"
  appointment_id: "appt-adhoc-001"
  patient_email: "adhoc.patient@propeliq.dev"
  reminder_type: "AdHoc"
```

---

### TC-UC011-ER-001: Cancelled Appointment Does Not Trigger Reminder Notifications

**Type:** error | **Priority:** P1

**Trigger:** Patient cancels appointment before reminder windows. System must suppress all future scheduled reminders for this appointment.

**Preconditions:**
- Patient `cancel.reminder@propeliq.dev` has appointment `appt-cancel-remind-001`
- 48h and 24h reminder windows have NOT yet fired
- Patient cancels the appointment

**Steps:**

```yaml
steps:
  - step_id: "ER001"
    action: api_call
    target: "POST /api/appointments/appt-cancel-remind-001/cancel"
    auth: "cancel.reminder patient JWT"
    expect: "HTTP 200; appointment status=Cancelled"

  - step_id: "ER002"
    action: api_verify
    target: "GET /api/notifications/scheduled?appointmentId=appt-cancel-remind-001"
    expect: "All scheduled reminders status=Cancelled (not Pending)"

  - step_id: "ER003"
    action: advance_time
    value: "T-24h"
    expect: "24h window passes without sending reminder"

  - step_id: "ER004"
    action: api_verify
    target: "GET /api/notifications?appointmentId=appt-cancel-remind-001&channel=SMS"
    expect: "No new Notification records with status=Sent after cancellation"

  - step_id: "ER005"
    action: navigate
    target: "http://localhost:4200/patient/notifications"
    auth: "cancel.reminder JWT"
    expect: "Notification history shows 0 reminders for cancelled appointment"
```

**Test Data:**

```yaml
test_data:
  patient_email: "cancel.reminder@propeliq.dev"
  appointment_id: "appt-cancel-remind-001"
```

---

### TC-UC012-HP-001: Patient Syncs Appointment to Google Calendar via OAuth

**Type:** happy_path | **Priority:** P0

**Preconditions:**
- Patient `calendar.patient@propeliq.dev` has confirmed appointment `appt-calendar-001`
- Google Calendar OAuth mock configured to return success and capture event creation
- Redirect URI `http://localhost:4200/calendar/callback` configured in OAuth mock

**Steps:**

```yaml
steps:
  - step_id: "001"
    action: navigate
    target: "http://localhost:4200/patient/appointments"
    auth: "calendar.patient JWT"
    expect: "Patient appointments list loaded"

  - step_id: "002"
    action: click
    target: "getByTestId('appointment-appt-calendar-001')"
    expect: "Appointment detail view shown"

  - step_id: "003"
    action: click
    target: "getByRole('button', {name: 'Add to Google Calendar'})"
    expect: "Redirect to Google OAuth consent screen (mocked)"

  - step_id: "004"
    action: navigate
    target: "http://localhost:4200/calendar/callback?code=mock-auth-code-001"
    expect: "OAuth callback processed; redirected back to appointment view"

  - step_id: "005"
    action: verify
    target: "getByRole('alert')"
    expect: "contains text 'Appointment added to Google Calendar'"

  - step_id: "006"
    action: verify
    target: "getByTestId('calendar-sync-badge')"
    expect: "shows 'Synced to Google Calendar'"

  - step_id: "007"
    action: api_verify
    target: "GET /api/calendars/events?appointmentId=appt-calendar-001"
    expect: "CalendarEvent with provider=Google, status=Synced, eventId non-null"
```

**Test Data:**

```yaml
test_data:
  patient_email: "calendar.patient@propeliq.dev"
  appointment_id: "appt-calendar-001"
  oauth_provider: "Google"
  mock_auth_code: "mock-auth-code-001"
  redirect_uri: "http://localhost:4200/calendar/callback"
```

---

### TC-UC012-EC-001: Patient Downloads Appointment as ICS File

**Type:** edge_case | **Priority:** P1

**Scenario:** Patient chooses to download the appointment as an ICS file for manual import into any calendar application.

**Preconditions:**
- Patient authenticated; appointment `appt-ics-001` confirmed

**Steps:**

```yaml
steps:
  - step_id: "EC001"
    action: navigate
    target: "http://localhost:4200/patient/appointments"
    expect: "Appointments list loaded"

  - step_id: "EC002"
    action: click
    target: "getByTestId('appointment-appt-ics-001')"
    expect: "Appointment detail shown"

  - step_id: "EC003"
    action: click
    target: "getByRole('button', {name: 'Download ICS file'})"
    expect: "File download triggered"

  - step_id: "EC004"
    action: verify_download
    expect: "Downloaded file name matches 'appointment_*.ics' pattern"

  - step_id: "EC005"
    action: verify_file_content
    target: "downloaded ics file"
    expect: "Contains VEVENT with DTSTART, DTEND, SUMMARY matching appointment details"

  - step_id: "EC006"
    action: api_verify
    target: "GET /api/calendars/events?appointmentId=appt-ics-001"
    expect: "CalendarEvent with provider=ICS, status=Downloaded"
```

**Test Data:**

```yaml
test_data:
  appointment_id: "appt-ics-001"
  expected_ics_fields:
    - "VEVENT"
    - "DTSTART"
    - "DTEND"
    - "SUMMARY"
```

---

### TC-UC012-ER-001: Patient Denies OAuth Consent — Guidance Message Shown

**Type:** error | **Priority:** P1

**Trigger:** Patient clicks "Add to Google Calendar" but denies consent on the OAuth screen.

**Preconditions:**
- Google OAuth mock configured to simulate denied consent (returns `error=access_denied`)

**Steps:**

```yaml
steps:
  - step_id: "ER001"
    action: navigate
    target: "http://localhost:4200/patient/appointments"
    auth: "calendar.patient JWT"
    expect: "Appointments list loaded"

  - step_id: "ER002"
    action: click
    target: "getByTestId('appointment-appt-calendar-001')"
    expect: "Appointment detail view shown"

  - step_id: "ER003"
    action: click
    target: "getByRole('button', {name: 'Add to Google Calendar'})"
    expect: "Redirect to Google OAuth consent (mocked)"

  - step_id: "ER004"
    action: navigate
    target: "http://localhost:4200/calendar/callback?error=access_denied"
    expect: "OAuth denial callback handled; redirected to appointment page"

  - step_id: "ER005"
    action: verify
    target: "getByRole('alert')"
    expect: "contains text 'Calendar sync was not completed'"

  - step_id: "ER006"
    action: verify
    target: "getByRole('link', {name: 'Download ICS instead'})"
    expect: "Alternative ICS download link visible as fallback"

  - step_id: "ER007"
    action: verify
    target: "getByTestId('calendar-sync-badge')"
    expect: "does NOT show 'Synced' (sync was not completed)"

  - step_id: "ER008"
    action: api_verify
    target: "GET /api/calendars/events?appointmentId=appt-calendar-001&provider=Google"
    expect: "No CalendarEvent with status=Synced created"
```

**Test Data:**

```yaml
test_data:
  appointment_id: "appt-calendar-001"
  oauth_error: "access_denied"
  redirect_uri: "http://localhost:4200/calendar/callback"
```

---

## Page Objects

```yaml
pages:
  - name: "AdminUsersPage"
    file: "pages/admin-users.page.ts"
    elements:
      - addStaffButton: "getByRole('button', {name: 'Add staff member'})"
      - emailInput: "getByLabel('Email address')"
      - firstNameInput: "getByLabel('First name')"
      - lastNameInput: "getByLabel('Last name')"
      - roleDropdown: "getByLabel('Role')"
      - createButton: "getByRole('button', {name: 'Create account'})"
      - userRow: "getByTestId('user-row-{userId}')"
      - editRoleButton: "getByRole('button', {name: 'Edit role'})"
      - saveChangesButton: "getByRole('button', {name: 'Save changes'})"
      - deactivateButton: "getByRole('button', {name: 'Deactivate account'})"
      - reAuthDialog: "getByRole('dialog')"
    actions:
      - createStaff(email, firstName, lastName, role): "Fill form and submit"
      - changeRole(userId, newRole): "Open user row, edit role, save"

  - name: "NotificationsAdminPage"
    file: "pages/notifications-admin.page.ts"
    elements:
      - searchInput: "getByLabel('Search appointment')"
      - sendReminderButton: "getByTestId('{appointmentId}-send-reminder')"
      - sendNowButton: "getByRole('button', {name: 'Send reminder now'})"
    actions:
      - sendAdHocReminder(appointmentId): "Search, click send reminder, confirm"

  - name: "PatientNotificationsPage"
    file: "pages/patient-notifications.page.ts"
    elements:
      - notificationList: "getByTestId('notification-list')"
    actions:
      - getReminderCount(): "Count reminder notification entries"

  - name: "CalendarIntegrationPage"
    file: "pages/calendar-integration.page.ts"
    elements:
      - addToGoogleButton: "getByRole('button', {name: 'Add to Google Calendar'})"
      - downloadIcsButton: "getByRole('button', {name: 'Download ICS file'})"
      - syncBadge: "getByTestId('calendar-sync-badge')"
      - icsFallbackLink: "getByRole('link', {name: 'Download ICS instead'})"
    actions:
      - syncToGoogle(): "Click Add to Google Calendar and handle OAuth redirect"
      - downloadIcs(): "Click Download ICS and verify file"
```

## Success Criteria

- [ ] All happy path steps execute without errors
- [ ] Re-authentication gate enforced before deactivation
- [ ] Automated reminder suppression verified for cancelled appointments
- [ ] ICS file download content validated against VEVENT structure
- [ ] OAuth denial gracefully handled with fallback guidance
- [ ] All tests run independently with no shared state
- [ ] External OAuth flows mocked; no real Google credentials used in tests

## Locator Reference

| Priority | Method | Example |
|----------|--------|---------|
| 1st | `getByRole` | `getByRole('button', {name: 'Add to Google Calendar'})` |
| 2nd | `getByTestId` | `getByTestId('calendar-sync-badge')` |
| 3rd | `getByLabel` | `getByLabel('Email address')` |
| AVOID | CSS | `.mat-dialog`, `#oauth-button`, `nth-child` |

---

*Template: automated-testing-template.md | Output: `.propel/context/test/tw_admin_notifications_calendar_20260420.md`*
