# Component Inventory — UniPatient Hi-Fi Wireframes

> All components are implemented via CSS classes in `_tokens.css`.  
> **Prefix convention:** BEM-adjacent — block classes on root element, modifier via `--variant`.

---

## Layout Components

| Component      | CSS Class(es)                          | Description                                  | Used In                    |
|----------------|----------------------------------------|----------------------------------------------|----------------------------|
| App Shell      | `.app-shell`                           | Full-page flex wrapper (sidebar + main)      | SCR-003 to SCR-021         |
| Sidebar        | `.sidebar`                             | 240px left nav, teal-900 bg                  | All authenticated screens  |
| Main Content   | `.main-content`                        | Flex-1 scrollable content area               | All authenticated screens  |
| Page Header    | `.page-header`                         | 64px top header with page title              | All authenticated screens  |
| Page Body      | `.page-body`                           | Padded content area below header             | All authenticated screens  |
| Grid 2-col     | `.grid.grid--2`                        | 2-column responsive grid                     | SCR-006, SCR-010, SCR-015  |
| Grid 3-col     | `.grid.grid--3`                        | 3-column grid                                | SCR-005, SCR-008           |
| Grid 4-col     | `.grid.grid--4`                        | 4-column stats grid                          | SCR-003, 013, 014, 019, 020|

---

## Card Components

| Component      | CSS Class(es)                          | Description                                  | Used In                         |
|----------------|----------------------------------------|----------------------------------------------|---------------------------------|
| Card           | `.card`                                | White surface with border and shadow         | All screens                     |
| Stat Card      | `.stat-card`                           | KPI card with icon, label, value, delta      | SCR-003, 013, 014, 019, 020     |
| Stat Card trend| `.stat-card__delta--up/down`           | Up/down trend color indicator                | SCR-003, 013                    |

---

## Form Components

| Component      | CSS Class(es)                          | Description                                  | Used In                             |
|----------------|----------------------------------------|----------------------------------------------|-------------------------------------|
| Form Group     | `.form-group`                          | Label + input vertical stack                 | All forms                           |
| Form Label     | `.form-label`                          | Input label, 14px medium                     | All forms                           |
| Form Input     | `.form-input`                          | Text/email/password input fields             | SCR-001–010, 015, 020               |
| Form Select    | `.form-input.form-select`              | Select dropdown                              | SCR-009, 014, 018, 021              |
| Textarea       | `.form-input` (textarea)               | Multiline text input                         | SCR-009, 015, OVL-006              |
| Search Bar     | `.search-bar`                          | Pill-style search with icon                  | SCR-014, 015, 018, 020, 021         |
| Form Error     | `.form-error`                          | Red validation error message                 | SCR-002, 009                        |
| Form Hint      | `.form-hint`                           | Gray helper text below input                 | SCR-002, 006, 010                   |
| Required Mark  | `.form-label__required`                | Red asterisk for required fields             | SCR-002, 005, 006, 008–010          |

---

## Button Components

| Component      | CSS Class(es)                          | Description                                  | Used In                         |
|----------------|----------------------------------------|----------------------------------------------|---------------------------------|
| Primary Button | `.btn.btn--primary`                    | Teal filled CTA                              | All screens                     |
| Secondary Button| `.btn.btn--secondary`                 | Gray outline / subdued                       | All modals, SCR-004a            |
| Ghost Button   | `.btn.btn--ghost`                      | Transparent, border-only                     | SCR-003, 012, OVL-001, 002      |
| Danger Button  | `.btn.btn--danger`                     | Red destructive action                       | OVL-003, SCR-014                |
| Icon Button    | `.btn.btn--icon`                       | Square icon-only button                      | SCR-011, drawers                |
| Small Button   | `.btn.btn--sm`                         | Compact button variant                       | Tables, OVL-005                 |
| Large Button   | `.btn.btn--lg`                         | Oversized button for hero CTAs               | SCR-007                         |

---

## Navigation Components

| Component      | CSS Class(es)                          | Description                                  | Used In                             |
|----------------|----------------------------------------|----------------------------------------------|-------------------------------------|
| Nav Item       | `.nav-item`, `.nav-item--active`       | Sidebar navigation links                     | All authenticated screens           |
| Nav Section    | `.nav-section`                         | Sidebar section divider/label                | SCR-013 (Staff), SCR-020 (Admin)    |
| Tabs           | `.tabs`, `.tab`, `.tab--active`        | Horizontal tab navigation                    | SCR-016 (5 tabs), SCR-011           |
| Breadcrumb     | `.breadcrumb`, `.breadcrumb__item`     | Page hierarchy navigation                    | SCR-004, SCR-008, SCR-009           |
| Stepper        | `.stepper`, `.step`, `.step--*`        | Multi-step progress indicator                | SCR-005, SCR-008, SCR-009           |
| Bottom Nav     | `.bottom-nav`, `.bottom-nav__item`     | Mobile bottom bar (responsive)               | Responsive breakpoints              |

