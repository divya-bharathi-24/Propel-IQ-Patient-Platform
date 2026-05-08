import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-appointment-detail',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <div class="appointment-detail-page">
      <header class="page-header">
        <div>
          <a routerLink="/dashboard" class="back-link">← Back to Dashboard</a>
          <h1 class="page-title">Appointment Details</h1>
        </div>
        <div class="page-actions">
          <button class="btn btn--secondary" (click)="reschedule()">Reschedule</button>
          <button class="btn btn--danger" (click)="cancel()">Cancel</button>
        </div>
      </header>

      <div class="page-body">
        @if (loading()) {
          <div class="loading-state">
            <p>Loading appointment details...</p>
          </div>
        } @else if (error()) {
          <div class="alert alert--error">
            <p>{{ error() }}</p>
            <button class="btn btn--secondary btn--sm" (click)="loadAppointment()">Retry</button>
          </div>
        } @else {
          <div class="detail-card">
            <div class="detail-section">
              <h2>Appointment Information</h2>
              <div class="detail-row">
                <span class="label">Appointment ID:</span>
                <span class="value">{{ appointmentId() }}</span>
              </div>
              <div class="detail-row">
                <span class="label">Status:</span>
                <span class="value">{{ appointmentDetails()?.status || 'Booked' }}</span>
              </div>
              <div class="detail-row">
                <span class="label">Specialty:</span>
                <span class="value">{{ appointmentDetails()?.specialty || 'General Medicine' }}</span>
              </div>
              <div class="detail-row">
                <span class="label">Date:</span>
                <span class="value">{{ appointmentDetails()?.date || 'May 8, 2026' }}</span>
              </div>
              <div class="detail-row">
                <span class="label">Time:</span>
                <span class="value">{{ appointmentDetails()?.time || '09:00 AM' }}</span>
              </div>
              <div class="detail-row">
                <span class="label">Location:</span>
                <span class="value">{{ appointmentDetails()?.location || 'Virtual or In-person' }}</span>
              </div>
            </div>

            <div class="detail-section">
              <h2>Actions</h2>
              <div class="action-buttons">
                <button class="btn btn--outline" (click)="completeIntake()">Complete Intake Form</button>
                <button class="btn btn--outline" (click)="viewDocuments()">View Documents</button>
                <button class="btn btn--outline" routerLink="/dashboard">Back to Dashboard</button>
              </div>
            </div>
          </div>
        }
      </div>
    </div>
  `,
  styles: [`
    .appointment-detail-page {
      display: flex;
      flex-direction: column;
      height: 100%;
      overflow: hidden;
    }

    .page-header {
      background: white;
      padding: 1.5rem 2rem;
      border-bottom: 1px solid #e0e0e0;
      display: flex;
      justify-content: space-between;
      align-items: center;
      flex-shrink: 0;
    }

    .back-link {
      display: block;
      color: #0d7c8f;
      text-decoration: none;
      font-size: 0.9rem;
      margin-bottom: 0.5rem;
      
      &:hover {
        text-decoration: underline;
      }
    }

    .page-title {
      font-size: 1.75rem;
      font-weight: 600;
      margin: 0;
      color: #1a1a1a;
    }

    .page-actions {
      display: flex;
      gap: 0.75rem;
    }

    .page-body {
      flex: 1;
      overflow-y: auto;
      padding: 2rem;
      background-color: #f5f5f5;
    }

    .detail-card {
      max-width: 800px;
      margin: 0 auto;
      background: white;
      border-radius: 8px;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
      overflow: hidden;
    }

    .detail-section {
      padding: 2rem;
      border-bottom: 1px solid #e0e0e0;

      &:last-child {
        border-bottom: none;
      }

      h2 {
        font-size: 1.25rem;
        font-weight: 600;
        margin: 0 0 1.5rem 0;
        color: #333;
      }
    }

    .detail-row {
      display: flex;
      margin-bottom: 1rem;
      
      .label {
        flex: 0 0 150px;
        font-weight: 500;
        color: #666;
      }

      .value {
        flex: 1;
        color: #1a1a1a;
      }
    }

    .action-buttons {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }

    .btn {
      padding: 0.625rem 1.25rem;
      font-size: 0.95rem;
      font-weight: 500;
      border-radius: 4px;
      border: none;
      cursor: pointer;
      transition: all 0.2s;

      &--secondary {
        background: #e0e0e0;
        color: #333;

        &:hover {
          background: #d0d0d0;
        }
      }

      &--danger {
        background: #f44336;
        color: white;

        &:hover {
          background: #d32f2f;
        }
      }

      &--outline {
        background: white;
        border: 1px solid #0d7c8f;
        color: #0d7c8f;

        &:hover {
          background: #f0f9fa;
        }
      }

      &--sm {
        padding: 0.5rem 1rem;
        font-size: 0.875rem;
      }
    }

    .loading-state {
      text-align: center;
      padding: 3rem;
      color: #666;
    }

    .alert {
      padding: 1rem 1.5rem;
      margin-bottom: 1.5rem;
      border-radius: 4px;

      &--error {
        background: #ffebee;
        border: 1px solid #f44336;
        color: #721c24;
      }

      p {
        margin: 0 0 0.75rem 0;
      }
    }

    @media (max-width: 768px) {
      .page-header {
        flex-direction: column;
        align-items: flex-start;
        gap: 1rem;
      }

      .page-actions {
        width: 100%;
      }

      .detail-row {
        flex-direction: column;
        margin-bottom: 1.25rem;

        .label {
          margin-bottom: 0.25rem;
          font-size: 0.85rem;
        }
      }
    }
  `]
})
export class AppointmentDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly appointmentId = signal<string>('');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly appointmentDetails = signal<any>(null);

  ngOnInit(): void {
    this.appointmentId.set(this.route.snapshot.paramMap.get('id') || '');
    this.loadAppointment();
  }

  loadAppointment(): void {
    this.loading.set(true);
    this.error.set(null);

    // Simulate loading - in a real app, this would call a service
    setTimeout(() => {
      this.loading.set(false);
      // For now, just set some mock data
      this.appointmentDetails.set({
        status: 'Booked',
        specialty: 'Cardiology',
        date: 'May 8, 2026',
        time: '09:00 AM',
        location: 'Virtual or In-person'
      });
    }, 500);
  }

  reschedule(): void {
    this.router.navigate(['/appointments', this.appointmentId(), 'reschedule']);
  }

  cancel(): void {
    if (confirm('Are you sure you want to cancel this appointment?')) {
      // In a real app, this would call a service to cancel the appointment
      alert('Appointment cancellation functionality will be implemented.');
    }
  }

  completeIntake(): void {
    this.router.navigate(['/intake', this.appointmentId()]);
  }

  viewDocuments(): void {
    this.router.navigate(['/documents']);
  }
}
