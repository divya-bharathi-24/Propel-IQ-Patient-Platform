import { Component, OnInit, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import {
  MAT_DIALOG_DATA,
  MatDialogModule,
  MatDialogRef,
} from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { AdminUser, UserRole } from '../../models/admin.models';

export interface UserFormDialogData {
  /** When provided, the dialog is in Edit mode; otherwise Create mode. */
  user?: AdminUser;
}

export interface UserFormDialogResult {
  name: string;
  email: string;
  role: UserRole;
}

/**
 * Reactive form dialog for creating and editing staff/admin accounts.
 *
 * Create mode: all fields editable.
 * Edit mode:   email is read-only (email changes are not supported in this story).
 */
@Component({
  selector: 'app-user-form-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatSelectModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './user-form-dialog.component.html',
})
export class UserFormDialogComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<UserFormDialogComponent>);
  readonly data = inject<UserFormDialogData>(MAT_DIALOG_DATA);

  readonly isSubmitting = signal(false);
  readonly serverError = signal<string | null>(null);

  readonly roles: UserRole[] = ['Staff', 'Admin'];
  readonly isEditMode = !!this.data?.user;

  form!: FormGroup;

  ngOnInit(): void {
    const existing = this.data?.user;
    this.form = this.fb.group({
      name: [
        existing?.name ?? '',
        [Validators.required, Validators.maxLength(200)],
      ],
      email: [
        { value: existing?.email ?? '', disabled: this.isEditMode },
        [Validators.required, Validators.email, Validators.maxLength(254)],
      ],
      role: [existing?.role ?? 'Staff', [Validators.required]],
    });
  }

  get nameControl(): AbstractControl {
    return this.form.get('name')!;
  }

  get emailControl(): AbstractControl {
    return this.form.get('email')!;
  }

  get roleControl(): AbstractControl {
    return this.form.get('role')!;
  }

  onSubmit(): void {
    if (this.form.invalid || this.isSubmitting()) {
      this.form.markAllAsTouched();
      return;
    }

    const { name, role } = this.form.getRawValue();
    const email = this.isEditMode
      ? (this.data.user?.email ?? '')
      : (this.form.getRawValue().email as string);

    const result: UserFormDialogResult = {
      name: (name as string).trim(),
      email: email.trim().toLowerCase(),
      role: role as UserRole,
    };

    this.dialogRef.close(result);
  }

  onCancel(): void {
    this.dialogRef.close(undefined);
  }
}
