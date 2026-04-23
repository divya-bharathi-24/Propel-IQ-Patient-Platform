using MediatR;
using Propel.Modules.Patient.Dtos;

namespace Propel.Modules.Patient.Commands;

/// <summary>
/// MediatR command to sync a patient's localStorage backup draft to the server (US_030, AC-3).
/// <para>
/// Compares <see cref="LocalTimestamp"/> against <c>IntakeRecord.lastModifiedAt</c> to detect
/// offline-sync conflicts. If the local draft is strictly newer, it is applied to
/// <c>draftData</c> via UPSERT. If the server record is equal-or-newer, returns
/// <c>Applied = false</c> with both versions so the frontend can present a conflict resolution UI.
/// </para>
/// <para>
/// <c>PatientId</c> is extracted from the JWT <c>sub</c> claim in the controller — never from
/// the request body (OWASP A01 — Broken Access Control).
/// </para>
/// </summary>
public sealed record SyncLocalDraftCommand(
    Guid AppointmentId,
    IntakeFieldMap LocalFields,
    DateTimeOffset LocalTimestamp,
    Guid PatientId) : IRequest<SyncLocalDraftResult>;

/// <summary>
/// Result returned by <see cref="SyncLocalDraftCommand"/>.
/// <para>
/// When <see cref="Applied"/> is <c>true</c>, the local draft was accepted and persisted.
/// When <c>false</c>, a conflict was detected: <see cref="ServerFields"/> and
/// <see cref="ServerLastModifiedAt"/> carry the server-side version for the client to display.
/// </para>
/// </summary>
public sealed record SyncLocalDraftResult(
    bool Applied,
    IntakeFieldMap? ServerFields,
    DateTimeOffset? ServerLastModifiedAt);