---

## Feedback Components

| Component      | CSS Class(es)                          | Description                                  | Used In                             |
|----------------|----------------------------------------|----------------------------------------------|-------------------------------------|
| Alert          | `.alert.alert--{info,warning,error,success}` | Contextual message bar                 | SCR-002a, 003, 006, 015, 016, OVL-003|
| Toast          | `.toast.toast--{success,warning,error}`| Dismissible notification                     | SCR-003, 019, OVL-002              |
| Badge          | `.badge.badge--{primary,warning,error,success,neutral}` | Status/role pill            | All tables, SCR-003, 013–020       |
| Progress Bar   | `.progress`, `.progress__bar`          | Linear progress indicator                    | SCR-008, 011, 017, OVL-001         |
| Skeleton       | `.skeleton`, `.skeleton--card`         | Loading placeholder                          | Background views in overlays        |
| Risk Badge     | `.risk-badge.risk--{high,medium,low}`  | Clinical risk level indicator               | SCR-014, 016                        |

---

## Overlay Components

| Component      | CSS Class(es)                          | Description                                  | Used In                             |
|----------------|----------------------------------------|----------------------------------------------|-------------------------------------|
| Modal Overlay  | `.modal-overlay`                       | Full-screen dim backdrop (z:1000)            | OVL-001, 003, 004, 006             |
| Modal          | `.modal`                               | Centered dialog card (max-w: 480px)          | OVL-001, 003, 004, 006             |
| Modal Header   | `.modal__header`                       | Modal title bar                              | All modals                          |
| Modal Body     | `.modal__body`                         | Scrollable content area                      | All modals                          |
| Modal Footer   | `.modal__footer`                       | Action buttons row                           | All modals                          |
| Modal Close    | `.modal__close`                        | X close button                               | All modals                          |
| Drawer         | `.drawer`                              | Right-side panel 400px (z:900)               | OVL-005                             |
| Drawer Header  | `.drawer__header`                      | Drawer title bar with close                  | OVL-005                             |
| Drawer Body    | `.drawer__body`                        | Scrollable drawer content                    | OVL-005                             |
| Drawer Footer  | `.drawer__footer`                      | Drawer action buttons                        | OVL-005                             |

---

## Data Display Components

| Component      | CSS Class(es)                          | Description                                  | Used In                             |
|----------------|----------------------------------------|----------------------------------------------|-------------------------------------|
| Table          | `table`, `thead`, `tbody`, `tr`, `td`  | Standard data table with hover state         | SCR-003–005, 013–021               |
| Slot Grid      | `.slot-grid`, `.slot-chip.slot-chip--{available,selected,full}` | Appointment time picker | SCR-005             |
| Chat Bubble    | `.chat-msg.chat-msg--{ai,patient}`     | Conversational intake messages               | SCR-008                             |
| Chat Input     | `.chat-input-bar`                      | Intake chat input with send button           | SCR-008                             |
| Quick Reply    | `.quick-reply`                         | Pre-built response chips                     | SCR-008                             |
| Drop Zone      | `.dropzone`                            | File upload drag-and-drop area               | SCR-011                             |
| File Item      | `.file-item`                           | Uploaded file row with status                | SCR-011                             |
| Conflict Indicator | `.conflict-indicator`              | Orange warning icon on conflicting data      | SCR-016                             |
| PHI Masked     | `.phi-masked`                          | Blurred PHI field with reveal button         | SCR-010, 015, 016                  |
| Source Badge   | `.source-badge.source-badge--{ehr,patient,ai}` | Clinical data provenance tag       | SCR-016, OVL-005                   |

---

## Miscellaneous

| Component      | CSS Class(es)                          | Description                                  | Used In                             |
|----------------|----------------------------------------|----------------------------------------------|-------------------------------------|
| Wireframe Meta | `.wf-meta`                             | Annotation bar at page top (ID, screen name) | All 31 wireframe files              |
| Page Container | `.page-container`                      | Max-width centered content wrapper           | Public screens (SCR-001, 002, 002a) |
| Empty State    | `.empty-state`                         | Centered no-data illustration + text         | SCR-003, 014, 017                  |
| Divider        | `.divider`                             | Horizontal rule with optional label          | SCR-001, OVL-006                   |
