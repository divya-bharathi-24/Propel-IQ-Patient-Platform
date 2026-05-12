# Information Architecture — UniPatient Platform

> **Wireframe set:** Hi-Fi · 31 files · `_tokens.css` shared stylesheet  
> **Design system:** `.propel/context/docs/designsystem.md`  
> **Status:** All screens and overlays complete

---

## Site Hierarchy

```
UniPatient
├── Public (unauthenticated)
│   ├── /login                        SCR-001  Login / Sign-In
│   ├── /register                     SCR-002  Patient Registration
│   └── /verify-email                 SCR-002a Email Verification Pending
│
├── Patient Portal (role: Patient)
│   ├── /dashboard                    SCR-003  Patient Dashboard        ← FL-001 exit, FL-003
│   ├── /appointments/:id             SCR-004  Appointment Detail
│   ├── /appointments/:id/manage      SCR-004a Reschedule / Cancel
│   ├── /slots                        SCR-005  Slot Selection
│   ├── /slots/waitlist               SCR-005b Preferred Slot Waitlist
│   ├── /insurance                    SCR-006  Insurance Pre-Check
│   ├── /booking/confirmation         SCR-007  Booking Confirmation
│   ├── /intake/ai                    SCR-008  AI Conversational Intake
│   ├── /intake/manual                SCR-009  Manual Intake Form
│   ├── /profile                      SCR-010  Patient Profile (PHI)
│   ├── /documents                    SCR-011  Document Upload
│   └── /calendar-sync                SCR-012  Calendar Sync
│
├── Staff Portal (role: Staff)
│   ├── /staff/dashboard              SCR-013  Staff Dashboard
│   ├── /staff/queue                  SCR-014  Same-Day Queue
│   ├── /staff/walkin                 SCR-015  Walk-In Booking
│   ├── /staff/patient/:id/360        SCR-016  Patient 360° View (PHI)
│   ├── /staff/code-review            SCR-017  Medical Code Review
│   ├── /staff/appointments           SCR-018  Appointment Management
│   └── /staff/reminders              SCR-019  Reminder Management
│
└── Admin Portal (role: Admin)
    ├── /admin/users                  SCR-020  User Management
    └── /admin/audit-log              SCR-021  Audit Log
```

---

## Screen Inventory

| ID      | File                                      | Route                        | Role    | Flow(s)        | PHI |
|---------|-------------------------------------------|------------------------------|---------|----------------|-----|
| SCR-001 | wireframe-SCR-001-login.html              | /login                       | Public  | FL-001         | No  |
| SCR-002 | wireframe-SCR-002-registration.html       | /register                    | Public  | FL-001         | No  |
| SCR-002a| wireframe-SCR-002a-email-verification.html| /verify-email                | Public  | FL-001         | No  |
| SCR-003 | wireframe-SCR-003-patient-dashboard.html  | /dashboard                   | Patient | FL-001, FL-003 | No  |
| SCR-004 | wireframe-SCR-004-appointment-detail.html | /appointments/:id            | Patient | FL-001, FL-003 | No  |
| SCR-004a| wireframe-SCR-004a-reschedule-cancel.html | /appointments/:id/manage     | Patient | FL-003         | No  |
| SCR-005 | wireframe-SCR-005-slot-selection.html     | /slots                       | Patient | FL-001, FL-003 | No  |
| SCR-005b| wireframe-SCR-005b-preferred-slot.html    | /slots/waitlist              | Patient | FL-003         | No  |
| SCR-006 | wireframe-SCR-006-insurance-precheck.html | /insurance                   | Patient | FL-001         | Yes |
| SCR-007 | wireframe-SCR-007-booking-confirmation.html| /booking/confirmation        | Patient | FL-001         | No  |
| SCR-008 | wireframe-SCR-008-ai-intake.html          | /intake/ai                   | Patient | FL-002         | Yes |
| SCR-009 | wireframe-SCR-009-manual-intake.html      | /intake/manual               | Patient | FL-002         | Yes |
| SCR-010 | wireframe-SCR-010-patient-profile.html    | /profile                     | Patient | —              | Yes |
| SCR-011 | wireframe-SCR-011-document-upload.html    | /documents                   | Patient | FL-005         | No  |
| SCR-012 | wireframe-SCR-012-calendar-sync.html      | /calendar-sync               | Patient | FL-001         | No  |
| SCR-013 | wireframe-SCR-013-staff-dashboard.html    | /staff/dashboard             | Staff   | FL-004         | No  |
| SCR-014 | wireframe-SCR-014-same-day-queue.html     | /staff/queue                 | Staff   | FL-004         | Yes |
| SCR-015 | wireframe-SCR-015-walkin-booking.html     | /staff/walkin                | Staff   | FL-004         | Yes |
| SCR-016 | wireframe-SCR-016-patient-360.html        | /staff/patient/:id/360       | Staff   | FL-005         | Yes |
| SCR-017 | wireframe-SCR-017-code-review.html        | /staff/code-review           | Staff   | FL-006         | No  |
| SCR-018 | wireframe-SCR-018-appointment-management.html| /staff/appointments       | Staff   | —              | No  |
| SCR-019 | wireframe-SCR-019-reminders.html          | /staff/reminders             | Staff   | —              | No  |
| SCR-020 | wireframe-SCR-020-user-management.html    | /admin/users                 | Admin   | FL-007         | No  |
| SCR-021 | wireframe-SCR-021-audit-log.html          | /admin/audit-log             | Admin   | —              | No  |

---

## Overlay Inventory

| ID      | File                                          | Trigger                          | Scope           |
|---------|-----------------------------------------------|----------------------------------|-----------------|
| OVL-001 | wireframe-OVL-001-session-timeout.html        | Session idle ~10 min             | All auth screens|
| OVL-002 | wireframe-OVL-002-slot-swap-toast.html        | Preferred slot opens             | SCR-003         |
| OVL-003 | wireframe-OVL-003-cancel-dialog.html          | "Cancel appointment" CTA         | SCR-004a        |
| OVL-004 | wireframe-OVL-004-reauth-modal.html           | Admin sensitive action           | SCR-020         |
| OVL-005 | wireframe-OVL-005-conflict-drawer.html        | "Resolve conflicts" CTA          | SCR-016         |
| OVL-006 | wireframe-OVL-006-360-verify-confirm.html     | "Verify & Sign Off" CTA          | SCR-016, OVL-005|

---

## User Flow Map

| Flow   | Name                        | Entry      | Exit       | Screens                                           |
|--------|-----------------------------|------------|------------|---------------------------------------------------|
| FL-001 | Patient Registration & Booking| SCR-001  | SCR-007    | 001→002→002a→003→005→006→008→007→012             |
| FL-002 | AI/Manual Intake            | SCR-008    | SCR-007    | 008 ↔ 009 → 007                                  |
| FL-003 | Appointment Management      | SCR-003    | SCR-003    | 003→004→004a→005/005b/003                        |
| FL-004 | Staff Same-Day Operations   | SCR-013    | SCR-014    | 013→015→014→016                                  |
| FL-005 | Patient 360° + Documents    | SCR-016    | SCR-016    | 016→OVL-005→OVL-006 / 011                       |
| FL-006 | Medical Code Review         | SCR-017    | SCR-017    | 017 (self-contained table interaction)           |
| FL-007 | Admin User Management       | SCR-020    | SCR-020    | 020→OVL-004→020                                  |
