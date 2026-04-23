using MediatR;
using Microsoft.AspNetCore.Http;
using Propel.Api.Gateway.Features.Documents.Dtos;

namespace Propel.Api.Gateway.Features.Documents.UploadClinicalDocuments;

/// <summary>
/// MediatR command for <c>POST /api/documents/upload</c> (US_038, AC-2, FR-041).
/// <para>
/// <b>OWASP A01 — Broken Access Control</b>: <c>PatientId</c> is resolved by the controller
/// from the JWT <c>NameIdentifier</c> claim and is NOT accepted from the request body,
/// query string, or URL to prevent horizontal privilege escalation.
/// </para>
/// </summary>
/// <param name="PatientId">The authenticated patient's ID — sourced exclusively from JWT (OWASP A01).</param>
/// <param name="Files">The batch of uploaded PDF files (max 20, each ≤ 25 MB — FR-042).</param>
public record UploadClinicalDocumentsCommand(
    Guid PatientId,
    IReadOnlyList<IFormFile> Files
) : IRequest<UploadBatchResultDto>;
