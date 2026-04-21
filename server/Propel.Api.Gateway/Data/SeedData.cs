using Microsoft.EntityFrameworkCore;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data;

/// <summary>
/// One-time reference data seeder. Called on application startup after EF Core migrations
/// are applied. Each method is idempotent — it inserts rows only when the target table is empty.
/// </summary>
public static class SeedData
{
    private static readonly IReadOnlyList<string> SpecialtyNames =
    [
        "General Practice",
        "Cardiology",
        "Orthopaedics",
        "Neurology",
        "Paediatrics",
        "Dermatology",
        "Oncology",
        "Radiology",
        "Psychiatry",
        "Ophthalmology"
    ];

    /// <summary>
    /// Seeds the <see cref="Specialty"/> reference table if it is empty (AC2).
    /// </summary>
    public static async Task SeedSpecialtiesAsync(AppDbContext db)
    {
        if (await db.Specialties.AnyAsync())
        {
            return;
        }

        var specialties = SpecialtyNames
            .Select(name => new Specialty { Name = name })
            .ToList();

        db.Specialties.AddRange(specialties);
        await db.SaveChangesAsync();

        Console.WriteLine($"[SeedData] Specialties seeded: {specialties.Count} rows");
    }
}
