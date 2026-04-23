namespace Propel.Api.Gateway.Features.Documents.Exceptions;

/// <summary>
/// Thrown when a staff member attempts a soft-delete operation on a document that is not
/// eligible: either the source type is not <c>StaffUpload</c>, the 24-hour delete window
/// has expired, or the document is already soft-deleted.
/// Maps to HTTP 403 Forbidden via <c>GlobalExceptionFilter</c> (US_039, OWASP A01).
/// </summary>
public sealed class DocumentForbiddenException : Exception
{
    public DocumentForbiddenException(string message)
        : base(message)
    {
    }
}
