# Task - TASK_004

## Requirement Reference

- **User Story**: US_033 — Automated Multi-Channel Reminders with Configurable Intervals
- **Story Location**: `.propel/context/tasks/EP-006/us_033/us_033.md`
- **Acceptance Criteria**:
  - AC-3: Given a Staff or Admin user changes the default reminder intervals in system settings, when the new intervals are saved, then future reminder schedules are recalculated using the new intervals.

## Design References (Frontend Tasks Only)

| Reference Type         | Value |
|------------------------|-------|
| **UI Impact**          | Yes   |
| **Figma URL**          | N/A   |
| **Wireframe Status**   | PENDING |
| **Wireframe Type**     | N/A   |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-settings-reminders.[html\|png\|jpg]` or provide external URL |
| **Screen Spec**        | N/A (figma_spec.md not yet generated for this screen) |
| **UXR Requirements**   | N/A   |
| **Design Tokens**      | N/A (designsystem.md not yet generated) |

> **Wireframe Status: PENDING** — US_033 specifies no wireframe yet. Implement using system settings UI patterns consistent with existing admin settings screens.

### **CRITICAL: Wireframe Implementation Requirement**
Wireframe is PENDING. Implement UI following the existing system settings page layout patterns in the Angular codebase. When wireframe becomes AVAILABLE, run `/analyze-ux` to validate alignment.

## Applicable Technology Stack

| Layer          | Technology   | Version |
|----------------|--------------|---------|
| Frontend       | Angular      | 18.x    |
| Frontend State | NgRx Signals | 18.x    |
| Auth           | JWT + RBAC   | —       |
| Testing — E2E  | Playwright   | 1.x     |
| AI/ML          | N/A          | N/A     |
| Mobile         | N/A          | N/A     |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type       | Value |
|----------------------|-------|
| **AI Impact**        | No    |
| **AIR Requirements** | N/A   |
| **AI Pattern**       | N/A   |
| **Prompt Template**  | N/A   |
| **Guardrails Config**| N/A   |
| **Model Provider**   | N/A   |

## Mobile References (Mobile Tasks Only)

| Reference Type      | Value |
|---------------------|-------|
| **Mobile Impact**   | No    |
| **Platform Target** | N/A   |
| **Min OS Version**  | N/A   |
| **Mobile Framework**| N/A   |

## Task Overview

Implement an Angular 18 standalone component (`ReminderSettingsComponent`) accessible at `/settings/reminders` for Staff and Admin users. The component presents the current reminder intervals (48h, 24h, 2h by default) as a dynamic form array allowing users to add, remove, and modify interval values. On save, the component calls `PUT /api/settings/reminders` and provides success/error feedback. The route is guarded to block Patient role access. NgRx Signals state manages loading, saving, and error states reactively.

## Dependent Tasks

- **task_003_be_reminder_settings_api.md** — `GET /api/settings/reminders` and `PUT /api/settings/reminders` endpoints must be deployed before connecting this component.

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `ReminderSettingsComponent` | `app/settings` | CREATE |
| `ReminderSettingsService` | `app/settings` | CREATE |
| `reminderSettings.signal.ts` | `app/settings` | CREATE |
| Settings route configuration | `app/settings/settings.routes.ts` | MODIFY |
| Settings nav menu | `app/layout` or `app/settings` | MODIFY (add Reminders link, Staff/Admin only) |
| `StaffAdminGuard` | `app/auth` | CONSUME |

## Implementation Plan

1. **Create `ReminderSettingsComponent`** as an Angular 18 standalone component (`standalone: true`). Use Angular reactive forms (`FormArray`) for the dynamic list of interval hour inputs.
2. **Load current intervals on init** — inject `ReminderSettingsService` and call `getIntervals()` which issues `GET /api/settings/reminders`. Populate `FormArray` with returned values.
3. **Form array management** — provide "Add Interval" button (appends a new `FormControl<number>`) and a remove button per row (removes interval from the array). Minimum 1 interval enforced (disable remove button when only 1 remains).
4. **Form validation** — apply synchronous validators: each value must be a positive integer, all values must be unique within the array (cross-field `FormArray` validator), maximum 10 intervals.
5. **Save action** — on "Save" button click, dispatch `PUT /api/settings/reminders { intervalHours: [...] }`. While saving, set NgRx signal `isSaving = true` and disable form controls to prevent double-submit.
6. **Feedback** — display success toast/inline message on 200 response. Display backend validation errors (400) inline next to the relevant field or as a form-level error banner.
7. **RBAC guard** — apply `StaffAdminGuard` (canActivate) to the `/settings/reminders` route. Hide the "Reminder Settings" nav link from the Patient role using `*ngIf` or Angular control flow `@if` based on user role signal.
8. **Accessibility** — each interval input labelled with `aria-label="Reminder interval N in hours"`. "Save" button has `aria-busy` attribute bound to `isSaving` signal (WCAG 2.2 AA).

### Component Structure

```typescript
// reminder-settings.component.ts
@Component({
  selector: 'app-reminder-settings',
  standalone: true,
  imports: [ReactiveFormsModule, CommonModule],
  templateUrl: './reminder-settings.component.html'
})
export class ReminderSettingsComponent implements OnInit {
  private readonly service = inject(ReminderSettingsService);

