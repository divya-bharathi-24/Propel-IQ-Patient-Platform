namespace Propel.Api.Gateway.Features.Documents.Dtos;

/// <summary>
/// Batch upload response for <c>POST /api/documents/upload</c> (US_038, AC-2, AC-4).
/// Contains a per-file result list so the frontend can identify which files succeeded and which failed.
/// HTTP 200 when all files succeed; HTTP 207 Multi-Status when any file in the batch fails.
/// </summary>
/// <param name="Files">Per-file results in the same order as the submitted batch.</param>
public record UploadBatchResultDto(IReadOnlyList<UploadFileResultDto> Files);
