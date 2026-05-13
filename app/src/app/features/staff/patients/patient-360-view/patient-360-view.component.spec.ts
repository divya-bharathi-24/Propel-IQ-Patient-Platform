import { signal } from '@angular/core';
import {
  ComponentFixture,
  TestBed,
  fakeAsync,
  tick,
} from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import {
  ClinicalSectionDto,
  DataConflictDto,
  DocumentStatusDto,
  Patient360ViewDto,
} from '../../../../core/services/patient-360-view.service';
import { Patient360ViewComponent } from './patient-360-view.component';
import { Patient360ViewStore } from './patient-360-view.store';
import { ClinicalConflictStore } from './store/clinical-conflict.store';
import { NO_ERRORS_SCHEMA } from '@angular/core';

// ── Helpers ───────────────────────────────────────────────────────────────────

function makeView(
  overrides: Partial<Patient360ViewDto> = {},
): Patient360ViewDto {
  return {
    patientId: 'patient-1',
    verificationStatus: 'Unverified',
    unresolvedCriticalConflicts: [],
    conflicts: [],
    documents: [],
    sections: [],
    ...overrides,
  };
}

function makeSection(
  type: ClinicalSectionDto['sectionType'],
): ClinicalSectionDto {
  return { sectionType: type, items: [] };
}

function makeDoc(
  id: string,
  status: DocumentStatusDto['status'],
): DocumentStatusDto {
  return { documentId: id, documentName: `Doc ${id}`, status };
}

// ── Mock factories ────────────────────────────────────────────────────────────

function buildMockViewStore() {
  return {
    loadingState: signal<'idle' | 'loading' | 'loaded' | 'error'>('idle'),
    view360: signal<Patient360ViewDto | null>(null),
    verifyState: signal<'idle' | 'loading' | 'success' | 'error'>('idle'),
    verifyResult: signal<{
      verificationStatus: string;
      verifiedAt: string;
      verifiedByStaffName: string;
    } | null>(null),
    verifyError: signal<string | null>(null),
    loadError: signal<string | null>(null),
    canVerify: signal(true),
    load360View: jasmine.createSpy('load360View'),
    verifyProfile: jasmine.createSpy('verifyProfile'),
    retryDocument: jasmine.createSpy('retryDocument'),
  };
}

