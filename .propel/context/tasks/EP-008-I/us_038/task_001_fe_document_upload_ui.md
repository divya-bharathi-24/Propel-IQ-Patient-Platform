# Task - task_001_fe_document_upload_ui

## Requirement Reference

- **User Story:** us_038 — Patient Clinical Document Upload with Encrypted Storage
- **Story Location:** `.propel/context/tasks/EP-008-I/us_038/us_038.md`
- **Acceptance Criteria:**
  - AC-1: Client-side validation of PDF type and ≤25 MB size before upload begins; validates all files before the first byte is sent
  - AC-3: Upload history section on dashboard shows each document's fileName, uploadDate, fileSize, processingStatus (Pending / Processing / Completed / Failed)
  - AC-4: Per-file error messages ("File too large" / "Only PDF files are accepted") — valid files in the same batch continue uploading
- **Edge Cases:**
  - Partial batch failure: already-uploaded files remain stored; failed files are shown individually in the result summary with a "Retry" action that re-queues only those files (no re-upload of successful ones)
  - Storage unavailable (503): user shown "Please try again shortly" banner; no partial state persisted in UI

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value                                                                                                                                |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| **UI Impact**          | Yes                                                                                                                                  |
| **Figma URL**          | N/A                                                                                                                                  |
| **Wireframe Status**   | PENDING                                                                                                                              |
| **Wireframe Type**     | N/A                                                                                                                                  |
| **Wireframe Path/URL** | TODO: Provide wireframe — upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-DOC-upload.[html\|png\|jpg]` or add external URL |
| **Screen Spec**        | N/A (figma_spec.md not yet generated)                                                                                                |
| **UXR Requirements**   | N/A (figma_spec.md not yet generated)                                                                                                |
| **Design Tokens**      | N/A (designsystem.md not yet generated)                                                                                              |

---

## Applicable Technology Stack

| Layer          | Technology              | Version |
| -------------- | ----------------------- | ------- |
| Frontend       | Angular                 | 18.x    |
| Frontend State | NgRx Signals            | 18.x    |
| UI Components  | Angular Material        | 18.x    |
| HTTP Client    | Angular HttpClient      | 18.x    |
| Styling        | Angular Material + SCSS | 18.x    |
| Testing        | Playwright              | 1.x     |
| AI/ML          | N/A                     | N/A     |
| Mobile         | N/A                     | N/A     |

> All code and libraries MUST be compatible with versions above.

---

## AI References (AI Tasks Only)

| Reference Type           | Value |
| ------------------------ | ----- |
| **AI Impact**            | No    |
| **AIR Requirements**     | N/A   |
| **AI Pattern**           | N/A   |
| **Prompt Template Path** | N/A   |
| **Guardrails Config**    | N/A   |
| **Model Provider**       | N/A   |

---

## Mobile References (Mobile Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **Mobile Impact**    | No    |
| **Platform Target**  | N/A   |
| **Min OS Version**   | N/A   |
| **Mobile Framework** | N/A   |

---

## Task Overview

Build the patient-facing document upload UI within the Patient Dashboard. The feature includes a drag-and-drop / file-picker upload zone with full client-side validation, batch upload orchestration, a real-time upload history table, per-file status tracking, and graceful handling of storage-unavailable errors and partial batch failures.

**Components to create:**

- `DocumentUploadComponent` — host for the upload zone and upload history (lazy-loaded under `PatientModule`, route: `/patient/documents`)
- `DocumentUploadStore` (NgRx Signals) — reactive state for files, validation errors, upload progress, history list, and flags
- `DocumentUploadService` — HTTP client wrapper for `POST /api/documents/upload` with per-file progress
- Integrated `Upload History` sub-section inside the component using `mat-table`

---

## Dependent Tasks

- **task_002_be_document_upload_api.md** — `POST /api/documents/upload` endpoint must exist for HTTP integration
- **task_003_db_clinical_document_schema.md** — `ClinicalDocument` entity and DB migration must be in place
- **US_016 (Foundational)** — Patient dashboard route and `PatientModule` lazy-loading scaffold already exists; this component is added to that feature module

---

## Impacted Components

| Status | Component / Module                   | Project                                                             |
| ------ | ------------------------------------ | ------------------------------------------------------------------- |
| CREATE | `DocumentUploadComponent`            | `Client/src/app/patient/document-upload/`                           |
| CREATE | `DocumentUploadStore` (NgRx Signals) | `Client/src/app/patient/document-upload/document-upload.store.ts`   |
| CREATE | `DocumentUploadService`              | `Client/src/app/patient/document-upload/document-upload.service.ts` |
| MODIFY | `PatientModule` / patient routes     | Add `/patient/documents` lazy route                                 |

---

## Implementation Plan

1. **`DocumentUploadStore`** (NgRx Signals):

   ```typescript
   interface DocumentUploadState {
     selectedFiles: File[];
     validationErrors: Record<string, string>; // filename -> error message
     uploadProgress: number; // 0–100 overall batch progress
     isUploading: boolean;
     uploadHistory: ClinicalDocumentDto[]; // loaded from GET /api/documents
     uploadResult: UploadFileResult[] | null; // per-file result after batch
     storageUnavailable: boolean; // 503 response received
   }
   ```

   Signals: `files`, `validationErrors`, `uploadProgress`, `isUploading`, `uploadHistory`, `uploadResult`, `storageUnavailable`.
   Methods: `validateFiles(files: FileList)`, `resetUpload()`, `loadHistory()`, `handleUploadResult(results)`, `retryFiles(failedFileNames: string[])`.

2. **`DocumentUploadComponent`**:
   - **Drop zone**: `<div (dragover)="onDragOver($event)" (drop)="onDrop($event)" (click)="fileInput.click()" role="button" aria-label="Upload PDF files. Click or drag and drop.">`
   - `<input #fileInput type="file" accept=".pdf,application/pdf" multiple style="display:none">` — keyboard accessible via button click
   - On file selection: call `store.validateFiles(files)` before displaying the file list; never send unvalidated files
   - Validation rules (AC-1):
     - MIME: `file.type === 'application/pdf'` AND filename ends with `.pdf` (double check)
     - Size: `file.size <= 25 * 1024 * 1024` (25 MB)
     - Batch max: `files.length <= 20` — show global error "Maximum 20 files per upload" if exceeded
   - Show per-file validation error (AC-4): `<mat-error *ngIf="validationErrors()[file.name]">{{ validationErrors()[file.name] }}</mat-error>`
   - "Upload" button disabled while `isUploading()` is true or no valid files pending; shows `<mat-spinner>` during upload
   - **Storage unavailable banner**: `<mat-banner *ngIf="storageUnavailable()">Service temporarily unavailable. Please try again shortly.</mat-banner>`

