using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// INSERT-only EF Core implementation of <see cref="IAuditLogRepository"/> (AD-7, NFR-013).
/// Uses <see cref="IDbContextFactory{TContext}"/> to create an independent <see cref="AppDbContext"/>
/// scope per write, ensuring audit entries are never rolled back by an outer business transaction.
/// This repository deliberately exposes no UPDATE or DELETE methods.
/// The database-level INSERT-only trigger (applied in migration) provides an additional
/// enforcement layer that rejects any UPDATE or DELETE at the PostgreSQL level.
/// </summary>
public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public AuditLogRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task AppendAsync(AuditLog auditLog, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        context.AuditLogs.Add(auditLog);
        await context.SaveChangesAsync(cancellationToken);
    }
}
