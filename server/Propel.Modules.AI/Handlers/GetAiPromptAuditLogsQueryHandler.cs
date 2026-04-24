using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Utilities;
using Propel.Modules.AI.Dtos;
using Propel.Modules.AI.Interfaces;
using Propel.Modules.AI.Queries;

namespace Propel.Modules.AI.Handlers;

/// <summary>
/// Handles <see cref="GetAiPromptAuditLogsQuery"/>: applies optional filters, performs
/// keyset-paginated read of AI prompt audit logs (page size fixed at 50, ORDER BY recordedAt DESC),
/// and returns an <see cref="AiPromptAuditPageResponse"/> with an opaque cursor and total count
/// (EP-010/us_049, AC-4, task_002).
/// <para>
/// Page query and count query are issued concurrently via <c>Task.WhenAll</c> to minimise
/// latency on large audit tables (performance).
/// </para>
/// </summary>
public sealed class GetAiPromptAuditLogsQueryHandler
    : IRequestHandler<GetAiPromptAuditLogsQuery, AiPromptAuditPageResponse>
{
    private const int PageSize = 50;

    private readonly IAiPromptAuditReadRepository _readRepo;
    private readonly ILogger<GetAiPromptAuditLogsQueryHandler> _logger;

    public GetAiPromptAuditLogsQueryHandler(
        IAiPromptAuditReadRepository readRepo,
        ILogger<GetAiPromptAuditLogsQueryHandler> logger)
    {
        _readRepo = readRepo;
        _logger   = logger;
    }

    public async Task<AiPromptAuditPageResponse> Handle(
        GetAiPromptAuditLogsQuery request,
        CancellationToken cancellationToken)
    {
        var cursor = AuditCursorHelper.Decode(request.Cursor);

        // Issue page query and total count concurrently.
        var pageTask  = _readRepo.GetPageAsync(request.UserId, request.SessionId, cursor, PageSize, cancellationToken);
        var countTask = _readRepo.CountAsync(request.UserId, request.SessionId, cancellationToken);

        await Task.WhenAll(pageTask, countTask).ConfigureAwait(false);

        var entries    = pageTask.Result;
        var totalCount = countTask.Result;

        // Determine next cursor from the last returned entry when a full page was returned.
        string? nextCursor = null;
        if (entries.Count == PageSize)
        {
            var last = entries[^1];
            nextCursor = AuditCursorHelper.Encode(last.RecordedAt, last.Id);
        }

        _logger.LogInformation(
            "GetAiPromptAuditLogs: returned {Count}/{Total} entries. HasNextPage={HasNext}.",
            entries.Count,
            totalCount,
            nextCursor is not null);

        var dtos = entries
            .Select(e => new AiPromptAuditLogDto(
                e.Id,
                e.RecordedAt,
                e.SessionId,
                e.RequestingUserId,
                e.ModelName,
                e.FunctionName,
                e.RedactedPrompt,
                e.ResponseText,
                e.PromptTokenCount,
                e.CompletionTokenCount,
                e.ContentFilterBlocked))
            .ToList();

        return new AiPromptAuditPageResponse(dtos, nextCursor, totalCount);
    }
}
