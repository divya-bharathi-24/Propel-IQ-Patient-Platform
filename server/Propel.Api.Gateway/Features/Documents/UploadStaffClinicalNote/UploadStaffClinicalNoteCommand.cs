using MediatR;
using Microsoft.AspNetCore.Http;
using Propel.Api.Gateway.Features.Documents.Dtos;

namespace Propel.Api.Gateway.Features.Documents.UploadStaffClinicalNote;

/// <summary>
/// MediatR command for <c>POST /api/staff/documents/upload</c> (US_039, AC-1).
/// <para>
/// <b>OWASP A01 — Broken Access Control</b>: <c>StaffId</c> is resolved by the handler
/// from the JWT <c>NameIdentifier</c> claim and is NOT part of this command — callers must
/// never pass <c>staffId</c> from form body, query string, or URL.
/// </para>
/// </summary>
/// <param name="PatientId">The target patient for this clinical note.</param>
/// <param name="File">The uploaded PDF file (validated: PDF MIME, ≤ 25 MB).</param>
/// <param name="EncounterReference">Optional appointment reference string (max 100 chars).</param>
public record UploadStaffClinicalNoteCommand(
    Guid PatientId,
    IFormFile File,
    string? EncounterReference
) : IRequest<UploadNoteResponseDto>;
