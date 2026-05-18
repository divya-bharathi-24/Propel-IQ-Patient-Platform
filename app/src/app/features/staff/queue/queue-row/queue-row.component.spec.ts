import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { QueueRowComponent } from './queue-row.component';
import { QueueItem } from '../queue.models';

// ── Fixtures ──────────────────────────────────────────────────────────────────

function makeItem(overrides: Partial<QueueItem> = {}): QueueItem {
  return {
    appointmentId: 'appt-001',
    patientId: 'patient-001',
    patientName: 'Marcus Johnson',
    queuePosition: 1,
    chiefComplaint: 'Chest pain, shortness of breath',
    timeSlotStart: '08:42',
    waitTime: '47 min',
    riskLevel: 'High',
    bookingType: 'SelfBooked',
    arrivalStatus: 'Waiting',
    arrivalTimestamp: null,
    ...overrides,
  };
}

// ── Suite ─────────────────────────────────────────────────────────────────────

/**
 * Regression tests for QueueRowComponent (bug_queue_row_column_mismatch).
 *
 * Verifies that the row renders exactly 8 <td> cells aligned with the SCR-014
 * wireframe table header: Position | Patient | Chief Complaint | Arrival Time |
 * Wait | Risk | Status | Actions.
 */
describe('QueueRowComponent', () => {
  let fixture: ComponentFixture<QueueRowComponent>;
  let component: QueueRowComponent;

  async function setup(item: QueueItem = makeItem()) {
    await TestBed.configureTestingModule({
      imports: [QueueRowComponent],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(QueueRowComponent);
    component = fixture.componentInstance;
    component.item = item;
    fixture.detectChanges();
  }

  // ── Structure ───────────────────────────────────────────────────────────────

  it('should create', async () => {
    await setup();
    expect(component).toBeTruthy();
  });

  it('renders exactly 8 <td> cells per row (regression: was 5)', async () => {
    await setup();
    const cells = fixture.debugElement.queryAll(By.css('td'));
    expect(cells.length).toBe(8);
  });

  // ── Position column (regression: was showing timeSlotStart "09:00:00") ──────

  it('displays queue position as #N in the first cell', async () => {
    await setup(makeItem({ queuePosition: 3 }));
    const positionCell = fixture.debugElement.query(By.css('.col-position'));
    expect(positionCell.nativeElement.textContent.trim()).toBe('#3');
  });

  it('does NOT render a raw time string in the position cell', async () => {
    await setup(makeItem({ queuePosition: 1, timeSlotStart: '09:00' }));
    const positionCell = fixture.debugElement.query(By.css('.col-position'));
    expect(positionCell.nativeElement.textContent).not.toContain('09:00');
  });

  // ── Chief Complaint column (regression: was showing BookingTypeBadge) ───────

  it('displays chiefComplaint text in the complaint cell', async () => {
    await setup(makeItem({ chiefComplaint: 'Diabetes follow-up' }));
    const complaintCell = fixture.debugElement.query(By.css('.col-complaint'));
    expect(complaintCell.nativeElement.textContent.trim()).toBe('Diabetes follow-up');
  });

  it('does NOT render a booking-type badge in the complaint cell', async () => {
    await setup();
    const badge = fixture.debugElement.query(By.css('.col-complaint app-booking-type-badge'));
    expect(badge).toBeNull();
  });

  it('shows em-dash when chiefComplaint is null', async () => {
    await setup(makeItem({ chiefComplaint: null }));
    const complaintCell = fixture.debugElement.query(By.css('.col-complaint'));
    expect(complaintCell.nativeElement.textContent.trim()).toBe('—');
  });

  // ── Arrival Time column (regression: was absent) ─────────────────────────

  it('renders arrival time in the col-arrival cell', async () => {
    await setup(makeItem({ timeSlotStart: '08:42' }));
    const arrivalCell = fixture.debugElement.query(By.css('.col-arrival'));
    expect(arrivalCell.nativeElement.textContent.trim()).toBe('08:42');
  });

  // ── Wait Time column (regression: was absent) ───────────────────────────

  it('renders wait time when provided', async () => {
    await setup(makeItem({ waitTime: '47 min' }));
    const waitCell = fixture.debugElement.query(By.css('.col-wait'));
    expect(waitCell.nativeElement.textContent.trim()).toBe('47 min');
  });

  it('renders em-dash when waitTime is null', async () => {
    await setup(makeItem({ waitTime: null }));
    const waitCell = fixture.debugElement.query(By.css('.col-wait'));
    expect(waitCell.nativeElement.textContent.trim()).toBe('—');
  });

  it('applies wait--overdue class only when waitTime is non-null', async () => {
    await setup(makeItem({ waitTime: '10 min' }));
    const waitCell = fixture.debugElement.query(By.css('.col-wait'));
    expect(waitCell.nativeElement.classList).toContain('wait--overdue');
  });

  it('does NOT apply wait--overdue class when waitTime is null', async () => {
    await setup(makeItem({ waitTime: null }));
    const waitCell = fixture.debugElement.query(By.css('.col-wait'));
    expect(waitCell.nativeElement.classList).not.toContain('wait--overdue');
  });

  // ── Risk column (regression: was absent) ───────────────────────────────────

  it('renders risk badge with risk-badge--high class for High risk', async () => {
    await setup(makeItem({ riskLevel: 'High' }));
    const badge = fixture.debugElement.query(By.css('.risk-badge--high'));
    expect(badge).not.toBeNull();
    expect(badge.nativeElement.textContent.trim()).toBe('High');
  });

  it('renders risk badge with risk-badge--medium class for Medium risk', async () => {
    await setup(makeItem({ riskLevel: 'Medium' }));
    const badge = fixture.debugElement.query(By.css('.risk-badge--medium'));
    expect(badge).not.toBeNull();
    expect(badge.nativeElement.textContent.trim()).toBe('Medium');
  });

  it('renders risk badge with risk-badge--low class for Low risk', async () => {
    await setup(makeItem({ riskLevel: 'Low' }));
    const badge = fixture.debugElement.query(By.css('.risk-badge--low'));
    expect(badge).not.toBeNull();
    expect(badge.nativeElement.textContent.trim()).toBe('Low');
  });

  it('renders em-dash and no risk badge when riskLevel is null', async () => {
    await setup(makeItem({ riskLevel: null }));
    const badge = fixture.debugElement.query(By.css('.risk-badge'));
    expect(badge).toBeNull();
    const col = fixture.debugElement.query(By.css('.col-risk'));
    expect(col.nativeElement.textContent.trim()).toBe('—');
  });

  // ── Status column ──────────────────────────────────────────────────────────

  it('renders the queue-status-chip component in the status cell', async () => {
    await setup();
    const chip = fixture.debugElement.query(By.css('.col-status app-queue-status-chip'));
    expect(chip).not.toBeNull();
  });

  // ── Actions column ─────────────────────────────────────────────────────────

  it('shows "Arrived" button when arrivalStatus is Waiting', async () => {
    await setup(makeItem({ arrivalStatus: 'Waiting' }));
    const btn = fixture.debugElement.query(By.css('.btn-arrived'));
    expect(btn).not.toBeNull();
  });

  it('hides "Arrived" button when arrivalStatus is Arrived', async () => {
    const today = new Date().toISOString();
    await setup(makeItem({ arrivalStatus: 'Arrived', arrivalTimestamp: today }));
    const btn = fixture.debugElement.query(By.css('.btn-arrived'));
    expect(btn).toBeNull();
  });

  it('shows 360° link for named patients (patientId non-null)', async () => {
    await setup(makeItem({ patientId: 'patient-001' }));
    const link = fixture.debugElement.query(By.css('.btn-360'));
    expect(link).not.toBeNull();
  });

  it('hides 360° link for anonymous walk-ins (patientId null)', async () => {
    await setup(makeItem({ patientId: null }));
    const link = fixture.debugElement.query(By.css('.btn-360'));
    expect(link).toBeNull();
  });

  // ── High-risk row highlight ────────────────────────────────────────────────

  it('adds queue-row--high-risk class to the <tr> when riskLevel is High', async () => {
    await setup(makeItem({ riskLevel: 'High' }));
    const row = fixture.debugElement.query(By.css('tr.queue-row'));
    expect(row.nativeElement.classList).toContain('queue-row--high-risk');
  });

  it('does NOT add queue-row--high-risk class when riskLevel is Low', async () => {
    await setup(makeItem({ riskLevel: 'Low' }));
    const row = fixture.debugElement.query(By.css('tr.queue-row'));
    expect(row.nativeElement.classList).not.toContain('queue-row--high-risk');
  });

  // ── Event emitters ─────────────────────────────────────────────────────────

  it('emits markArrived with appointmentId when Arrived button is clicked', async () => {
    await setup(makeItem({ arrivalStatus: 'Waiting', appointmentId: 'appt-99' }));
    const emitted: string[] = [];
    component.markArrived.subscribe((id: string) => emitted.push(id));

    const btn = fixture.debugElement.query(By.css('.btn-arrived'));
    btn.nativeElement.click();

    expect(emitted).toEqual(['appt-99']);
  });
});
