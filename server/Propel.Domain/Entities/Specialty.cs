namespace Propel.Domain.Entities;

/// <summary>
/// Specialty lookup entity representing a medical provider specialty.
/// Used as a reference table seeded on first run.
/// </summary>
public sealed class Specialty
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    // Navigation properties
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}
