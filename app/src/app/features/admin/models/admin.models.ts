export type UserRole = 'Admin' | 'Staff';

// ---------------------------------------------------------------------------
// Audit Log — US_047 / FR-057 / FR-058 / FR-059
// ---------------------------------------------------------------------------

export type AuditUserRole = 'Patient' | 'Staff' | 'Admin';

export type AuditActionType = 'Create' | 'Read' | 'Update' | 'Delete';

export type AuditEntityType =
  | 'Patient'
  | 'Appointment'
  | 'Document'
  | 'IntakeForm'
  | 'MedicalCode'
  | 'DataConflict'
  | 'User';

export interface AuditEventDetails {
  before: Record<string, unknown>;
  after: Record<string, unknown>;
}

export interface AuditEventDto {
  id: string;
  userId: string;
  userRole: AuditUserRole;
  entityType: string;
  entityId: string;
  actionType: AuditActionType;
  ipAddress: string;
  timestamp: string; // ISO 8601 UTC
  details: AuditEventDetails | null; // non-null for FR-058 clinical events
}

export interface AuditLogResponse {
  events: AuditEventDto[];
  nextCursor: string | null;
  totalCount: number;
}

export interface AuditLogQueryParams {
  cursor?: string;
  dateFrom?: string;
  dateTo?: string;
  userId?: string;
  actionType?: AuditActionType;
  entityType?: AuditEntityType;
}

export type UserStatus = 'Active' | 'Deactivated';

export type CredentialEmailStatus = 'Pending' | 'Sent' | 'Failed' | 'Used';

export interface AdminUser {
  id: string;
  name: string;
  email: string;
  role: UserRole;
  status: UserStatus;
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
  emailSent?: boolean;
}

export interface UpdateUserRequest {
  name: string;
  role: UserRole;
}

export interface ReAuthTokenResponse {
  reAuthToken: string;
}

export interface ReAuthModalData {
  /** Sentence fragment displayed in the modal: "Confirm password to {actionLabel}" */
  actionLabel: string;
}

export type ReAuthStatus = 'confirmed' | 'cancelled' | 'timeout';

export interface ReAuthModalResult {
  status: ReAuthStatus;
  reAuthToken?: string;
}
