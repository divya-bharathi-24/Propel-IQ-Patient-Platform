using MediatR;

namespace Propel.Api.Gateway.Features.Documents.SoftDeleteClinicalDocument;

/// <summary>
/// MediatR command for <c>DELETE /api/staff/documents/{id}</c> (US_039, AC-2 edge case).
/// Soft-deletes a staff-uploaded clinical document within the 24-hour window.
/// <para>
/// <b>OWASP A01</b>: <c>staffId</c> is resolved by the handler from JWT — never from the request body.
/// </para>
/// </summary>
/// <param name="DocumentId">The <see cref="Propel.Domain.Entities.ClinicalDocument"/> to soft-delete.</param>
/// <param name="Reason">Mandatory deletion reason (10–500 chars). Captured in the audit log (FR-058).</param>
public record SoftDeleteClinicalDocumentCommand(
    Guid DocumentId,
    string Reason
) : IRequest<Unit>;
