import {
  ChangeDetectionStrategy,
  Component,
  Input,
  OnChanges,
  SimpleChanges,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ExtractedField } from '../intake-chat.store';

/** Confidence threshold below which a field is flagged for clarification. */
const LOW_CONFIDENCE_THRESHOLD = 0.8;

/** Four canonical sections displayed in the preview panel. */
const SECTION_FIELD_MAP: Record<string, string[]> = {
  Demographics: [
    'firstName',
    'lastName',
    'dateOfBirth',
    'biologicalSex',
    'phone',
    'street',
    'city',
    'state',
    'postalCode',
    'country',
  ],
  'Medical History': ['conditions', 'diagnoses', 'medicalHistory'],
  Symptoms: ['symptoms', 'symptomName', 'severity', 'onsetDate'],
  Medications: ['medications', 'medicationName', 'dosage', 'frequency'],
};

@Component({
  selector: 'app-intake-preview-panel',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatIconModule,
    MatTooltipModule,
    MatFormFieldModule,
    MatInputModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './intake-preview-panel.component.html',
  styleUrl: './intake-preview-panel.component.scss',
})
export class IntakePreviewPanelComponent implements OnChanges {
  /** Fields extracted by the AI, passed in by the parent component. */
  @Input() extractedFields: ExtractedField[] = [];

  /** When true, each field value becomes an editable input. */
  @Input() editMode = false;

  /** Emits the edited field values when in edit mode. */
  readonly editedValues = signal<Record<string, string | undefined>>({});

  readonly sections = Object.keys(SECTION_FIELD_MAP);

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['extractedFields']) {
      // Initialise editable values from extracted fields
      const values: Record<string, string> = {};
      for (const field of this.extractedFields) {
        values[field.fieldName] = field.value;
      }
      this.editedValues.set(values);
    }
  }

  isLowConfidence(field: ExtractedField): boolean {
    return field.confidence < LOW_CONFIDENCE_THRESHOLD;
  }

  confidencePercent(field: ExtractedField): number {
    return Math.round(field.confidence * 100);
  }

  getFieldsForSection(section: string): ExtractedField[] {
    const sectionKeys = SECTION_FIELD_MAP[section] ?? [];
    const sectionFields = this.extractedFields.filter((f) =>
      sectionKeys.some((key) =>
        f.fieldName.toLowerCase().includes(key.toLowerCase()),
      ),
    );
    return sectionFields;
  }

  getUnclassifiedFields(): ExtractedField[] {
    const allSectionKeys = Object.values(SECTION_FIELD_MAP).flat();
    return this.extractedFields.filter(
      (f) =>
        !allSectionKeys.some((key) =>
          f.fieldName.toLowerCase().includes(key.toLowerCase()),
        ),
    );
  }

  updateFieldValue(fieldName: string, value: string): void {
    this.editedValues.set({ ...this.editedValues(), [fieldName]: value });
  }

  getConfirmedFields(): Record<string, string | undefined> {
    return this.editedValues();
  }

  getEditedValue(fieldName: string, fallback: string): string {
    return this.editedValues()[fieldName] ?? fallback;
  }

  formatFieldLabel(fieldName: string): string {
    return fieldName
      .replace(/([A-Z])/g, ' $1')
      .replace(/^./, (s) => s.toUpperCase())
      .trim();
  }
}
