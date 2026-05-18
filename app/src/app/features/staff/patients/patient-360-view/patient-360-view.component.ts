import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { DatePipe } from '@angular/common';
import { Patient360ViewStore } from './patient-360-view.store';
import { ClinicalConflictStore } from './store/clinical-conflict.store';
import { ClinicalSectionComponent } from './clinical-section/clinical-section.component';
import {
  ClinicalSectionDto,
  DocumentStatusDto,
  IntakeConditionItem,
  ManualMedicalHistorySnapshot,
  SectionType,
} from '../../../../core/services/patient-360-view.service';

const SECTION_ORDER: SectionType[] = [
  'Vitals',
  'Medications',
  'Diagnoses',
  'Allergies',
  'Immunizations',
  'SurgicalHistory',
];

type Tab = 'clinical' | 'appointments' | 'documents' | 'intake' | 'billing';

@Component({
  selector: 'app-patient-360-view',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatIconModule,
    ClinicalSectionComponent,
  ],
  templateUrl: './patient-360-view.component.html',
  styleUrl: './patient-360-view.component.scss',
})
export class Patient360ViewComponent implements OnInit {
  protected readonly store = inject(Patient360ViewStore);
  protected readonly conflictStore = inject(ClinicalConflictStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private patientId = '';
  readonly activeTab = signal<Tab>('clinical');

  ngOnInit(): void {
    this.patientId = this.route.snapshot.paramMap.get('patientId') ?? '';
    if (this.patientId) {
      this.store.load360View(this.patientId);
    }
  }

  protected initials(name?: string): string {
    if (!name) return '?';
    return name.split(' ').map((w: string) => w[0]).join('').toUpperCase().slice(0, 2);
  }

  protected sectionLabel(type: SectionType): string {
    const labels: Record<SectionType, string> = {
      Vitals: 'Vitals',
      Medications: 'Current Medications',
      Diagnoses: 'Medical History',
      Allergies: 'Allergies',
      Immunizations: 'Immunizations',
      SurgicalHistory: 'Surgical History',
    };
    return labels[type] ?? type;
  }

  protected leftSections(sections: ClinicalSectionDto[] | undefined | null): ClinicalSectionDto[] {
    return this.orderedSections(sections).filter((_, i) => i % 2 === 0);
  }

  protected rightSections(sections: ClinicalSectionDto[] | undefined | null): ClinicalSectionDto[] {
    return this.orderedSections(sections).filter((_, i) => i % 2 !== 0);
  }

  protected orderedSections(sections: ClinicalSectionDto[] | undefined | null): ClinicalSectionDto[] {
    const sectionMap = new Map((sections ?? []).map(s => [s.sectionType, s]));
    return SECTION_ORDER.map(type => sectionMap.get(type)).filter((s): s is ClinicalSectionDto => s !== undefined);
  }

  protected failedDocuments(docs: DocumentStatusDto[] | undefined | null): DocumentStatusDto[] {
    return (docs ?? []).filter(d => d.status === 'Failed');
  }

  protected onVerifyProfile(): void {
    this.store.verifyProfile(this.patientId);
  }

  protected onSelectConflictValue(conflictId: string, resolvedValue: string): void {
    this.conflictStore.resolveConflict({ conflictId, payload: { resolvedValue } });
  }

  protected onResolveConflicts(): void {
    this.router.navigate(['/staff/conflict-resolution', this.patientId]);
  }

  protected onRetryDocument(documentId: string): void {
    this.store.retryDocument({ patientId: this.patientId, documentId });
  }

  /**
   * Normalises the medicalHistory field from either AI intake (array) or
   * manual intake (object with .conditions array) into a flat list of conditions.
   */
  protected intakeConditions(
    medHistory: IntakeConditionItem[] | ManualMedicalHistorySnapshot | null | undefined,
  ): IntakeConditionItem[] {
    if (!medHistory) return [];
    if (Array.isArray(medHistory)) return medHistory;
    return (medHistory as ManualMedicalHistorySnapshot).conditions ?? [];
  }

  /**
   * Extracts allergies from a manual-intake medical history object.
   * Returns an empty array for AI-intake (array) format.
   */
  protected intakeAllergies(
    medHistory: IntakeConditionItem[] | ManualMedicalHistorySnapshot | null | undefined,
  ): { substance: string; reaction?: string }[] {
    if (!medHistory || Array.isArray(medHistory)) return [];
    return (medHistory as ManualMedicalHistorySnapshot).allergies ?? [];
  }
}
