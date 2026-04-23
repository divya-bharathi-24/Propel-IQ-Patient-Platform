import {
  HttpClientTestingModule,
  HttpTestingController,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ReminderTriggerError,
  StaffReminderService,
  TriggerReminderResponseDto,
} from './staff-reminder.service';

describe('StaffReminderService', () => {
  let service: StaffReminderService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
    });
    service = TestBed.inject(StaffReminderService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('triggerManualReminder', () => {
    const appointmentId = 'appt-uuid-123';
    const url = `/api/staff/appointments/${appointmentId}/reminders/trigger`;

    it('emits TriggerReminderResponseDto on HTTP 200', () => {
      const mockResponse: TriggerReminderResponseDto = {
        sentAt: '2026-04-23T10:00:00Z',
        triggeredByStaffName: 'Dr. Jane Smith',
      };

      let result: TriggerReminderResponseDto | undefined;
      service.triggerManualReminder(appointmentId).subscribe((r) => {
        result = r;
      });

      const req = httpMock.expectOne(url);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({});
      req.flush(mockResponse);

      expect(result).toEqual(mockResponse);
    });

    it('maps HTTP 422 to CANCELLED_APPOINTMENT error', () => {
      let error: ReminderTriggerError | undefined;
      service.triggerManualReminder(appointmentId).subscribe({
        error: (e: ReminderTriggerError) => {
          error = e;
        },
      });

      httpMock
        .expectOne(url)
        .flush(
          { message: 'Appointment cancelled' },
          { status: 422, statusText: 'Unprocessable Entity' },
        );

      expect(error?.type).toBe('CANCELLED_APPOINTMENT');
      expect(error?.message).toBe(
        'Cannot send reminders for cancelled appointments.',
      );
    });

    it('maps HTTP 429 to COOLDOWN error with retryAfterSeconds', () => {
      let error: ReminderTriggerError | undefined;
      service.triggerManualReminder(appointmentId).subscribe({
        error: (e: ReminderTriggerError) => {
          error = e;
        },
      });

      httpMock
        .expectOne(url)
        .flush(
          { retryAfterSeconds: 180 },
          { status: 429, statusText: 'Too Many Requests' },
        );

      expect(error?.type).toBe('COOLDOWN');
      expect(error?.retryAfterSeconds).toBe(180);
    });

    it('uses 300 as default retryAfterSeconds when 429 body lacks the field', () => {
      let error: ReminderTriggerError | undefined;
      service.triggerManualReminder(appointmentId).subscribe({
        error: (e: ReminderTriggerError) => {
          error = e;
        },
      });

      httpMock
        .expectOne(url)
        .flush({}, { status: 429, statusText: 'Too Many Requests' });

      expect(error?.retryAfterSeconds).toBe(300);
    });

    it('maps HTTP 500 to GENERIC error with message from body', () => {
      let error: ReminderTriggerError | undefined;
      service.triggerManualReminder(appointmentId).subscribe({
        error: (e: ReminderTriggerError) => {
          error = e;
        },
      });

      httpMock
        .expectOne(url)
        .flush(
          { message: 'Internal server error' },
          { status: 500, statusText: 'Internal Server Error' },
        );

      expect(error?.type).toBe('GENERIC');
      expect(error?.message).toBe('Internal server error');
    });

    it('uses fallback message for GENERIC error when body has no message', () => {
      let error: ReminderTriggerError | undefined;
      service.triggerManualReminder(appointmentId).subscribe({
        error: (e: ReminderTriggerError) => {
          error = e;
        },
      });

      httpMock
        .expectOne(url)
        .flush({}, { status: 503, statusText: 'Service Unavailable' });

      expect(error?.message).toBe('Failed to send reminder. Please try again.');
    });
  });
});
