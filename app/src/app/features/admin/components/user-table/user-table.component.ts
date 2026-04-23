import {
  AfterViewInit,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginator, MatPaginatorModule } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatSort, MatSortModule } from '@angular/material/sort';
import { MatTableDataSource, MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AdminUser, UserRole } from '../../models/admin.models';
import { UserStatusBadgeComponent } from '../user-status-badge/user-status-badge.component';

/**
 * Presentational component: renders a sortable, filterable, paginated mat-table
 * of admin/staff user accounts. All actions are emitted upward to the page.
 *
 * Self-deactivation is disabled when `user.id === currentAdminId`.
 */
@Component({
  selector: 'app-user-table',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatSelectModule,
    MatSortModule,
    MatTableModule,
    MatTooltipModule,
    UserStatusBadgeComponent,
  ],
  templateUrl: './user-table.component.html',
  styleUrl: './user-table.component.scss',
})
export class UserTableComponent implements OnChanges, AfterViewInit {
  @Input({ required: true }) users: AdminUser[] = [];
  @Input({ required: true }) currentAdminId: string | null = null;

  @Output() readonly editUser = new EventEmitter<AdminUser>();
  @Output() readonly deactivateUser = new EventEmitter<AdminUser>();
  @Output() readonly resendEmail = new EventEmitter<AdminUser>();
  @Output() readonly changeRole = new EventEmitter<{
    user: AdminUser;
    newRole: UserRole;
  }>();

  @ViewChild(MatSort) sort!: MatSort;
  @ViewChild(MatPaginator) paginator!: MatPaginator;

  readonly filterValue = signal('');

  readonly dataSource = new MatTableDataSource<AdminUser>([]);

  readonly displayedColumns = [
    'name',
    'email',
    'role',
    'status',
    'lastLoginAt',
    'actions',
  ];

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['users']) {
      this.dataSource.data = this.users;
    }
  }

  ngAfterViewInit(): void {
    this.dataSource.sort = this.sort;
    this.dataSource.paginator = this.paginator;

    // Custom filter predicate: searches name, email, and role
    this.dataSource.filterPredicate = (user: AdminUser, filter: string) => {
      const normalized = filter.trim().toLowerCase();
      return (
        user.name.toLowerCase().includes(normalized) ||
        user.email.toLowerCase().includes(normalized) ||
        user.role.toLowerCase().includes(normalized)
      );
    };
  }

  applyFilter(value: string): void {
    this.filterValue.set(value);
    this.dataSource.filter = value.trim().toLowerCase();
    this.dataSource.paginator?.firstPage();
  }

  isSelf(userId: string): boolean {
    return this.currentAdminId !== null && userId === this.currentAdminId;
  }

  formatDate(dateStr: string | null): string {
    if (!dateStr) return '—';
    return new Date(dateStr).toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  }

  onEdit(user: AdminUser): void {
    this.editUser.emit(user);
  }

  onDeactivate(user: AdminUser): void {
    this.deactivateUser.emit(user);
  }

  onResendEmail(user: AdminUser): void {
    this.resendEmail.emit(user);
  }

  onChangeRole(user: AdminUser, newRole: UserRole): void {
    this.changeRole.emit({ user, newRole });
  }
}
