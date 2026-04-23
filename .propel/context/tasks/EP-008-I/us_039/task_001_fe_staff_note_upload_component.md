# Task - TASK_001

## Requirement Reference

- **User Story**: US_039 — Staff Post-Visit Clinical Note Upload
- **Story Location**: `.propel/context/tasks/EP-008-I/us_039/us_039.md`
- **Acceptance Criteria**:
  - AC-1: Given I am authenticated as Staff and navigate to a patient's record, When I upload a PDF clinical note, Then the document is validated (PDF, ≤25 MB), encrypted at rest, and a ClinicalDocument record is created with the patient's ID and the encounter reference.
  - AC-2: Given the clinical note is uploaded, When I view the patient's document history, Then the note appears with a "Staff Upload" indicator, the staff member's name, the upload timestamp, and the encounter reference.
  - AC-4: Given a Patient-role user attempts to access the Staff upload endpoint, When the request is evaluated, Then HTTP 403 Forbidden is returned and no document is stored.
- **Edge Cases**:
  - Wrong patient uploaded: Staff can soft-delete the incorrect document from the patient record within 24 hours with a documented reason; deletion event logged in audit trail. Show delete button on Staff-uploaded documents within 24h of upload, with a deletion reason text area.
  - Encounter reference not found: UI renders a dismissible amber warning banner below the form — "Encounter reference not found — document linked to patient without appointment reference" — but does NOT block the upload.

## Design References (Frontend Tasks Only)

