import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  ViewChild,
  inject,
} from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { StaffNoteUploadComponent } from './note-upload/staff-note-upload.component';
import { DocumentHistoryListComponent } from '../../documents/document-history-list/document-history-list.component';

/**
 * StaffPatientRecordComponent — US_039 / TASK_001
 *
 * Patient record page for staff members at `/staff/patients/:patientId`.
 * Hosts the clinical note upload form and the document history list side-by-side
 * (desktop) / stacked (mobile).
 *
 * Route param `patientId` is passed as `@Input` to both child components.
 *
 * Access control:
 * - Route is protected by `authGuard` + `staffGuard` (see `staff.routes.ts`).
 * - Patient-role users are redirected to `/access-denied` at the route level;
 *   the underlying API enforces HTTP 403 as a defence-in-depth measure (AC-4).
 *
 * WCAG 2.2 AA:
 * - `<main>` landmark with descriptive `aria-label`.
 */
@Component({
  selector: 'app-staff-patient-record',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    MatButtonModule,
    MatIconModule,
    StaffNoteUploadComponent,
    DocumentHistoryListComponent,
  ],
  templateUrl: './staff-patient-record.component.html',
  styleUrl: './staff-patient-record.component.scss',
})
export class StaffPatientRecordComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);

  /** ViewChild reference so the parent can call `reload()` after upload. */
  @ViewChild(DocumentHistoryListComponent)
  private readonly historyList?: DocumentHistoryListComponent;

  /** Patient UUID extracted from the route parameter `:patientId`. */
  protected patientId = '';

  ngOnInit(): void {
    this.patientId = this.route.snapshot.paramMap.get('patientId') ?? '';
  }

  /** Triggered by `StaffNoteUploadComponent.uploadComplete` output event. */
  protected onUploadComplete(): void {
    this.historyList?.reload();
  }
}
