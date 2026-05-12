import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterOutlet, Router } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';

interface NavItem {
  label: string;
  icon: string;
  route: string;
  roles?: string[];
}

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterOutlet],
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.scss',
})
export class AppShellComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly userRole = this.authService.currentRole();
  readonly userName = 'Jane Anderson'; // TODO: Get from auth service
  
  get navItems(): NavItem[] {
    const role = this.userRole;
    
    // Patient navigation
    if (role === 'Patient') {
      return [
        { label: 'Dashboard', icon: 'dashboard', route: '/patient/dashboard' },
        { label: 'My Appointments', icon: 'calendar', route: '/appointments' },
        { label: 'My Documents', icon: 'documents', route: '/documents' },
        { label: 'My Profile', icon: 'profile', route: '/patient/profile' },
      ];
    }
    
    // Staff navigation
    if (role === 'Staff') {
      return [
        { label: 'Dashboard', icon: 'dashboard', route: '/staff/dashboard' },
        { label: 'Queue', icon: 'queue', route: '/staff/queue' },
        { label: 'Appointments', icon: 'calendar', route: '/staff/appointments' },
        { label: 'Patients', icon: 'patients', route: '/staff/patients' },
      ];
    }
    
    // Admin navigation
    if (role === 'Admin') {
      return [
        { label: 'Dashboard', icon: 'dashboard', route: '/admin/dashboard' },
        { label: 'Appointments', icon: 'calendar', route: '/admin/appointments' },
        { label: 'Users', icon: 'users', route: '/admin/users' },
        { label: 'Audit Log', icon: 'audit', route: '/admin/audit' },
      ];
    }
    
    return [];
  }

  isActiveRoute(route: string): boolean {
    return this.router.url.startsWith(route);
  }

  onLogout(): void {
    this.authService.logout('user_initiated');
  }
}
