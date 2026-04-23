namespace Propel.Domain.Entities;

/// <summary>
/// Seed-data entity representing a known insurance provider used for the inline soft-check
/// during appointment booking (US_019, task_003). The table is populated via EF Core HasData()
/// seeding and is never written to at runtime. Matching logic: a patient's submitted
/// <c>InsuranceId</c> must start with <c>MemberIdPrefix</c> (case-insensitive) for the
/// insurer lookup to return <c>Verified</c> (FR-040).
/// </summary>
public sealed class DummyInsurer
{
    public Guid Id { get; set; }

    /// <summary>Display name of the insurance provider, e.g. "BlueCross Shield".</summary>
    public string InsurerName { get; set; } = string.Empty;

    /// <summary>Prefix that the patient's insurance member ID must start with to count as a match, e.g. "BCS".</summary>
    public string MemberIdPrefix { get; set; } = string.Empty;

    /// <summary>When false the record is ignored by the soft-check query.</summary>
    public bool IsActive { get; set; }
}