| Reference Type         | Value                                                                                                                           |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| **UI Impact**          | Yes                                                                                                                             |
| **Figma URL**          | N/A                                                                                                                             |
| **Wireframe Status**   | PENDING                                                                                                                         |
| **Wireframe Type**     | N/A                                                                                                                             |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-staff-note-upload.[html\|png\|jpg]` or provide external URL |
| **Screen Spec**        | N/A (figma_spec.md not yet generated)                                                                                           |
| **UXR Requirements**   | N/A (figma_spec.md not yet generated)                                                                                           |
| **Design Tokens**      | N/A (designsystem.md not yet generated)                                                                                         |

### **CRITICAL: Wireframe Implementation Requirement**

**Wireframe Status = PENDING:** When wireframe becomes available, implementation MUST:

- Match the staff note upload panel and document history list layout
- Implement all states: Default (empty form), Validating, Uploading (progress bar), Success (document listed), Error (per-field/server), Warning (encounter ref not found)
- Validate implementation against wireframe at breakpoints: 375px, 768px, 1440px
- Run `/analyze-ux` after implementation to verify pixel-perfect alignment

## Applicable Technology Stack

| Layer          | Technology           | Version |
| -------------- | -------------------- | ------- |
| Frontend       | Angular              | 18.x    |
| Frontend State | NgRx Signals         | 18.x    |
| Backend        | ASP.NET Core Web API | .net 10 |
| Database       | PostgreSQL           | 16+     |
| Library        | Angular Router       | 18.x    |
| Library        | Angular `HttpClient` | 18.x    |
| AI/ML          | N/A                  | N/A     |
| Vector Store   | N/A                  | N/A     |
| AI Gateway     | N/A                  | N/A     |
| Mobile         | N/A                  | N/A     |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type           | Value |
| ------------------------ | ----- |
| **AI Impact**            | No    |
| **AIR Requirements**     | N/A   |
| **AI Pattern**           | N/A   |
| **Prompt Template Path** | N/A   |
| **Guardrails Config**    | N/A   |
| **Model Provider**       | N/A   |

## Mobile References (Mobile Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **Mobile Impact**    | No    |
| **Platform Target**  | N/A   |
| **Min OS Version**   | N/A   |
| **Mobile Framework** | N/A   |

## Task Overview

Implement `StaffNoteUploadComponent` — an Angular 18 standalone, `ChangeDetectionStrategy.OnPush` component surfaced on the patient record page (`/staff/patients/:patientId`). The component allows authenticated Staff users to upload a single post-visit clinical note PDF with an optional encounter reference, view the patient's full document history, and soft-delete their own recently uploaded notes.

**Upload flow:**

1. Staff selects a single PDF file via `<input type="file" accept=".pdf">`.
2. Client-side validation: MIME type (`application/pdf`) + size ≤ 25 MB. Display per-field error if invalid.
3. Staff enters an optional encounter reference (appointment ID or reference string).
4. Click "Upload Note" → `StaffDocumentService.uploadNote(patientId, file, encounterRef)` → `POST /api/staff/documents/upload` (multipart/form-data).
5. Upload progress tracked via `HttpClient` `reportProgress: true, observe: 'events'` → `uploadProgress` signal (0–100).
6. On `UploadCompleteResponse.encounterWarning = true`: show amber warning banner (dismissible, `role="alert"`).
7. On success: reset form, reload document history.

**Document history (`DocumentHistoryListComponent`):**

- Displays `DocumentHistoryItemDto[]` loaded from `GET /api/staff/patients/{patientId}/documents`.
- Each row shows: file name, file size, `sourceType` badge ("Staff Upload" amber / "Patient Upload" blue), staff member name (if Staff Upload), encounter reference (or "—"), upload timestamp, processing status chip (Pending / Processing / Completed / Failed).
- **Soft-delete:** For `sourceType = 'StaffUpload'` documents uploaded within 24 hours of current time, render a "Delete" icon button. Clicking opens a confirmation dialog with a required `DeletionReason` text area (min 10 chars). On confirm → `DELETE /api/staff/documents/{id}` with `{ reason }` body. Optimistically removes from list; reverts on error.

**Route protection:** Both the upload form and document history are rendered only within pages accessed via `staffGuard` (role `'Staff' || 'Admin'`). The `staffGuard` prevents Patient-role access (AC-4).

**Signal state model:**

```typescript
interface StaffNoteUploadState {
  isUploading: boolean;
  uploadProgress: number;
  encounterWarning: boolean;
  serverError: string | null;
  validationErrors: { file?: string; encounterRef?: string };
}
```

## Dependent Tasks

- **US_039 / TASK_002** — `POST /api/staff/documents/upload`, `GET /api/staff/patients/{patientId}/documents`, `DELETE /api/staff/documents/{id}` endpoints must be implemented.
- **US_039 / TASK_003** — DB migration extending `clinical_documents` with `source_type`, `uploaded_by_id`, `encounter_reference`, `deleted_at`, `deletion_reason` must be applied.
- **US_038 / TASK_001** — Patient document upload UI establishes the `DocumentHistoryListComponent` and shared document-related models/services; US_039 TASK_001 reuses or extends those.
- **US_011 / TASK_001** — `AuthInterceptor` attaches Bearer token to all `HttpClient` calls; `staffGuard` protects routes.
- **US_016 / TASK_001** — Staff patient record page must provide the `patientId` route param that `StaffNoteUploadComponent` consumes.

## Impacted Components

| Component                      | Status        | Location                                                                                                 |
| ------------------------------ | ------------- | -------------------------------------------------------------------------------------------------------- |
| `StaffNoteUploadComponent`     | NEW           | `app/features/staff/patient-record/note-upload/staff-note-upload.component.ts`                           |
| `DocumentHistoryListComponent` | NEW or EXTEND | `app/features/documents/document-history-list/document-history-list.component.ts`                        |
| `StaffDocumentService`         | NEW           | `app/features/staff/patient-record/staff-document.service.ts`                                            |
| `StaffDocumentModels`          | NEW           | `app/features/staff/patient-record/staff-document.models.ts`                                             |
| `StaffPatientRecordComponent`  | MODIFY        | Add `<app-staff-note-upload [patientId]="...">` and `<app-document-history-list [patientId]="...">` tabs |
| `AppRoutingModule`             | VERIFY        | `/staff/patients/:patientId` route protected by `staffGuard`                                             |

## Implementation Plan

1. **TypeScript models** (`staff-document.models.ts`):

   ```typescript
   export type DocumentSourceType = "PatientUpload" | "StaffUpload";
   export type DocumentProcessingStatus =
     | "Pending"
     | "Processing"
     | "Completed"
     | "Failed";

   export interface DocumentHistoryItemDto {
     id: string;
     fileName: string;
     fileSize: number;
     sourceType: DocumentSourceType;
     uploadedByName: string | null; // staff full name for StaffUpload
     encounterReference: string | null;
     processingStatus: DocumentProcessingStatus;
     uploadedAt: string; // ISO 8601
     isDeletable: boolean; // computed by BE: StaffUpload && within 24h
   }

   export interface UploadNoteResponse {
     id: string;
     encounterWarning: boolean;
     warningMessage: string | null;
   }
   ```

2. **`StaffDocumentService`**:

   ```typescript
   @Injectable({ providedIn: "root" })
   export class StaffDocumentService {
     private readonly http = inject(HttpClient);

     uploadNote(
       patientId: string,
       file: File,
       encounterRef: string | null,
     ): Observable<HttpEvent<UploadNoteResponse>> {
       const form = new FormData();
       form.append("patientId", patientId);
       form.append("file", file, file.name);
       if (encounterRef) form.append("encounterReference", encounterRef);
       return this.http.post<UploadNoteResponse>(
         "/api/staff/documents/upload",
         form,
         {
           reportProgress: true,
           observe: "events",
         },
       );
     }

     getDocumentHistory(
       patientId: string,
     ): Observable<DocumentHistoryItemDto[]> {
       return this.http.get<DocumentHistoryItemDto[]>(
         `/api/staff/patients/${patientId}/documents`,
       );
     }

     deleteDocument(id: string, reason: string): Observable<void> {
       return this.http.delete<void>(`/api/staff/documents/${id}`, {
         body: { reason },
       });
     }
   }
   ```

3. **`StaffNoteUploadComponent`** — signal-based state + upload progress:

   ```typescript
   @Component({
     standalone: true,
     selector: "app-staff-note-upload",
     changeDetection: ChangeDetectionStrategy.OnPush,
   })
   export class StaffNoteUploadComponent {
     @Input({ required: true }) patientId!: string;
     private readonly svc = inject(StaffDocumentService);
     private readonly destroyRef = inject(DestroyRef);

     uploadState = signal<StaffNoteUploadState>({
       isUploading: false,
       uploadProgress: 0,
       encounterWarning: false,
       serverError: null,
       validationErrors: {},
     });

     selectedFile = signal<File | null>(null);
     encounterRef = signal<string>("");

     onFileSelected(event: Event): void {
       const file = (event.target as HTMLInputElement).files?.[0] ?? null;
       if (!file) return;
       const errors: { file?: string } = {};
       if (file.type !== "application/pdf")
         errors.file = "Only PDF files are accepted";
       else if (file.size > 25 * 1024 * 1024)
         errors.file = "File too large — maximum 25 MB";
       this.uploadState.update((s) => ({
         ...s,
         validationErrors: errors,
         serverError: null,
       }));
       if (!errors.file) this.selectedFile.set(file);
     }

     upload(): void {
       const file = this.selectedFile();
       if (!file) return;
       this.uploadState.update((s) => ({
         ...s,
         isUploading: true,
         uploadProgress: 0,
         encounterWarning: false,
       }));

       this.svc
         .uploadNote(this.patientId, file, this.encounterRef() || null)
         .pipe(takeUntilDestroyed(this.destroyRef))
         .subscribe({
           next: (event) => {
             if (event.type === HttpEventType.UploadProgress && event.total) {
               this.uploadState.update((s) => ({
                 ...s,
                 uploadProgress: Math.round(
                   (100 * event.loaded) / event.total!,
                 ),
               }));
             } else if (event.type === HttpEventType.Response && event.body) {
               this.uploadState.update((s) => ({
                 ...s,
                 isUploading: false,
                 uploadProgress: 100,
                 encounterWarning: event.body!.encounterWarning,
               }));
               this.selectedFile.set(null);
               this.encounterRef.set("");
             }
           },
           error: (err) =>
             this.uploadState.update((s) => ({
               ...s,
               isUploading: false,
               serverError:
                 err.status === 403
                   ? "Access denied."
                   : "Upload failed. Please try again.",
             })),
         });
     }
   }
   ```

4. **Template** — upload form + warning banner:

   ```html
   <form (ngSubmit)="upload()">
     <div>
       <label for="noteFile">Clinical Note (PDF, max 25 MB)</label>
       <input
         id="noteFile"
         type="file"
         accept=".pdf"
         (change)="onFileSelected($event)"
         [attr.aria-describedby]="uploadState().validationErrors.file ? 'fileError' : null"
       />
       @if (uploadState().validationErrors.file) {
       <span id="fileError" role="alert"
         >{{ uploadState().validationErrors.file }}</span
       >
       }
     </div>
     <div>
       <label for="encounterRef">Encounter Reference (optional)</label>
       <input
         id="encounterRef"
         type="text"
         [value]="encounterRef()"
         (input)="encounterRef.set($any($event.target).value)"
       />
     </div>
     @if (uploadState().encounterWarning) {
     <div role="alert" class="warning-banner">
       Encounter reference not found — document linked to patient without
       appointment reference.
       <button
         type="button"
         (click)="uploadState.update(s => ({...s, encounterWarning: false}))"
       >
         Dismiss
       </button>
     </div>
     } @if (uploadState().isUploading) {
     <progress
       [value]="uploadState().uploadProgress"
       max="100"
       aria-label="Upload progress"
     >
       {{ uploadState().uploadProgress }}%
     </progress>
     } @if (uploadState().serverError) {
     <span role="alert">{{ uploadState().serverError }}</span>
     }
     <button
       type="submit"
       [disabled]="!selectedFile() || uploadState().isUploading"
     >
       Upload Note
     </button>
   </form>
   ```

5. **`DocumentHistoryListComponent`** — shows history with source type badge + soft-delete:

   ```typescript
   @Component({
     standalone: true,
     selector: "app-document-history-list",
     changeDetection: ChangeDetectionStrategy.OnPush,
   })
   export class DocumentHistoryListComponent implements OnInit {
     @Input({ required: true }) patientId!: string;
     private readonly svc = inject(StaffDocumentService);
     private readonly destroyRef = inject(DestroyRef);

     documents = signal<DocumentHistoryItemDto[]>([]);
     deletingId = signal<string | null>(null);
     deletionReason = signal<string>("");
     deleteError = signal<string | null>(null);

     ngOnInit(): void {
       this.svc
         .getDocumentHistory(this.patientId)
         .pipe(
           takeUntilDestroyed(this.destroyRef),
           catchError(() => of([])),
         )
         .subscribe((docs) => this.documents.set(docs));
     }

     confirmDelete(id: string): void {
       const reason = this.deletionReason().trim();
       if (reason.length < 10) {
         this.deleteError.set(
           "Please provide a reason (minimum 10 characters).",
         );
         return;
       }
       // Optimistic removal
       const original = this.documents();
       this.documents.update((docs) => docs.filter((d) => d.id !== id));
       this.deletingId.set(null);

       this.svc
         .deleteDocument(id, reason)
         .pipe(takeUntilDestroyed(this.destroyRef))
         .subscribe({
           error: () => {
             this.documents.set(original);
             this.deleteError.set("Delete failed. Please try again.");
           },
         });
     }
   }
   ```

6. **Accessibility requirements:**
   - File input has `aria-describedby` linked to error span.
   - Warning banner and error messages use `role="alert"`.
   - Progress bar uses `aria-label`.
   - Source type badges use `aria-label="Source: Staff Upload"` / `aria-label="Source: Patient Upload"`.

## Current Project State

```
app/
├── features/
│   ├── auth/             (US_011 — completed)
│   ├── patient/          (US_016 — completed)
│   ├── documents/
│   │   └── document-history-list/
│   │       └── document-history-list.component.ts    ← NEW (shared)
│   └── staff/
│       └── patient-record/
│           ├── staff-note-upload/
│           │   └── staff-note-upload.component.ts    ← NEW
│           ├── staff-document.service.ts             ← NEW
│           └── staff-document.models.ts              ← NEW
```

## Expected Changes

| Action | File Path                                                                         | Description                                                                                                                         |
| ------ | --------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| CREATE | `app/features/staff/patient-record/staff-document.models.ts`                      | `DocumentSourceType`, `DocumentProcessingStatus`, `DocumentHistoryItemDto`, `UploadNoteResponse`                                    |
| CREATE | `app/features/staff/patient-record/staff-document.service.ts`                     | `StaffDocumentService`: `uploadNote()` (multipart, `reportProgress`), `getDocumentHistory()`, `deleteDocument()`                    |
| CREATE | `app/features/staff/patient-record/note-upload/staff-note-upload.component.ts`    | `StaffNoteUploadComponent`: signal state, client-side validation, `HttpEventType.UploadProgress` tracking, encounter warning banner |
| CREATE | `app/features/documents/document-history-list/document-history-list.component.ts` | `DocumentHistoryListComponent`: loads history, renders source-type badges, 24h soft-delete with reason dialog, optimistic removal   |
| MODIFY | `app/features/staff/patient-record/staff-patient-record.component.ts`             | Add `<app-staff-note-upload>` and `<app-document-history-list>` as tabs in patient record view                                      |

## External References

- [Angular `HttpClient` upload progress with `reportProgress`](https://angular.dev/guide/http/making-requests#uploading-files)
- [Angular `FormData` with `HttpClient`](https://angular.dev/guide/http/making-requests#sending-form-data)
- [WCAG 2.2 — 4.1.3 Status Messages (`role="alert"`, `aria-live`)](https://www.w3.org/TR/WCAG22/#status-messages)
- [FR-044 — Staff uploads post-visit clinical notes (spec.md#FR-044)](spec.md#FR-044)
- [FR-058 — Clinical data modification event logging (spec.md#FR-058)](spec.md#FR-058)
- [UC-007 — Document Upload sequence diagram (models.md#UC-007)](models.md#UC-007)

## Build Commands

- Refer to: `.propel/build/frontend-build.md`

## Implementation Validation Strategy

- [ ] Unit tests pass: `onFileSelected()` displays "Only PDF files are accepted" for non-PDF files
- [ ] Unit tests pass: `onFileSelected()` displays "File too large" for files > 25 MB
- [ ] Unit tests pass: `upload()` sets `isUploading = true` and tracks `uploadProgress` from `HttpEventType.UploadProgress`
- [ ] Unit tests pass: `encounterWarning = true` renders amber warning banner with `role="alert"` after successful upload
- [ ] Unit tests pass: delete button only renders for `sourceType = 'StaffUpload'` rows with `isDeletable = true`
- [ ] Unit tests pass: `confirmDelete()` rejects deletion reason shorter than 10 characters
- [ ] `staffGuard` blocks `Patient`-role users from the staff patient record route (HTTP 403 on underlying API — AC-4)
- [ ] **[UI Tasks]** Visual comparison against wireframe at 375px, 768px, 1440px (when wireframe is available)
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment (when wireframe is available)

## Implementation Checklist

- [x] Create `StaffNoteUploadComponent` (standalone, `OnPush`, `staffGuard`-protected route): file input (`accept=".pdf"`), `encounterRef` text input, `upload()` button; client-side validate MIME type (`application/pdf`) + size ≤ 25 MB before any HTTP call (AC-1)
- [x] `upload()` uses `HttpClient` with `reportProgress: true, observe: 'events'`; tracks `HttpEventType.UploadProgress` → `uploadProgress` signal (0–100); renders `<progress>` element with `aria-label` during upload
- [x] On `UploadCompleteResponse.encounterWarning = true`, render dismissible amber warning banner with `role="alert"` — "Encounter reference not found — document linked to patient without appointment reference" (edge case — encounter ref not found)
- [x] Create `DocumentHistoryListComponent`: loads from `GET /api/staff/patients/{patientId}/documents`; renders "Staff Upload" amber badge vs "Patient Upload" blue badge; shows staff member name, encounter reference, upload timestamp, processing status chip (AC-2)
- [x] For `isDeletable = true` rows, render "Delete" icon button; clicking opens inline confirmation panel with required `DeletionReason` textarea (min 10 chars) and Cancel/Confirm buttons; optimistic list removal on confirm; revert on API error (edge case — wrong patient upload)
- [x] `StaffDocumentService.uploadNote()` constructs `FormData` with `patientId`, `file`, optional `encounterReference`; all HTTP calls carry Bearer token via `AuthInterceptor` (US_011); `takeUntilDestroyed(destroyRef)` on all subscriptions
- [x] `staffGuard` (role check `'Staff' || 'Admin'`) applied to `/staff/patients/:patientId` route; Patient-role users receive HTTP 403 from the underlying API endpoint (AC-4 — role enforcement at both FE routing and BE controller level)
