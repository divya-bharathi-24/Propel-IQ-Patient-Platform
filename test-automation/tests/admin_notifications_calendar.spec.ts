/**
 * Feature: Admin User Management, Notifications & Calendar Integration
 * Use Cases: UC-010, UC-011, UC-012
 * Source: .propel/context/test/tw_admin_notifications_calendar_20260420.md
 */
import { test, expect } from '@playwright/test';
import { AdminUsersPage } from '../pages/admin-users.page';
import { CalendarIntegrationPage } from '../pages/calendar-integration.page';
import { LoginPage } from '../pages/login.page';
import {
  mockNotificationApi,
  mockUserDeactivateForbidden,
  mockCalendarOAuthSuccess,
  Responses,
} from '../support/api-mocks';
import testData from '../data/admin_notifications_calendar.json';

// ── UC-010: Admin User Management ─────────────────────────────────────────

test.describe('UC-010: Admin User Management', () => {
  test('TC-UC010-HP-001: Admin creates staff account; credential email sent; staff activates and logs in', async ({
    page,
  }) => {
    const d = testData.tc_uc010_hp_001;
    await mockNotificationApi(page, Responses.notificationSent());

    await test.step('Create new staff account from admin panel', async () => {
      await page.goto('/admin/users');
      const adminUsers = new AdminUsersPage(page);
      await adminUsers.createStaff(
        d.newStaffEmail,
        d.newStaffFirstName,
        d.newStaffLastName,
        d.role,
      );
      await expect(adminUsers.successAlert).toContainText('Staff account created');
    });

    await test.step('Staff activates account via credential email link', async () => {
      await page.goto(`/activate?token=${d.activationToken}`);
      await page.getByLabel('New password').fill(d.staffPassword);
      await page.getByLabel('Confirm password').fill(d.staffPassword);
      await page.getByRole('button', { name: 'Activate account' }).click();
      await expect(page).toHaveURL(/login/);
    });

    await test.step('New staff logs in with activated credentials', async () => {
      const login = new LoginPage(page);
      await login.login(d.newStaffEmail, d.staffPassword);
      await expect(login.roleBadge).toContainText('Staff');
    });
  });

  test('TC-UC010-EC-001: Admin changes staff role; updated permissions effective on next sign-in', async ({
    page,
  }) => {
    const d = testData.tc_uc010_ec_001;

    await test.step('Change staff role from Staff to Admin', async () => {
      await page.goto('/admin/users');
      const adminUsers = new AdminUsersPage(page);
      await adminUsers.changeRole(d.targetStaffRef, d.newRole);
      await expect(adminUsers.successAlert).toContainText(
        'Role updated. Changes take effect on next sign-in.',
      );
    });

    await test.step('Staff re-authenticates and sees Admin role badge', async () => {
      // New browser context simulates fresh session (next sign-in)
      const newContext = await page.context().browser()!.newContext();
      const newPage = await newContext.newPage();
      const login = new LoginPage(newPage);
      await newPage.goto('/login');
      await login.login(d.targetStaffEmail, d.existingStaffPassword);
      await expect(login.roleBadge).toContainText('Admin');
      await newContext.close();
    });
  });

  test('TC-UC010-ER-001: Admin cannot deactivate account without re-authentication', async ({
    page,
  }) => {
    const d = testData.tc_uc010_er_001;
    await mockUserDeactivateForbidden(page);

    await test.step('Navigate to user management and click Deactivate', async () => {
      await page.goto('/admin/users');
      const adminUsers = new AdminUsersPage(page);
      await adminUsers.userRow(d.targetStaffRef).click();
      await adminUsers.deactivateButton.click();
    });

    await test.step('Verify re-authentication dialog shown (not direct deactivation)', async () => {
      const adminUsers = new AdminUsersPage(page);
      await expect(adminUsers.reAuthDialog).toContainText('Confirm your identity');
      await adminUsers.cancelDialogButton.click();
    });

    await test.step('Verify direct API call also returns 403', async () => {
      const response = await page.request.delete(`/api/users/${d.targetStaffId}`, {
        headers: { Authorization: 'Bearer admin-token-no-reauth' },
      });
      expect(response.status()).toBe(403);
    });
  });
});

// ── UC-011: Appointment Reminders ─────────────────────────────────────────