3. **`DocumentUploadService`**:

   ```typescript
   uploadDocuments(validFiles: File[]): Observable<HttpEvent<UploadBatchResultDto>> {
     const formData = new FormData();
     validFiles.forEach(f => formData.append('files', f, f.name));
     return this.http.post<UploadBatchResultDto>('/api/documents/upload', formData, {
       reportProgress: true,
       observe: 'events'
     });
   }

   getUploadHistory(): Observable<ClinicalDocumentDto[]> {
     return this.http.get<ClinicalDocumentDto[]>('/api/documents');
   }
   ```

   - On `HttpEventType.UploadProgress`: compute `Math.round(100 * event.loaded / event.total)` → update `uploadProgress` signal
   - On `HttpEventType.Response` (200): parse `UploadBatchResultDto.files[]` → `store.handleUploadResult(results)`
   - On 503: set `store.storageUnavailable = true`; do NOT retain partial file list in `selectedFiles`

4. **Upload History `mat-table`** (AC-3):

   ```
   Columns: File Name | Upload Date | File Size | Status
   ```

   - `processingStatus` rendered as a `mat-chip`: Pending → grey (#9E9E9E), Processing → blue (#1565C0), Completed → green (#2E7D32), Failed → red (#C62828); all white text ≥4.5:1 contrast (WCAG 2.2 AA, NFR visual)
   - `aria-label` on each chip: `"Processing status: <status>"` (WCAG 2.2 AA — text alongside color)
   - Loading state: `<mat-progress-bar mode="indeterminate">` while `loadingHistory` is true
   - Empty state: "No documents uploaded yet." message
   - File size formatted: `{{ fileSize | fileSize }}` (KB/MB pipe)

5. **Partial batch Retry** (edge case):
   - After upload result, display per-file result summary in a `mat-list`; each failed file shows a "Retry" icon button
   - "Retry" action re-queues only the failed file(s): `store.retryFiles([fileName])` → calls `uploadDocuments([file])` using the cached `File` object from `selectedFiles`
   - Successfully uploaded files show a green check icon and cannot be re-uploaded in the same session

6. **Route integration**:
   ```typescript
   // patient-routing.module.ts
   { path: 'documents', loadComponent: () =>
     import('./document-upload/document-upload.component')
       .then(m => m.DocumentUploadComponent) }
   ```
   Guarded by `AuthGuard` (Patient role).

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no Client/ scaffold yet — greenfield Angular 18 project)
```

> Update with actual `Client/src/app/patient/` tree after Angular scaffold.

---

## Expected Changes

| Action | File Path                                                                | Description                                                                               |
| ------ | ------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------- |
| CREATE | `Client/src/app/patient/document-upload/document-upload.component.ts`    | Host component: drop zone, file picker, validation display, upload button, result summary |
| CREATE | `Client/src/app/patient/document-upload/document-upload.component.html`  | Template: mat-card layout, drop zone, file list, mat-table history, banners               |
| CREATE | `Client/src/app/patient/document-upload/document-upload.component.scss`  | Drop zone hover state, drag-over highlight, status chip colour classes                    |
| CREATE | `Client/src/app/patient/document-upload/document-upload.store.ts`        | NgRx Signals store: file state, validation errors, progress, history, result              |
| CREATE | `Client/src/app/patient/document-upload/document-upload.service.ts`      | HTTP client: POST upload with progress, GET history                                       |
| CREATE | `Client/src/app/patient/document-upload/models/clinical-document.dto.ts` | `ClinicalDocumentDto`, `UploadBatchResultDto`, `UploadFileResult` interfaces              |
| MODIFY | `Client/src/app/patient/patient-routing.module.ts`                       | Add `/patient/documents` lazy route with `AuthGuard`                                      |

---

## External References

- [Angular 18 — Reactive Forms & FormArray (for dynamic file list)](https://angular.dev/guide/forms/reactive-forms)
- [Angular Material 18 — mat-table with data source](https://material.angular.io/components/table/overview)
- [Angular Material 18 — mat-chip for status badges](https://material.angular.io/components/chips/overview)
- [Angular HttpClient — reportProgress & observe: 'events'](https://angular.dev/guide/http/making-requests#tracking-upload-progress)
- [NgRx Signals — signalStore pattern](https://ngrx.io/guide/signals/signal-store)
- [WCAG 2.2 AA — Use of color (Success Criterion 1.4.1)](https://www.w3.org/WAI/WCAG22/Understanding/use-of-color.html)
- [FR-041 — Patient document upload interface (spec.md line 183)](spec.md)
- [FR-042 — Batch limits: 20 files, 25 MB each (spec.md line 184)](spec.md)
- [UC-007 sequence diagram — client-side validate then POST (models.md)](models.md)

---

## Build Commands

```bash
# Generate component (Angular CLI)
ng generate component patient/document-upload --standalone --style=scss

