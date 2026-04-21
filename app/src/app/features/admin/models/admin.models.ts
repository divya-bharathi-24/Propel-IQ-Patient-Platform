export type UserRole = 'Admin' | 'Staff';

export type CredentialEmailStatus = 'Pending' | 'Sent' | 'Failed' | 'Used';

export interface AdminUser {
  id: string;
  name: string;
  email: string;
  role: UserRole;
  status: string;
  lastLoginAt: string | null;
  credentialEmailStatus: CredentialEmailStatus;
}

export interface CreateUserRequest {
  name: string;
  email: string;
  role: UserRole;
}

export interface CreateUserResponse {
  id: string;
  message: string;
}
