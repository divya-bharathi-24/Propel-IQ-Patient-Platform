using Propel.Domain.Entities;

namespace Propel.Modules.Clinical.Exceptions;

/// <summary>
/// Thrown when a <c>POST /360-view/verify</c> request is blocked by one or more
/// <see cref="DataConflict"/> records with <c>Severity = Critical</c> and
/// <c>ResolutionStatus = Unresolved</c> for the target patient (AC-4).
/// Maps to HTTP 409 Conflict in the global exception handler.
/// </summary>
public sealed class UnresolvedConflictsException : Exception
{
    public IReadOnlyList<DataConflict> Conflicts { get; }

    public UnresolvedConflictsException(IReadOnlyList<DataConflict> conflicts)
        : base("Patient profile verification is blocked by unresolved Critical conflicts.")
    {
        Conflicts = conflicts;
    }
}