# Run Angular dev server
ng serve

# Build for production
ng build --configuration production

# Run Playwright E2E tests
npx playwright test --grep "document upload"
```

---

## Implementation Validation Strategy

- [ ] Selecting a non-PDF file (e.g., `.docx`) shows "Only PDF files are accepted" per-file error; the file is NOT included in the upload payload
- [ ] Selecting a PDF >25 MB shows "File too large" per-file error; the file is NOT included in the upload payload
- [ ] Selecting >20 valid PDFs shows "Maximum 20 files per upload" global error; upload button remains disabled
- [ ] Upload button is disabled while `isUploading` is true; spinner visible during upload
- [ ] 503 response renders "Service temporarily unavailable. Please try again shortly." banner without partial history entries
- [ ] Partial batch (some files fail server validation): failed files show per-file error and "Retry" button; successful files appear in upload history with `processingStatus=Pending`
- [ ] Upload history `mat-table` shows `fileName`, `uploadedAt`, `fileSize`, `processingStatus` after successful upload
- [ ] `aria-label` present on drop zone, all status chips, and the upload button; tab navigation reaches file picker

---

## Implementation Checklist

- [x] Create `DocumentUploadComponent` with drag-and-drop drop zone (`role="button"`, `aria-label`), hidden `<input type="file" accept=".pdf,application/pdf" multiple>`, keyboard-accessible click handler (WCAG 2.2 AA)
- [x] Implement client-side validation: MIME type + `.pdf` extension check, per-file ≤25 MB, max 20 files batch; render per-file `mat-error` messages "File too large" / "Only PDF files are accepted" (AC-1, AC-4, FR-042)
- [x] Create `DocumentUploadStore` (NgRx Signals): signals for `selectedFiles`, `validationErrors`, `uploadProgress`, `isUploading`, `uploadHistory`, `uploadResult`, `storageUnavailable`
- [x] Create `DocumentUploadService`: `uploadDocuments(validFiles)` with `reportProgress: true` + `observe: 'events'` for progress tracking; `getUploadHistory()` for history panel
- [x] Build upload history `mat-table` (AC-3): columns `fileName`, `uploadedAt`, `fileSize`, `processingStatus`; status chips colour-coded (Pending/Processing/Completed/Failed) with `aria-label="Processing status: <value>"` (WCAG 2.2 AA text-alongside-colour)
- [x] Handle 503 storage-unavailable: show `mat-banner` "Please try again shortly"; reset `selectedFiles` to empty; do NOT show partial history entries
- [x] Handle partial batch failures: per-file `UploadFileResult` rendered in result summary; "Retry" button re-queues only failed `File` objects from cached `selectedFiles`
- [x] Add lazy route `/patient/documents` in `patient-routing.module.ts` guarded by `AuthGuard` (Patient role)
