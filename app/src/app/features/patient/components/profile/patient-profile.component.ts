import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import {
  PatientProfileDto,
  UpdatePatientProfileDto,
} from '../../models/patient-profile.models';
import { PatientService } from '../../services/patient.service';
import { PatientProfileEditFormComponent } from '../profile-edit/patient-profile-edit-form.component';

@Component({
  selector: 'app-patient-profile',
  standalone: true,
  imports: [
    MatButtonModule,
    MatCardModule,
    MatDividerModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    PatientProfileEditFormComponent,
  ],
  templateUrl: './patient-profile.component.html',
  styleUrl: './patient-profile.component.scss',
})
export class PatientProfileComponent implements OnInit {
  private readonly patientService = inject(PatientService);
  private readonly snackBar = inject(MatSnackBar);

  readonly isLoading = signal(true);
  readonly isEditMode = signal(false);
  readonly profile = signal<PatientProfileDto | null>(null);
  readonly eTag = signal('');

  ngOnInit(): void {
    this.loadProfile();
  }

  loadProfile(): void {
    this.isLoading.set(true);
    this.patientService.getProfile().subscribe({
      next: ({ profile, eTag }) => {
        this.profile.set(profile);
        this.eTag.set(eTag);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
        this.snackBar.open(
          'Unable to load profile. Please try again later.',
          'Dismiss',
          { duration: 5000 },
        );
        console.error(
          'PatientProfileComponent: failed to load patient profile',
        );
      },
    });
  }

  get editInitialValue(): UpdatePatientProfileDto {
    const p = this.profile();
    return {
      phone: p?.phone ?? '',
      address: p?.address ?? {
        street: '',
        city: '',
        state: '',
        postalCode: '',
        country: '',
      },
      emergencyContact: p?.emergencyContact ?? {
        name: '',
        phone: '',
        relationship: '',
      },
      communicationPreferences: p?.communicationPreferences ?? {
        emailOptIn: false,
        smsOptIn: false,
        preferredLanguage: 'en',
      },
    };
  }

  onEditClicked(): void {
    this.isEditMode.set(true);
  }

  onEditSaved(updated: PatientProfileDto): void {
    this.profile.set(updated);
    this.isEditMode.set(false);
    this.loadProfile(); // refresh to get new ETag
  }

  onEditCancelled(): void {
    this.isEditMode.set(false);
  }
}
