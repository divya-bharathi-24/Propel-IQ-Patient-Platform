import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { SendReminderNowButtonComponent } from './send-reminder-now-button.component';

describe('SendReminderNowButtonComponent', () => {
  let fixture: ComponentFixture<SendReminderNowButtonComponent>;
  let component: SendReminderNowButtonComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SendReminderNowButtonComponent, NoopAnimationsModule],
    }).compileComponents();

    fixture = TestBed.createComponent(SendReminderNowButtonComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('renders "Send Reminder Now" label in idle state', () => {
    const btn = fixture.debugElement.query(By.css('button'));
    expect(btn.nativeElement.textContent.trim()).toBe('Send Reminder Now');
  });

  it('is enabled when neither isLoading nor disabled', () => {
    const btn = fixture.debugElement.query(By.css('button'));
    expect(btn.nativeElement.disabled).toBeFalse();
  });

  it('is disabled when disabled input is true', () => {
    component.disabled = true;
    fixture.detectChanges();
    const btn = fixture.debugElement.query(By.css('button'));
    expect(btn.nativeElement.disabled).toBeTrue();
  });

  it('is disabled and shows spinner when isLoading is true', () => {
    component.isLoading = true;
    fixture.detectChanges();
    const btn = fixture.debugElement.query(By.css('button'));
    expect(btn.nativeElement.disabled).toBeTrue();
    expect(btn.nativeElement.getAttribute('aria-busy')).toBe('true');
    const spinner = fixture.debugElement.query(By.css('mat-spinner'));
    expect(spinner).toBeTruthy();
  });

  it('shows "Sending…" label when isLoading is true', () => {
    component.isLoading = true;
    fixture.detectChanges();
    const label = fixture.debugElement.query(By.css('.btn-label'));
    expect(label.nativeElement.textContent.trim()).toBe('Sending…');
  });

  it('emits sendClicked when clicked in idle state', () => {
    let emitted = false;
    component.sendClicked.subscribe(() => {
      emitted = true;
    });
    const btn = fixture.debugElement.query(By.css('button'));
    btn.nativeElement.click();
    expect(emitted).toBeTrue();
  });

  it('does not have aria-busy attribute in idle state', () => {
    const btn = fixture.debugElement.query(By.css('button'));
    expect(btn.nativeElement.getAttribute('aria-busy')).toBeNull();
  });
});