  form = new FormGroup({
    intervalHours: new FormArray<FormControl<number>>([])
  });

  isSaving = signal(false);
  loadError = signal<string | null>(null);
  saveError = signal<string | null>(null);

  ngOnInit() {
    this.service.getIntervals().subscribe({
      next: ({ intervalHours }) => {
        intervalHours.forEach(h => this.addInterval(h));
      },
      error: () => this.loadError.set('Failed to load settings. Please refresh.')
    });
  }

  addInterval(value = 0) {
    this.intervals.push(new FormControl<number>(value, {
      validators: [Validators.required, Validators.min(1), Validators.max(168)]
    }));
  }

  removeInterval(index: number) {
    if (this.intervals.length > 1) this.intervals.removeAt(index);
  }

  save() {
    if (this.form.invalid || this.isSaving()) return;
    this.isSaving.set(true);
    this.service.updateIntervals(this.intervals.value as number[]).subscribe({
      next: () => { /* show success toast */ this.isSaving.set(false); },
      error: (err) => { this.saveError.set(err.error?.message ?? 'Save failed'); this.isSaving.set(false); }
    });
  }

  get intervals() { return this.form.controls.intervalHours; }
}
```

## Current Project State

```
app/
├── settings/
│   ├── settings.routes.ts         # Route config — to be updated
│   └── (no reminder-settings component yet)
├── auth/
│   └── guards/
│       └── staff-admin.guard.ts   # Existing guard (or create if absent)
└── layout/
    └── nav/
        └── nav.component.html     # Settings nav — add Reminders link
```

> Placeholder — update with actual paths as dependent tasks complete.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `app/settings/reminder-settings/reminder-settings.component.ts` | Standalone Angular component |
| CREATE | `app/settings/reminder-settings/reminder-settings.component.html` | Template with dynamic form array |
| CREATE | `app/settings/reminder-settings/reminder-settings.component.scss` | Component styles |
| CREATE | `app/settings/reminder-settings/reminder-settings.service.ts` | HTTP service for GET/PUT intervals |
| MODIFY | `app/settings/settings.routes.ts` | Add `/settings/reminders` route with StaffAdminGuard |
| MODIFY | `app/layout/nav/nav.component.html` | Add "Reminder Settings" nav link (Staff/Admin only) |

## External References

- [Angular 18 Reactive Forms — FormArray](https://angular.dev/guide/forms/reactive-forms)
- [Angular 18 Standalone components](https://angular.dev/guide/components/importing)
- [NgRx Signals — signal state management](https://ngrx.io/guide/signals)
- [Angular Route Guards — canActivate](https://angular.dev/api/router/CanActivateFn)
- [WCAG 2.2 AA — aria-busy for loading states](https://www.w3.org/WAI/WCAG22/Techniques/aria/ARIA18)

## Build Commands

```bash
# Generate component (Angular CLI)
cd app
ng generate component settings/reminder-settings --standalone

# Generate service
ng generate service settings/reminder-settings/reminder-settings

# Serve frontend
ng serve

# Build for production
ng build --configuration production
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Component loads and displays current intervals from `GET /api/settings/reminders`
- [ ] Adding an interval appends a new input row; removing reduces the list (minimum 1 enforced)
- [ ] Duplicate interval values display inline validation error before save
- [ ] Successful save shows success message; form re-enables
- [ ] Failed save (400) shows backend error message; form re-enables
- [ ] Patient role cannot access `/settings/reminders` (redirected by StaffAdminGuard)
- [ ] "Reminder Settings" nav link hidden for Patient role

## Implementation Checklist

- [ ] Create `ReminderSettingsComponent` (standalone) with `ReactiveFormsModule` and dynamic `FormArray`
- [ ] Load current intervals on `ngOnInit` via `ReminderSettingsService.getIntervals()`
- [ ] Implement "Add Interval" and "Remove" buttons with minimum-1-item enforcement
- [ ] Add cross-field `FormArray` validator for unique values and max-10 limit
- [ ] Implement save action with `isSaving` NgRx signal — disable form controls while in-flight to prevent double-submit
- [ ] Display success toast on 200; display backend validation message on 400 (OWASP A03 — no raw error stack displayed)
- [ ] Guard `/settings/reminders` route with `StaffAdminGuard`; hide nav link using `@if (isStaffOrAdmin())` Angular control flow
- [ ] Apply `aria-label` to each interval input and `aria-busy` to save button (WCAG 2.2 AA)