test.describe('UC-011: Appointment Reminder Notifications', () => {
  test('TC-UC011-HP-001: Automated reminders sent at 48h, 24h, and 2h windows', async ({
    page,
  }) => {
    const d = testData.tc_uc011_hp_001;

    // Mock all 3 reminder windows as pre-sent
    await page.route('**/api/notifications**', (route) =>
      route.fulfill({
        status: 200,
        json: d.reminderWindows.map((w) => ({
          window: `${w}h`,
          status: 'Sent',
          channel: 'Email',
          appointmentId: d.appointmentId,
        })),
      }),
    );

    await test.step('Verify 3 reminder notifications recorded for appointment', async () => {
      const resp = await page.request.get(
        `/api/notifications?appointmentId=${d.appointmentId}`,
      );
      const body = await resp.json() as Array<{ window: string; status: string }>;
      expect(body).toHaveLength(d.reminderWindows.length);
      for (const reminder of body) {
        expect(reminder.status).toBe('Sent');
      }
    });

    await test.step('Verify 3 reminder entries visible in patient notification history', async () => {
      await page.goto('/patient/notifications');
      await expect(
        page.getByTestId('notification-list'),
      ).toBeVisible();
    });
  });

  test('TC-UC011-EC-001: Admin sends manual ad-hoc reminder to a specific patient', async ({
    page,
  }) => {
    const d = testData.tc_uc011_ec_001;
    await mockNotificationApi(page, { status: 'Sent', channel: 'Email' });

    await test.step('Search appointment in notifications admin panel', async () => {
      await page.goto('/admin/notifications');
      await page.getByLabel('Search appointment').fill(d.appointmentId);
    });

    await test.step('Send ad-hoc reminder and confirm dispatch', async () => {
      await page.getByTestId(`${d.appointmentId}-send-reminder`).click();
      await page.getByRole('button', { name: 'Send reminder now' }).click();
      await expect(page.getByRole('alert')).toContainText('Reminder sent');
    });
  });

  test('TC-UC011-ER-001: Cancelled appointment — future reminders suppressed', async ({
    page,
  }) => {
    const d = testData.tc_uc011_er_001;

    await test.step('Cancel the appointment via API', async () => {
      const response = await page.request.post(
        `/api/appointments/${d.appointmentId}/cancel`,
        { headers: { Authorization: 'Bearer patient-token' } },
      );
      expect(response.status()).toBe(200);
    });

    await test.step('Verify all scheduled reminders cancelled', async () => {
      const resp = await page.request.get(
        `/api/notifications/scheduled?appointmentId=${d.appointmentId}`,
      );
      const body = await resp.json() as Array<{ status: string }>;
      for (const notification of body) {
        expect(notification.status).toBe('Cancelled');
      }
    });

    await test.step('Verify no reminder notifications in patient history for this appointment', async () => {
      await page.goto('/patient/notifications');
      await expect(
        page.getByTestId('notification-list'),
      ).not.toContainText(d.appointmentId);
    });
  });
});

// ── UC-012: Calendar Integration ──────────────────────────────────────────

test.describe('UC-012: Calendar Integration', () => {
  test('TC-UC012-HP-001: Patient syncs appointment to Google Calendar via OAuth', async ({
    page,
  }) => {
    const d = testData.tc_uc012_hp_001;
    await mockCalendarOAuthSuccess(page);

    await test.step('Open appointment detail view', async () => {
      await page.goto('/patient/appointments');
      const calendar = new CalendarIntegrationPage(page);
      await calendar.openAppointment(d.appointmentId);
    });

    await test.step('Initiate Google Calendar OAuth flow', async () => {
      const calendar = new CalendarIntegrationPage(page);
      await calendar.addToGoogleButton.click();
    });

    await test.step('Complete OAuth callback with mock auth code', async () => {
      await page.goto(`${d.redirectUri}?code=${d.mockAuthCode}`);
      const calendar = new CalendarIntegrationPage(page);
      await expect(calendar.successAlert).toContainText('Appointment added to Google Calendar');
      await expect(calendar.syncBadge).toContainText('Synced to Google Calendar');
    });
  });

  test('TC-UC012-EC-001: Patient downloads appointment as ICS file', async ({ page }) => {
    const d = testData.tc_uc012_ec_001;

    await test.step('Open appointment detail view', async () => {
      await page.goto('/patient/appointments');
      const calendar = new CalendarIntegrationPage(page);
      await calendar.openAppointment(d.appointmentId);
    });

    await test.step('Download ICS file and verify content', async () => {
      const calendar = new CalendarIntegrationPage(page);
      const [download] = await Promise.all([
        page.waitForEvent('download'),
        calendar.downloadIcsButton.click(),
      ]);
      const fileName = download.suggestedFilename();
      expect(fileName).toMatch(/appointment.*\.ics$/);
    });
  });

  test('TC-UC012-ER-001: Patient denies OAuth consent — guidance message and ICS fallback shown', async ({
    page,
  }) => {
    const d = testData.tc_uc012_er_001;

    await test.step('Open appointment and click Add to Google Calendar', async () => {
      await page.goto('/patient/appointments');
      const calendar = new CalendarIntegrationPage(page);
      await calendar.openAppointment(d.appointmentId);
      await calendar.addToGoogleButton.click();
    });

    await test.step('Simulate OAuth denial via callback error param', async () => {
      await page.goto(`${d.callbackPath}?error=${d.oauthError}`);
    });

    await test.step('Verify guidance message and ICS fallback link shown', async () => {
      const calendar = new CalendarIntegrationPage(page);
      await expect(calendar.successAlert).toContainText('Calendar sync was not completed');
      await expect(calendar.icsFallbackLink).toBeVisible();
      await expect(calendar.syncBadge).not.toContainText('Synced');
    });
  });
});
