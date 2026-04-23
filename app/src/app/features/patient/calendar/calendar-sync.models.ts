/**
 * All possible sync states for a Google Calendar appointment sync.
 *
 * - `none`     — patient has not yet initiated a sync for this appointment
 * - `pending`  — OAuth flow has been launched; awaiting callback
 * - `synced`   — event exists in Google Calendar; eventLink is populated
 * - `failed`   — Google Calendar API returned an error; ICS fallback shown
 * - `declined` — patient declined OAuth consent; guidance message shown
 * - `expired`  — patient's Google token has expired; Reconnect prompt shown
 */
export type CalendarSyncStatus =
  | 'none'
  | 'pending'
  | 'synced'
  | 'failed'
  | 'declined'
  | 'expired';

/** DTO returned by GET /api/calendar/google/status/{appointmentId} */
export interface CalendarSyncStatusDto {
  syncStatus: CalendarSyncStatus;
  /** Google Calendar event URL; present only when syncStatus is 'synced'. */
  eventLink?: string;
}

// ── Outlook Calendar sync types (EP-007 / US_036) ───────────────────────────

/** Calendar provider discriminator used in shared sync-status endpoints. */
export type CalendarProvider = 'Google' | 'Outlook';

/**
 * All possible sync states for a Microsoft Outlook Calendar appointment sync.
 *
 * - `Unknown`    — no sync has been attempted; initial state on component load
 * - `Initiating` — OAuth PKCE flow has started; awaiting Microsoft redirect
 * - `Synced`     — event exists in Outlook Calendar; eventLink is populated
 * - `Failed`     — Microsoft Graph API returned an error; ICS fallback shown
 * - `Revoked`    — patient's Outlook OAuth consent was revoked; Reconnect shown
 */
export type OutlookSyncStatus =
  | 'Unknown'
  | 'Initiating'
  | 'Synced'
  | 'Failed'
  | 'Revoked';

/** Local signal state shape for OutlookCalendarSyncComponent. */
export interface OutlookSyncState {
  status: OutlookSyncStatus;
  eventLink: string | null;
  errorMessage: string | null;
}

/** DTO returned by POST /api/calendar/outlook/initiate */
export interface InitiateCalendarSyncResponse {
  authorizationUrl: string;
}

/** DTO returned by GET /api/calendar/sync-status?appointmentId=&provider= */
export interface CalendarSyncStatusResponse {
  provider: CalendarProvider;
  syncStatus: OutlookSyncStatus;
  eventLink: string | null;
}
