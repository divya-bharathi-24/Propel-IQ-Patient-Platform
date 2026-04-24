import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';

// ── DTOs ──────────────────────────────────────────────────────────────────────

export type SectionType =
  | 'Vitals'
  | 'Medications'
  | 'Diagnoses'
  | 'Allergies'
  | 'Immunizations'
  | 'SurgicalHistory';

export type VerificationStatus = 'Unverified' | 'Verified';
export type DocumentProcessingStatus = 'Completed' | 'Failed';

export interface SourceCitationDto {
  documentName: string;
  pageNumber?: number;
  uploadedAt: string;
  textSnippet?: string;
}

export interface ClinicalItemDto {
  fieldName: string;
  value: string;
  /** AI extraction confidence score. Range 0–1. */
  confidence: number;
  /** True when confidence is below 0.80 (AIR-003). */
  isLowConfidence: boolean;
  sources: SourceCitationDto[];
}

export interface ClinicalSectionDto {
  sectionType: SectionType;
  items: ClinicalItemDto[];
}

export interface ConflictSummaryDto {
  fieldName: string;
  reason: string;
}

export type ConflictSeverity = 'Critical' | 'Warning';
export type ConflictResolutionStatus = 'Unresolved' | 'Resolved';

export interface DataConflictDto {
  conflictId: string;
  fieldName: string;
  severity: ConflictSeverity;
  resolutionStatus: ConflictResolutionStatus;
  value1: string;
  sourceDoc1: string;
  value2: string;
  sourceDoc2: string;
  resolvedValue?: string;
}

export interface ResolveConflictPayload {
  resolvedValue: string;
}

export interface DocumentStatusDto {
  documentId: string;
  documentName: string;
  status: DocumentProcessingStatus;
}

export interface Patient360ViewDto {
  patientId: string;
  verificationStatus: VerificationStatus;
  verifiedAt?: string;
  verifiedByStaffName?: string;
  unresolvedCriticalConflicts: ConflictSummaryDto[];
  /** Full conflict objects for card rendering and resolution (US_044, AC-2/AC-3). */
  conflicts: DataConflictDto[];
  documents: DocumentStatusDto[];
  sections: ClinicalSectionDto[];
}

export interface VerifyProfileResponseDto {
  verificationStatus: VerificationStatus;
  verifiedAt: string;
  verifiedByStaffName: string;
}

export interface Patient360ViewServiceError {
  status: number;
  message: string;
}

// ── Service ───────────────────────────────────────────────────────────────────

/**
 * HttpClient wrapper for the 360-degree patient view API (US_041, task_001).
 *
 * Endpoints (task_002 backend):
 *  - GET  /api/staff/patients/{patientId}/360-view
 *  - POST /api/staff/patients/{patientId}/360-view/verify
 *  - POST /api/staff/patients/{patientId}/360-view/retry/{documentId}
 *
 * All requests carry a Bearer JWT added by AuthInterceptor.
 * OWASP A01: access control is enforced server-side; staffGuard adds client-side defence.
 */
@Injectable({ providedIn: 'root' })
export class Patient360ViewService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/staff/patients';

  /**
   * Fetches the aggregated 360-degree view for a patient.
   * GET /api/staff/patients/{patientId}/360-view
   */
  get360View(patientId: string): Observable<Patient360ViewDto> {
    return this.http
      .get<Patient360ViewDto>(`${this.base}/${patientId}/360-view`)
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  /**
   * Submits the staff verification for a patient's aggregated 360 profile.
   * POST /api/staff/patients/{patientId}/360-view/verify
   */
  verifyProfile(patientId: string): Observable<VerifyProfileResponseDto> {
    return this.http
      .post<VerifyProfileResponseDto>(
        `${this.base}/${patientId}/360-view/verify`,
        {},
      )
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  /**
   * Triggers re-processing of a document that previously failed extraction.
   * POST /api/staff/patients/{patientId}/360-view/retry/{documentId}
   */
  retryDocument(patientId: string, documentId: string): Observable<void> {
    return this.http
      .post<void>(`${this.base}/${patientId}/360-view/retry/${documentId}`, {})
      .pipe(
        catchError((err: HttpErrorResponse) =>
          throwError(() => this.mapError(err)),
        ),
      );
  }

  private mapError(err: HttpErrorResponse): Patient360ViewServiceError {
    return {
      status: err.status,
      message: err.error?.message ?? 'An unexpected error occurred.',
    };
  }
}
