using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Dtos;
using Propel.Domain.Interfaces;
using Propel.Domain.Utilities;
using Propel.Modules.Admin.Dtos;
using Propel.Modules.Admin.Queries;

namespace Propel.Modules.Admin.Handlers;

/// <summary>
/// Handles <see cref="GetAuditLogsQuery"/>:
/// applies optional filters, performs keyset-paginated read of the audit log (page size fixed at 50),
/// and returns an <see cref="AuditLogPageResponse"/> with an opaque <c>nextCursor</c> and total count (US_047).
/// <para>
/// <c>GetPageAsync</c> and <c>CountAsync</c> are issued concurrently via <c>Task.WhenAll</c>
/// to minimise latency on large tables (AC-1).
/// </para>
/// </summary>
public sealed class GetAuditLogsQueryHandler
    : IRequestHandler<GetAuditLogsQuery, AuditLogPageResponse>
{
    private const int PageSize = 50;

    private readonly IAuditLogReadRepository _readRepo;
    private readonly ILogger<GetAuditLogsQueryHandler> _logger;

    public GetAuditLogsQueryHandler(
        IAuditLogReadRepository readRepo,
        ILogger<GetAuditLogsQueryHandler> logger)
    {
        _readRepo = readRepo;
        _logger = logger;
    }

    public async Task<AuditLogPageResponse> Handle(
        GetAuditLogsQuery request,
        CancellationToken cancellationToken)
    {
        var filters = new AuditLogFilterParams(
            request.DateFrom,
            request.DateTo,
            request.UserId,
            request.ActionType,
            request.EntityType);

        var cursor = AuditCursorHelper.Decode(request.Cursor);

        // Issue page query and total count concurrently (AC-1, performance).
        var pageTask  = _readRepo.GetPageAsync(filters, cursor, PageSize, cancellationToken);
        var countTask = _readRepo.CountAsync(filters, cancellationToken);

        await Task.WhenAll(pageTask, countTask);

        var events     = pageTask.Result;
        var totalCount = countTask.Result;

        // Determine next cursor from the last returned event.
        string? nextCursor = null;
        if (events.Count == PageSize)
        {
            var last = events[^1];
            nextCursor = AuditCursorHelper.Encode(last.Timestamp, last.Id);
        }

        _logger.LogInformation(
            "GetAuditLogs: returned {Count}/{Total} events. HasNextPage={HasNext}.",
            events.Count,
            totalCount,
            nextCursor is not null);

        return new AuditLogPageResponse(events, nextCursor, totalCount);
    }
}
