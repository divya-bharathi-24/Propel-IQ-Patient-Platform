using MediatR;

namespace Propel.Api.Gateway.Features.Documents.Notifications;

/// <summary>
/// Published by <c>UploadStaffClinicalNoteCommandHandler</c> after a successful
/// <c>SaveChangesAsync()</c> to trigger the asynchronous AI extraction pipeline (AC-3, AD-3).
/// This notification is publish-only from the US_039 context; the <c>INotificationHandler</c>
/// implementation lives in the AI module (separate task — decoupled async trigger per AD-3).
/// </summary>
/// <param name="DocumentId">The newly persisted <see cref="Propel.Domain.Entities.ClinicalDocument"/> ID.</param>
/// <param name="PatientId">The patient whose document was uploaded — passed for AI pipeline context.</param>
public record ClinicalDocumentUploadedNotification(
    Guid DocumentId,
    Guid PatientId
) : INotification;