function buildMockConflictStore() {
  return {
    conflicts: signal<DataConflictDto[]>([]),
    unresolvedCriticalCount: signal(0),
    unresolvedConflicts: signal<DataConflictDto[]>([]),
    loadConflicts: jasmine.createSpy('loadConflicts'),
    resolveConflict: jasmine.createSpy('resolveConflict'),
  };
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('Patient360ViewComponent', () => {
  let fixture: ComponentFixture<Patient360ViewComponent>;
  let component: Patient360ViewComponent;
  let mockViewStore: ReturnType<typeof buildMockViewStore>;
  let mockConflictStore: ReturnType<typeof buildMockConflictStore>;

  beforeEach(async () => {
    mockViewStore = buildMockViewStore();
    mockConflictStore = buildMockConflictStore();

    await TestBed.configureTestingModule({
      imports: [Patient360ViewComponent, NoopAnimationsModule],
      providers: [
        { provide: Patient360ViewStore, useValue: mockViewStore },
        { provide: ClinicalConflictStore, useValue: mockConflictStore },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap({ patientId: 'patient-1' }),
            },
          },
        },
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    fixture = TestBed.createComponent(Patient360ViewComponent);
    component = fixture.componentInstance;
  });

  // ── Unit: orderedSections ────────────────────────────────────────────────────

  describe('orderedSections()', () => {
    it('returns empty array when sections is undefined (regression: TypeError on .map)', () => {
      const result = (component as any).orderedSections(undefined);
      expect(result).toEqual([]);
    });

    it('returns empty array when sections is null', () => {
      const result = (component as any).orderedSections(null);
      expect(result).toEqual([]);
    });

    it('returns empty array when sections is empty', () => {
      expect((component as any).orderedSections([])).toEqual([]);
    });

    it('orders sections according to SECTION_ORDER', () => {
      const input: ClinicalSectionDto[] = [
        makeSection('SurgicalHistory'),
        makeSection('Vitals'),
        makeSection('Allergies'),
      ];
      const result: ClinicalSectionDto[] = (component as any).orderedSections(
        input,
      );
      expect(result.map((s: ClinicalSectionDto) => s.sectionType)).toEqual([
        'Vitals',
        'Allergies',
        'SurgicalHistory',
      ]);
    });

    it('omits sections that are not in the predefined SECTION_ORDER', () => {
      const input: ClinicalSectionDto[] = [makeSection('Vitals')];
      const result = (component as any).orderedSections(input);
      expect(result.length).toBe(1);
      expect(result[0].sectionType).toBe('Vitals');
    });

    it('returns all six sections in correct order when all are present', () => {
      const input: ClinicalSectionDto[] = [
        makeSection('SurgicalHistory'),
        makeSection('Immunizations'),
        makeSection('Allergies'),
        makeSection('Diagnoses'),
        makeSection('Medications'),
        makeSection('Vitals'),
      ];
      const result = (component as any).orderedSections(input);
      expect(result.map((s: ClinicalSectionDto) => s.sectionType)).toEqual([
        'Vitals',
        'Medications',
        'Diagnoses',
        'Allergies',
        'Immunizations',
        'SurgicalHistory',
      ]);
    });
  });

  // ── Unit: failedDocuments ────────────────────────────────────────────────────

  describe('failedDocuments()', () => {
    it('returns empty array when docs is undefined (regression: TypeError on .filter)', () => {
      expect((component as any).failedDocuments(undefined)).toEqual([]);
    });

    it('returns empty array when docs is null', () => {
      expect((component as any).failedDocuments(null)).toEqual([]);
    });

    it('returns empty array when all docs are Completed', () => {
      const docs = [makeDoc('d1', 'Completed'), makeDoc('d2', 'Completed')];
      expect((component as any).failedDocuments(docs)).toEqual([]);
    });

    it('returns only failed documents', () => {
      const docs = [
        makeDoc('d1', 'Completed'),
        makeDoc('d2', 'Failed'),
        makeDoc('d3', 'Failed'),
      ];
      const result = (component as any).failedDocuments(docs);
      expect(result.length).toBe(2);
      expect(result.map((d: DocumentStatusDto) => d.documentId)).toEqual([
        'd2',
        'd3',
      ]);
    });
  });

  // ── ngOnInit ─────────────────────────────────────────────────────────────────

  describe('ngOnInit()', () => {
    it('calls store.load360View with the patientId from the route', () => {
      fixture.detectChanges(); // triggers ngOnInit
      expect(mockViewStore.load360View).toHaveBeenCalledOnceWith('patient-1');
    });
  });

  // ── Template: loading state ──────────────────────────────────────────────────

  describe('template: loading state', () => {
    it('shows the progress bar when loadingState is "loading"', () => {
      mockViewStore.loadingState.set('loading');
      fixture.detectChanges();
      const bar = fixture.debugElement.query(By.css('mat-progress-bar'));
      expect(bar).toBeTruthy();
    });

    it('hides the progress bar when loadingState is "idle"', () => {
      mockViewStore.loadingState.set('idle');
      fixture.detectChanges();
      const bar = fixture.debugElement.query(By.css('mat-progress-bar'));
      expect(bar).toBeNull();
    });
  });

  // ── Template: error state ────────────────────────────────────────────────────

  describe('template: error state', () => {
    it('shows error card when loadingState is "error"', () => {
      mockViewStore.loadingState.set('error');
      mockViewStore.loadError.set('Something went wrong');
      fixture.detectChanges();
      const el: HTMLElement = fixture.nativeElement;
      expect(el.querySelector('.error-card')).toBeTruthy();
    });
  });

  // ── Template: loaded state ───────────────────────────────────────────────────

  describe('template: loaded state', () => {
    beforeEach(() => {
      mockViewStore.loadingState.set('loaded');
      mockViewStore.view360.set(makeView());
      fixture.detectChanges();
    });

    it('renders the profile status badge', () => {
      const badge = fixture.nativeElement.querySelector(
        '[data-testid="profile-status-badge"]',
      ) as HTMLElement;
      expect(badge).toBeTruthy();
      expect(badge.textContent?.trim()).toBe('Unverified');
    });

    it('does not show the >10 documents banner when documents are <= 10', () => {
      expect(fixture.nativeElement.querySelector('.banner-info')).toBeNull();
    });

    it('shows the >10 documents banner when there are more than 10 documents', () => {
      const docs = Array.from({ length: 11 }, (_, i) =>
        makeDoc(`d${i}`, 'Completed'),
      );
      mockViewStore.view360.set(makeView({ documents: docs }));
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('.banner-info')).toBeTruthy();
    });

    it('shows doc-failures-card when there are failed documents', () => {
      mockViewStore.view360.set(
        makeView({ documents: [makeDoc('d1', 'Failed')] }),
      );
      fixture.detectChanges();
      expect(
        fixture.nativeElement.querySelector('.doc-failures-card'),
      ).toBeTruthy();
    });

    it('does not show doc-failures-card when all documents are completed', () => {
      mockViewStore.view360.set(
        makeView({ documents: [makeDoc('d1', 'Completed')] }),
      );
      fixture.detectChanges();
      expect(
        fixture.nativeElement.querySelector('.doc-failures-card'),
      ).toBeNull();
    });

    it('renders the Verify Profile button when verifyState is not "success"', () => {
      mockViewStore.verifyState.set('idle');
      fixture.detectChanges();
      const btn = fixture.debugElement.query(
        By.css('.verify-action-row button'),
      );
      expect(btn).toBeTruthy();
      expect(btn.nativeElement.textContent.trim()).toContain('Verify Profile');
    });

    it('hides the Verify Profile button when verifyState is "success"', () => {
      mockViewStore.verifyState.set('success');
      fixture.detectChanges();
      expect(
        fixture.nativeElement.querySelector('.verify-action-row'),
      ).toBeNull();
    });
  });

  // ── Template: sections rendered without crash ────────────────────────────────

  describe('template: sections with missing fields', () => {
    it('renders without crashing when view has no sections (undefined)', () => {
      const viewWithoutSections = {
        ...makeView(),
        sections: undefined as unknown as ClinicalSectionDto[],
      };
      mockViewStore.loadingState.set('loaded');
      mockViewStore.view360.set(viewWithoutSections);
      expect(() => fixture.detectChanges()).not.toThrow();
    });

    it('renders without crashing when view has no documents (undefined)', () => {
      const viewWithoutDocs = {
        ...makeView(),
        documents: undefined as unknown as DocumentStatusDto[],
      };
      mockViewStore.loadingState.set('loaded');
      mockViewStore.view360.set(viewWithoutDocs);
      expect(() => fixture.detectChanges()).not.toThrow();
    });
  });

  // ── Actions ──────────────────────────────────────────────────────────────────

  describe('onVerifyProfile()', () => {
    it('calls store.verifyProfile with the patientId', () => {
      (component as any).onVerifyProfile('patient-1');
      expect(mockViewStore.verifyProfile).toHaveBeenCalledOnceWith('patient-1');
    });
  });

  describe('onRetryDocument()', () => {
    it('calls store.retryDocument with patientId and documentId', () => {
      (component as any).onRetryDocument('patient-1', 'doc-42');
      expect(mockViewStore.retryDocument).toHaveBeenCalledOnceWith({
        patientId: 'patient-1',
        documentId: 'doc-42',
      });
    });
  });

  describe('onSelectConflictValue()', () => {
    it('calls conflictStore.resolveConflict with the correct payload', () => {
      (component as any).onSelectConflictValue('conflict-1', '120/80');
      expect(mockConflictStore.resolveConflict).toHaveBeenCalledOnceWith({
        conflictId: 'conflict-1',
        payload: { resolvedValue: '120/80' },
      });
    });
  });
});
