import {
  ChangeDetectionStrategy,
  Component,
  inject,
  signal,
} from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import {
  Subject,
  takeUntil,
  debounceTime,
  distinctUntilChanged,
  switchMap,
  catchError,
  of,
} from 'rxjs';
import { WalkInService } from '../services/walkin.service';
import { PatientSearchResultDto } from '../models/walkin.models';

/**
 * Patient search page (US_041, staff/patients).
 *
 * Allows staff to search for a patient by name or DOB, then navigate to
 * that patient's 360-degree view (/staff/patients/:patientId/360-view).
 *
 * Uses GET /api/staff/patients/search via WalkInService.
 * Minimum 2 characters before search fires (300 ms debounce).
 *
 * WCAG 2.2 AA:
 *  - Search results announced via aria-live="polite".
 *  - Each result row is a focusable button with aria-label.
 *  - Spinner has aria-label.
 */
@Component({
  selector: 'app-patient-list-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  template: `
    <main class="patient-list-page" aria-label="Patient search">
      <div class="page-header">
        <a
          routerLink="/staff/dashboard"
          mat-icon-button
          class="back-btn"
          aria-label="Back to dashboard"
        >
          <mat-icon>arrow_back</mat-icon>
        </a>
        <h1 class="page-title">360° Patient View</h1>
      </div>

      <p class="page-subtitle">
        Search for a patient to open their 360-degree clinical view.
      </p>

      <!-- Search field -->
      <mat-form-field appearance="outline" class="search-field">
        <mat-label>Search by name or date of birth</mat-label>
        <input
          matInput
          [formControl]="searchControl"
          type="search"
          autocomplete="off"
          placeholder="e.g. Jane Doe or 1990-05-15"
          aria-describedby="search-hint"
        />
        <mat-icon matSuffix aria-hidden="true">search</mat-icon>
        <mat-hint id="search-hint"
          >Enter at least 2 characters to search</mat-hint
        >
      </mat-form-field>

      <!-- Searching spinner -->
      @if (searching()) {
        <div class="status-row" role="status" aria-live="polite">
          <mat-spinner diameter="24" aria-label="Searching for patients…" />
          <span class="status-text">Searching…</span>
        </div>
      }

      <!-- Error -->
      @if (error()) {
        <div class="error-row" role="alert">
          <mat-icon aria-hidden="true">error_outline</mat-icon>
          {{ error() }}
        </div>
      }

      <!-- Results -->
      @if (!searching() && hasSearched() && results().length > 0) {
        <ul
          class="results-list"
          aria-label="Patient search results"
          aria-live="polite"
          role="list"
        >
          @for (patient of results(); track patient.patientId) {
            <li class="result-card" role="listitem">
              <button
                type="button"
                class="result-btn"
                [attr.aria-label]="'Open 360° view for ' + patient.name"
                (click)="navigate360(patient.patientId)"
              >
                <span class="result-avatar" aria-hidden="true">
                  {{ initials(patient.name) }}
                </span>
                <span class="result-info">
                  <span class="result-name">{{ patient.name }}</span>
                  <span class="result-meta">
                    @if (patient.dateOfBirth) {
                      DOB: {{ patient.dateOfBirth }} &bull;
                    }
                    {{ patient.email }}
                  </span>
                </span>
                <mat-icon class="result-chevron" aria-hidden="true"
                  >chevron_right</mat-icon
                >
              </button>
            </li>
          }
        </ul>
      }

      <!-- No results -->
      @if (
        !searching() && hasSearched() && results().length === 0 && !error()
      ) {
        <div class="empty-state" role="status" aria-live="polite">
          <mat-icon class="empty-icon" aria-hidden="true"
            >person_search</mat-icon
          >
          <p class="empty-title">No patients found</p>
          <p class="empty-body">Try a different name or date of birth.</p>
        </div>
      }

      <!-- Idle state -->
      @if (!hasSearched() && !searching()) {
        <div class="idle-state" aria-hidden="true">
          <mat-icon class="idle-icon">manage_search</mat-icon>
          <p>Start typing to search for a patient</p>
        </div>
      }
    </main>
  `,
  styles: [
    `
      .patient-list-page {
        max-width: 720px;
        margin: 0 auto;
        padding: 24px 16px;
      }

      .page-header {
        display: flex;
        align-items: center;
        gap: 8px;
        margin-bottom: 4px;
      }

      .back-btn {
        flex-shrink: 0;
      }

      .page-title {
        font-size: 1.5rem;
        font-weight: 700;
        color: #212121;
        margin: 0;
      }

      .page-subtitle {
        font-size: 0.9rem;
        color: #616161;
        margin: 0 0 20px;
      }

      .search-field {
        width: 100%;
        margin-bottom: 16px;
      }

      .status-row {
        display: flex;
        align-items: center;
        gap: 10px;
        padding: 8px 0;
        color: #616161;
        font-size: 0.875rem;
      }

      .status-text {
        font-size: 0.875rem;
      }

      .error-row {
        display: flex;
        align-items: center;
        gap: 8px;
        padding: 12px 16px;
        background: #ffebee;
        border-left: 4px solid #c62828;
        border-radius: 4px;
        color: #b71c1c;
        font-size: 0.875rem;
      }

      .results-list {
        list-style: none;
        margin: 0;
        padding: 0;
        display: flex;
        flex-direction: column;
        gap: 8px;
      }

      .result-card {
        background: #fff;
        border: 1px solid #e0e0e0;
        border-radius: 8px;
        overflow: hidden;
        transition: box-shadow 0.15s;
      }

      .result-card:hover {
        box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
      }

      .result-btn {
        width: 100%;
        display: flex;
        align-items: center;
        gap: 14px;
        padding: 14px 16px;
        background: none;
        border: none;
        cursor: pointer;
        text-align: left;
      }

      .result-btn:focus-visible {
        outline: 2px solid #1976d2;
        outline-offset: -2px;
      }

      .result-avatar {
        width: 40px;
        height: 40px;
        border-radius: 50%;
        background: #1976d2;
        color: #fff;
        display: flex;
        align-items: center;
        justify-content: center;
        font-size: 0.875rem;
        font-weight: 700;
        flex-shrink: 0;
      }

      .result-info {
        flex: 1;
        display: flex;
        flex-direction: column;
        gap: 2px;
      }

      .result-name {
        font-weight: 600;
        font-size: 0.9375rem;
        color: #212121;
      }

      .result-meta {
        font-size: 0.8125rem;
        color: #757575;
      }

      .result-chevron {
        color: #bdbdbd;
        flex-shrink: 0;
      }

      .empty-state,
      .idle-state {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: 8px;
        padding: 48px 24px;
        text-align: center;
        color: #9e9e9e;
      }

      .empty-icon,
      .idle-icon {
        font-size: 48px;
        width: 48px;
        height: 48px;
      }

      .empty-title {
        font-weight: 600;
        font-size: 1rem;
        color: #616161;
        margin: 0;
      }

      .empty-body {
        font-size: 0.875rem;
        color: #9e9e9e;
        margin: 0;
      }
    `,
  ],
})
export class PatientListPageComponent {
  private readonly walkinService = inject(WalkInService);
  private readonly router = inject(Router);
  private readonly destroy$ = new Subject<void>();

  readonly searchControl = new FormControl('');
  readonly results = signal<PatientSearchResultDto[]>([]);
  readonly searching = signal(false);
  readonly hasSearched = signal(false);
  readonly error = signal<string | null>(null);

  constructor() {
    this.searchControl.valueChanges
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        switchMap((value) => {
          const query = (value ?? '').trim();
          if (query.length < 2) {
            this.hasSearched.set(false);
            this.searching.set(false);
            this.results.set([]);
            this.error.set(null);
            return of(null);
          }
          this.searching.set(true);
          this.hasSearched.set(true);
          this.error.set(null);
          return this.walkinService.searchPatients(query).pipe(
            catchError(() => {
              this.error.set('Search failed. Please try again.');
              return of(null);
            }),
          );
        }),
        takeUntil(this.destroy$),
      )
      .subscribe((res) => {
        this.searching.set(false);
        if (res !== null) {
          this.results.set(res ?? []);
        }
      });
  }

  navigate360(patientId: string): void {
    this.router.navigate(['/staff/patients', patientId, '360-view']);
  }

  initials(name: string): string {
    return name
      .split(' ')
      .slice(0, 2)
      .map((n) => n[0]?.toUpperCase() ?? '')
      .join('');
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
