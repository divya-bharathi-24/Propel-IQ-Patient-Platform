# Navigation Map — UniPatient Hi-Fi Wireframes

> Cross-screen navigation index. All links are implemented via HTML `href` attributes in wireframe files.  
> **Notation:** `Source → [trigger/element] → Target`

---

## FL-001: Patient Registration & Appointment Booking

```
SCR-001 (Login)
  → [Register link]       → SCR-002 (Registration)
  → [Sign In button]      → SCR-003 (Patient Dashboard)  ← role: Patient

SCR-002 (Registration)
  → [Submit form]         → SCR-002a (Email Verification)
  → [Already have account] → SCR-001 (Login)

SCR-002a (Email Verification)
  → [Verified / continue] → SCR-001 (Login, re-sign-in)
  → [Resend email button] → self (cooldown state)

SCR-003 (Patient Dashboard)
  → [Book appointment]    → SCR-005 (Slot Selection)
  → [AI Intake quick action] → SCR-008 (AI Intake)
  → [Upload Docs]         → SCR-011 (Document Upload)
  → [Calendar Sync]       → SCR-012 (Calendar Sync)
  → [Profile]             → SCR-010 (Patient Profile)
  → [Appointment row]     → SCR-004 (Appointment Detail)
  → [OVL-002 Accept slot] → SCR-004 (Appointment Detail)

SCR-005 (Slot Selection)
  → [Continue → Insurance] → SCR-006 (Insurance Pre-Check)
  → [Add to waitlist]     → SCR-005b (Preferred Slot Waitlist)

SCR-006 (Insurance Pre-Check)
  → [Continue → Intake]   → SCR-008 (AI Intake)
  → [Skip intake]         → SCR-007 (Booking Confirmation)

SCR-007 (Booking Confirmation)
  → [View appointment]    → SCR-004 (Appointment Detail)
  → [Back to dashboard]   → SCR-003 (Patient Dashboard)
  → [Book another]        → SCR-005 (Slot Selection)
  → [Sync calendar]       → SCR-012 (Calendar Sync)

SCR-008 (AI Intake) / SCR-009 (Manual Intake)
  → [Mode toggle]         → SCR-009 / SCR-008 (toggle)
  → [Submit intake]       → SCR-007 (Booking Confirmation)
```

---

## FL-002: AI / Manual Intake

```
SCR-008 (AI Intake)
  → [Switch to Manual]    → SCR-009 (Manual Intake)
  → [Submit]              → SCR-007 (Booking Confirmation)

SCR-009 (Manual Intake)
  → [Switch to AI]        → SCR-008 (AI Intake)
  → [Submit]              → SCR-007 (Booking Confirmation)
```

---

## FL-003: Appointment Management

```
SCR-003 (Patient Dashboard)
  → [Appointment row]     → SCR-004 (Appointment Detail)
  → [Waitlist card]       → SCR-005b (Preferred Slot)

SCR-004 (Appointment Detail)
  → [Reschedule / Cancel] → SCR-004a (Reschedule/Cancel)
  → [Complete Intake]     → SCR-008 (AI Intake)
  → [View Documents]      → SCR-011 (Document Upload)
  → [Sync Calendar]       → SCR-012 (Calendar Sync)
  → [Cancel → dialog]     → OVL-003 (Cancel Dialog)

SCR-004a (Reschedule / Cancel)
  → [Reschedule card]     → SCR-005 (Slot Selection)
  → [Cancel card → dialog] → OVL-003 (Cancel Dialog)

OVL-003 (Cancel Dialog)
  → [Confirm cancellation] → SCR-003 (Patient Dashboard)
  → [Keep appointment]    → SCR-004 (Appointment Detail)

SCR-005b (Preferred Slot Waitlist)
  → [Back to dashboard]   → SCR-003 (Patient Dashboard)
  → [Book now]            → SCR-005 (Slot Selection)
```

---

## FL-004: Staff Same-Day Operations

```
SCR-013 (Staff Dashboard)
  → [View Queue]          → SCR-014 (Same-Day Queue)
  → [Walk-In Booking]     → SCR-015 (Walk-In Booking)
  → [Patient 360° row]    → SCR-016 (Patient 360°)
  → [Code Review]         → SCR-017 (Code Review)
  → [Appointments]        → SCR-018 (Appointment Mgmt)
  → [Reminders]           → SCR-019 (Reminder Mgmt)

SCR-015 (Walk-In Booking)
  → [Add to queue]        → SCR-014 (Same-Day Queue)

SCR-014 (Same-Day Queue)
  → [View patient]        → SCR-016 (Patient 360°)
```

---

## FL-005: Patient 360° & Documents

```
SCR-016 (Patient 360°)
  → [Conflict indicators] → OVL-005 (Conflict Drawer)
  → [Verify & Sign Off]   → OVL-006 (Verify Confirm Dialog)
  → [Documents tab]       → SCR-011 (Document Upload, staff-view)

OVL-005 (Conflict Drawer)
  → [Resolve & Verify]    → OVL-006 (Verify Confirm Dialog)
  → [Cancel]              → SCR-016 (Patient 360°)

OVL-006 (Verify Confirm Dialog)
  → [Sign off & verify]   → SCR-016 (Patient 360° — signed state)
  → [Cancel]              → SCR-016 (Patient 360°)
```

---

## FL-006: Medical Code Review

```
SCR-017 (Code Review)
  → [Confirm/Reject code] → self (row state updates)
  → [Back to dashboard]   → SCR-013 (Staff Dashboard)
```

---

## FL-007: Admin User Management

```
SCR-020 (User Management)
  → [Create User]         → OVL-004 (Re-auth Modal)
  → [Edit user]           → SCR-020 (inline edit state)
  → [Audit Log nav]       → SCR-021 (Audit Log)

OVL-004 (Re-auth Modal)
  → [Verify & continue]   → SCR-020 (User Management — create form)
  → [Cancel]              → SCR-020 (User Management)
```

---

## Global Navigation

| Trigger                     | Target                    | Applies To         |
|-----------------------------|---------------------------|--------------------|
| Logo / app name             | Role-appropriate dashboard| All screens        |
| Session idle → OVL-001      | OVL-001 (timeout modal)   | All auth screens   |
| OVL-001 Stay signed in      | Same screen (refresh)     | OVL-001            |
| OVL-001 Sign out            | SCR-001 (Login)           | OVL-001            |
| Browser back                | Previous screen           | All screens        |

---

## Summary Counts

| Type          | Count |
|---------------|-------|
| Base screens  | 24    |
| Overlay files | 6     |
| CSS files     | 1     |
| **Total files** | **31** |
| Unique flows  | 7     |
| PHI screens   | 6     |
