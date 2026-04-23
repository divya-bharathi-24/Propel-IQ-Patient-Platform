using MediatR;
using Propel.Modules.Calendar.Dtos;

namespace Propel.Modules.Calendar.Commands;

/// <summary>
/// Handles the Microsoft OAuth 2.0 callback after the patient grants consent
/// (us_036, AC-2, AC-4, edge case — revoked consent).
/// <list type="number">
///   <item>Validates and decodes the <paramref name="State"/> CSRF token.</item>
///   <item>Verifies appointment ownership using the <c>patientId</c> extracted from state.</item>
///   <item>Exchanges <paramref name="Code"/> for an access token via MSAL.</item>
///   <item>Creates a Microsoft Graph calendar event (<c>POST /me/events</c>).</item>
///   <item>Upserts <c>CalendarSync { provider = Outlook, syncStatus = Synced }</c>.</item>
///   <item>Writes audit log <c>OutlookCalendarSynced</c>.</item>
///   <item>On Graph 401: sets <c>syncStatus = Revoked</c>; throws <see cref="Exceptions.OutlookCalendarAuthRevokedException"/>.</item>
///   <item>On other Graph failure: sets <c>syncStatus = Failed</c>; enqueues retry.</item>
/// </list>
/// </summary>
public sealed record HandleOutlookCallbackCommand(string Code, string State)
    : IRequest<CalendarSyncResultDto>;
