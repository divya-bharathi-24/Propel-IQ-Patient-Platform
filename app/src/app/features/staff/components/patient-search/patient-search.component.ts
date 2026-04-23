import {
  Component,
  OnDestroy,
  OnInit,
  inject,
  output,
  signal,
} from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Subject, takeUntil } from 'rxjs';
import { PatientSearchResultDto } from '../../models/walkin.models';
import { WalkInStore } from '../../state/walkin.store';

@Component({
  selector: 'app-patient-search',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatListModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './patient-search.component.html',
})
export class PatientSearchComponent implements OnInit, OnDestroy {
  protected readonly store = inject(WalkInStore);

  /** Emits the patient selected from search results. */
  readonly patientSelected = output<PatientSearchResultDto>();
  /** Emits when staff chooses to create a new patient record. */
  readonly createNewRequested = output<void>();
  /** Emits when staff chooses to continue as anonymous. */
  readonly anonymousRequested = output<void>();

  readonly searchControl = new FormControl('');
  readonly hasSearched = signal(false);

  private readonly destroy$ = new Subject<void>();

  ngOnInit(): void {
    this.searchControl.valueChanges
      .pipe(takeUntil(this.destroy$))
      .subscribe((value) => {
        const trimmed = (value ?? '').trim();
        if (trimmed.length >= 2) {
          this.hasSearched.set(true);
          this.store.searchPatients(trimmed);
        } else {
          this.hasSearched.set(false);
        }
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  onPatientSelect(patient: PatientSearchResultDto): void {
    this.store.selectPatient(patient);
    this.patientSelected.emit(patient);
  }

  onCreateNew(): void {
    this.createNewRequested.emit();
  }

  onAnonymous(): void {
    this.anonymousRequested.emit();
  }
}
