import { Component, OnInit, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { AdminService } from '../../services/admin.service';
import { UserRole } from '../../models/admin.models';

@Component({
  selector: 'app-create-user-dialog',
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
  templateUrl: './create-user-dialog.component.html',
})
export class CreateUserDialogComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly adminService = inject(AdminService);
  private readonly dialogRef = inject(MatDialogRef<CreateUserDialogComponent>);

  readonly isSubmitting = signal(false);
  readonly serverError = signal<string | null>(null);

  readonly roles: UserRole[] = ['Staff', 'Admin'];

  form!: FormGroup;

  ngOnInit(): void {
    this.form = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      email: [
        '',
        [Validators.required, Validators.email, Validators.maxLength(254)],
      ],
      role: ['Staff', [Validators.required]],
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

    this.isSubmitting.set(true);
    this.serverError.set(null);

    const { name, email, role } = this.form.getRawValue();

    this.adminService
      .createUser({
        name: name.trim(),
        email: email.trim().toLowerCase(),
        role,
      })
      .subscribe({
        next: (result) => {
          this.isSubmitting.set(false);
          this.dialogRef.close(result);
        },
        error: (err: { status: number; message: string }) => {
          this.isSubmitting.set(false);
          if (err.status === 409) {
            this.emailControl.setErrors({ alreadyExists: true });
          } else if (err.status === 403) {
            this.dialogRef.close(null);
          } else {
            this.serverError.set(
              'An unexpected error occurred. Please try again.',
            );
          }
        },
      });
  }

  onCancel(): void {
    this.dialogRef.close(null);
  }
}
