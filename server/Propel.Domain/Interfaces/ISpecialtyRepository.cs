namespace Propel.Domain.Interfaces;

/// <summary>
/// Lightweight read model returned by <see cref="ISpecialtyRepository"/>.
/// </summary>
public sealed record SpecialtyReadModel(Guid Id, string Name);

/// <summary>
/// Repository abstraction for querying specialty reference data (US_018, task_002).
/// Implementations live in the infrastructure layer and use EF Core projections with
/// <c>AsNoTracking()</c> for read-only performance.
/// </summary>
public interface ISpecialtyRepository
{
    /// <summary>
    /// Returns all specialties ordered by name.
    /// </summary>
    Task<IReadOnlyList<SpecialtyReadModel>> GetAllAsync(CancellationToken cancellationToken = default);
}
