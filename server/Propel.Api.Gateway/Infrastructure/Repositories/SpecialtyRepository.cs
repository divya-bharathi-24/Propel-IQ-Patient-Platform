using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ISpecialtyRepository"/> (US_018, task_002).
/// Uses a single <c>AsNoTracking()</c> projection query ordered by name.
/// </summary>
public sealed class SpecialtyRepository : ISpecialtyRepository
{
    private readonly AppDbContext _db;

    public SpecialtyRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SpecialtyReadModel>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.Specialties
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new SpecialtyReadModel(s.Id, s.Name))
            .ToListAsync(cancellationToken);
    }
}
