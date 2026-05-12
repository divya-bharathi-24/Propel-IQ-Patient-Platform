# Design Tokens Applied — UniPatient Hi-Fi Wireframes

> Source of truth: `.propel/context/docs/designsystem.md`  
> Token stylesheet: `.propel/context/wireframes/Hi-Fi/_tokens.css`  
> All tokens use CSS custom properties (`var(--token-name)`)

---

## Color Palette Applied

### Brand / Primary Colors

| Token                  | Value     | Usage                                                          |
|------------------------|-----------|----------------------------------------------------------------|
| `--primary`            | `#0D7C8F` | Primary CTA buttons, active nav items, links, progress bars   |
| `--primary-hover`      | `#0A6070` | Hover state for primary buttons and links                      |
| `--primary-surface`    | `#E6F4F6` | Card backgrounds for highlighted content, slot selection       |
| `--color-teal-900`     | `#042D36` | Sidebar background for all authenticated screens               |
| `--color-teal-800`     | `#064D5C` | Sidebar hover states, secondary header elements                |
| `--color-teal-700`     | `#0A6070` | Sidebar active item text, info-level accents                   |
| `--color-teal-200`     | `#A8D9E0` | Subtle borders on teal-tinted cards                            |

### Semantic Colors

| Token                     | Value     | Usage                                                      |
|---------------------------|-----------|------------------------------------------------------------|
| `--color-success-50/700`  | Green tints| Verified insurance badge, upload success, booking confirm |
| `--color-warning-50/700`  | Amber tints| Conflict alerts, cancellation policy, session timer       |
| `--color-error-50/700`    | Red tints | Form validation, failed delivery row, cancel confirm icon  |
| `--color-info-50/700`     | Blue tints | Informational alerts (re-auth notice, policy hints)        |

### Neutral / Surface Colors

| Token              | Value     | Usage                                                           |
|--------------------|-----------|-----------------------------------------------------------------|
| `--bg-default`     | `#F9FAFB` | Page background for all screens                                 |
| `--bg-surface`     | `#FFFFFF` | Card and component surfaces                                     |
| `--bg-subtle`      | `#F3F4F6` | Muted row backgrounds, read-only fields                         |
| `--text-primary`   | `#111827` | Body copy and headings                                          |
| `--text-secondary` | `#6B7280` | Placeholder text, subtitles, metadata                           |
| `--border-default` | `#E5E7EB` | All borders and dividers                                        |

---

## Typography Scale Applied

| Token                 | Value         | Usage                                                  |
|-----------------------|---------------|--------------------------------------------------------|
| `--font-family`       | Inter          | Global font across all screens                        |
| `--font-size-xs`      | 12px          | Badges, metadata, table sub-text                       |
| `--font-size-sm`      | 14px          | Form labels, descriptions, secondary text              |
| `--font-size-base`    | 16px          | Body copy, table cells, form inputs                    |
| `--font-size-lg`      | 18px          | Card titles, section headers                           |
| `--font-size-xl`      | 20px          | Page titles, stat card values                          |
| `--font-size-2xl`     | 24px          | Stat card large numbers, modal headings                |
| `--font-size-3xl`     | 30px          | Dashboard KPI primary value                            |
| `--font-size-4xl`     | 36px          | Countdown timer (OVL-001), confirmation reference#     |
| `--font-weight-regular`| 400         | Body copy                                              |
| `--font-weight-medium`| 500           | Form labels, nav items, button text                    |
| `--font-weight-semibold`| 600         | Card headings, table column headers                    |
| `--font-weight-bold`  | 700           | Page titles, stat card values, confirmation numbers    |
| `--line-height-tight` | 1.25          | Headings                                               |
| `--line-height-normal`| 1.5           | Body text                                              |

---

## Spacing Scale Applied

| Token        | Value  | Usage                                                         |
|--------------|--------|---------------------------------------------------------------|
| `--space-1`  | 4px    | Icon gaps, tight text spacing                                 |
| `--space-2`  | 8px    | Badge internal padding, tab item gaps                         |
| `--space-3`  | 12px   | Button internal horizontal padding, form hint margin          |
| `--space-4`  | 16px   | Form group gap, card inner sections                           |
| `--space-5`  | 20px   | Modal body sections, alert padding                            |
| `--space-6`  | 24px   | Toast/modal margins, page-body padding top                    |
| `--space-8`  | 32px   | Page body horizontal padding, card outer padding              |
| `--space-10` | 40px   | Section vertical separation                                   |
| `--space-12` | 48px   | Hero section padding, large step marker size                  |

---

## Border Radius Applied

| Token            | Value  | Usage                                        |
|------------------|--------|----------------------------------------------|
| `--radius-sm`    | 4px    | Small chips, mini badges                     |
| `--radius-md`    | 6px    | Buttons, inputs, table rows                  |
| `--radius-lg`    | 8px    | Cards, alerts                                |
| `--radius-xl`    | 12px   | Modals, drawers, toast containers            |
| `--radius-2xl`   | 16px   | Dashboard stat cards                         |
| `--radius-full`  | 9999px | Avatar circles, pill-style badges, slot chips|

---

## Elevation / Shadow Applied

| Token          | CSS Value                                              | Usage                            |
|----------------|--------------------------------------------------------|----------------------------------|
| `--shadow-sm`  | `0 1px 2px rgba(0,0,0,.05)`                           | Stat cards, form inputs on focus |
| `--shadow-md`  | `0 4px 6px -1px rgba(0,0,0,.1)...`                   | Cards, sidebar                   |
| `--shadow-lg`  | `0 10px 15px -3px rgba(0,0,0,.1)...`                 | Modals                           |
| `--shadow-xl`  | `0 20px 25px -5px rgba(0,0,0,.1)...`                 | Toast notifications, drawers     |

---

## Motion / Animation Applied

| Token                     | Value              | Usage                                  |
|---------------------------|--------------------|-----------------------------------------|
| `--duration-fast`         | 150ms             | Button hover, badge state change        |
| `--duration-normal`       | 250ms             | Modal fade in/out, drawer slide         |
| `--duration-slow`         | 400ms             | Slot grid reveal, skeleton shimmer      |
| `--easing-standard`       | `cubic-bezier(0.4,0,0.2,1)` | General transitions            |
| `--easing-decelerate`     | `cubic-bezier(0,0,0.2,1)`   | Drawers opening, modals entering|
| `--easing-accelerate`     | `cubic-bezier(0.4,0,1,1)`   | Drawers closing, modals exiting |

---

## Z-Index Layers Applied

| Layer    | Value | Applied To                              |
|----------|-------|-----------------------------------------|
| Base     | 0     | Normal page content                     |
| Sticky   | 10    | Table headers, sidebar                  |
| Dropdown | 100   | Select menus, calendar popovers         |
| Drawer   | 900   | OVL-005 (Conflict Resolution Drawer)    |
| Modal    | 1000  | OVL-001, 003, 004, 006                  |
| Toast    | 1100  | OVL-002 (Slot Swap Toast)               |

---

## Token Coverage Summary

| Category       | Total Tokens | Used in Wireframes |
|----------------|-------------|---------------------|
| Colors         | 40+         | 32                  |
| Typography     | 18          | 18 (100%)           |
| Spacing        | 12          | 10                  |
| Border Radius  | 6           | 6 (100%)            |
| Elevation      | 4           | 4 (100%)            |
| Motion         | 6           | 4                   |
| Z-index        | 6           | 5                   |
| **Total**      | **~92**     | **~79 (86%)**       |
