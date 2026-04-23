import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { provideRouter } from '@angular/router';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { By } from '@angular/platform-browser';
import { signal } from '@angular/core';
import { AppointmentDetailComponent } from './appointment-detail.component';
import { StaffAppointmentStore } from '../../state/staff-appointment.store';
import { ReminderState } from '../../state/staff-appointment.store';
import { LastManualReminderDto } from '../../models/staff-appointment.models';

/** Builds a minimal mock store with configurable signal values. */
function buildMockStore(overrides: {
  detailLoadingState?: string;
  detailErrorMessage?: string | null;
  selectedAppointment?: object | null;
  reminderState?: ReminderState;
  lastManualReminder?: LastManualReminderDto | null;
  cooldownSecondsRemaining?: number;
  reminderErrorReason?: string | null;
}) {
  return {
    detailLoadingState: signal(overrides.detailLoadingState ?? 'idle'),
    detailErrorMessage: signal(overrides.detailErrorMessage ?? null),
    selectedAppointment: signal(overrides.selectedAppointment ?? null),
    reminderState: signal(overrides.reminderState ?? 'idle'),
    lastManualReminder: signal(overrides.lastManualReminder ?? null),
    cooldownSecondsRemaining: signal(overrides.cooldownSecondsRemaining ?? 0),
    reminderErrorReason: signal(overrides.reminderErrorReason ?? null),
    loadAppointmentById: jasmine.createSpy('loadAppointmentById'),
    triggerReminder: jasmine.createSpy('triggerReminder'),
    resetReminderState: jasmine.createSpy('resetReminderState'),
  };
}

describe('AppointmentDetailComponent', () => {
  let fixture: ComponentFixture<AppointmentDetailComponent>;
  let component: AppointmentDetailComponent;

  async function createComponent(
    storeOverrides: Parameters<typeof buildMockStore>[0] = {},
    routeId = 'appt-id-001',
  ) {
    await TestBed.configureTestingModule({
      imports: [AppointmentDetailComponent, NoopAnimationsModule],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: { get: () => routeId } },
          },
        },
        {
          provide: StaffAppointmentStore,
          useValue: buildMockStore(storeOverrides),
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AppointmentDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('should create', async () => {
    await createComponent();
    expect(component).toBeTruthy();
  });

  it('calls loadAppointmentById with the route id on init', async () => {
    await createComponent({}, 'test-appt-42');
    const store = TestBed.inject(
      StaffAppointmentStore,
    ) as unknown as ReturnType<typeof buildMockStore>;
    expect(store.loadAppointmentById).toHaveBeenCalledWith('test-appt-42');
  });

  it('shows spinner when detailLoadingState is loading', async () => {
    await createComponent({ detailLoadingState: 'loading' });
    const spinner = fixture.debugElement.query(By.css('mat-spinner'));
    expect(spinner).toBeTruthy();
  });

  it('shows load-error message when detailLoadingState is error', async () => {
    await createComponent({
      detailLoadingState: 'error',
      detailErrorMessage: 'Not found',
    });
    const err = fixture.debugElement.query(By.css('.load-error'));
    expect(err.nativeElement.textContent).toContain('Not found');
  });

  it('renders the Send Reminder Now button when appointment is loaded', async () => {
    await createComponent({
      detailLoadingState: 'loaded',
      selectedAppointment: {
        id: 'a1',
        patientName: 'Alice',
        timeSlot: '09:00',
        specialty: 'Cardiology',
        status: 'Booked',
        noShowRisk: null,
        patientEmail: 'alice@example.com',
        patientPhone: '555-1234',
        lastManualReminder: null,
      },
      reminderState: 'idle',
    });
    const btn = fixture.debugElement.query(
      By.css('app-send-reminder-now-button'),
    );
    expect(btn).toBeTruthy();
  });

  it('shows success panel when reminderState is success', async () => {
    await createComponent({
      detailLoadingState: 'loaded',
      selectedAppointment: {
        id: 'a1',
        patientName: 'Alice',
        timeSlot: '09:00',
        specialty: 'Cardiology',
        status: 'Booked',
        noShowRisk: null,
        patientEmail: 'alice@example.com',
        patientPhone: '555-1234',
        lastManualReminder: null,
      },
      reminderState: 'success',
      lastManualReminder: {
        sentAt: '2026-04-23T10:00:00Z',
        triggeredByStaffName: 'Dr. Jane Smith',
      },
    });
    const card = fixture.debugElement.query(By.css('.feedback-card--success'));
    expect(card).toBeTruthy();
    expect(card.nativeElement.textContent).toContain('Dr. Jane Smith');
  });

  it('shows cooldown panel when reminderState is cooldown', async () => {
    await createComponent({
      detailLoadingState: 'loaded',
      selectedAppointment: {
        id: 'a1',
        patientName: 'Alice',
        timeSlot: '09:00',
        specialty: 'Cardiology',
        status: 'Booked',
        noShowRisk: null,
        patientEmail: 'alice@example.com',
        patientPhone: '555-1234',
        lastManualReminder: null,
      },
      reminderState: 'cooldown',
      cooldownSecondsRemaining: 240,
    });
    const cooldown = fixture.debugElement.query(By.css('.feedback-cooldown'));
    expect(cooldown).toBeTruthy();
    expect(cooldown.nativeElement.textContent).toContain('4 minutes');
  });

  it('shows error panel with retry button when reminderState is error', async () => {
    await createComponent({
      detailLoadingState: 'loaded',
      selectedAppointment: {
        id: 'a1',
        patientName: 'Alice',
        timeSlot: '09:00',
        specialty: 'Cardiology',
        status: 'Booked',
        noShowRisk: null,
        patientEmail: 'alice@example.com',
        patientPhone: '555-1234',
        lastManualReminder: null,
      },
      reminderState: 'error',
      reminderErrorReason: 'Cannot send reminders for cancelled appointments.',
    });
    const card = fixture.debugElement.query(By.css('.feedback-card--error'));
    expect(card).toBeTruthy();
    expect(card.nativeElement.textContent).toContain(
      'Cannot send reminders for cancelled appointments.',
    );
    const retryBtn = fixture.debugElement.query(By.css('.retry-btn'));
    expect(retryBtn).toBeTruthy();
  });

  it('calls triggerReminder when retry button is clicked', async () => {
    await createComponent({
      detailLoadingState: 'loaded',
      selectedAppointment: {
        id: 'a1',
        patientName: 'Alice',
        timeSlot: '09:00',
        specialty: 'Cardiology',
        status: 'Booked',
        noShowRisk: null,
        patientEmail: 'alice@example.com',
        patientPhone: '555-1234',
        lastManualReminder: null,
      },
      reminderState: 'error',
      reminderErrorReason: 'Service error.',
    });
    const retryBtn = fixture.debugElement.query(By.css('.retry-btn'));
    retryBtn.nativeElement.click();
    const store = TestBed.inject(
      StaffAppointmentStore,
    ) as unknown as ReturnType<typeof buildMockStore>;
    expect(store.triggerReminder).toHaveBeenCalledWith('appt-id-001');
  });

  it('cooldownMinutes rounds up fractional minutes', async () => {
    await createComponent({ cooldownSecondsRemaining: 61 });
    // 61s → ceil(61/60) = 2 minutes
    expect(
      (component as unknown as { cooldownMinutes(): number }).cooldownMinutes(),
    ).toBe(2);
  });
});
