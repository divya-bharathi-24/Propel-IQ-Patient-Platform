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

    /// <summary>
    /// Seeds the system settings table with default reminder intervals.
    /// This is idempotent and will not duplicate data.
    /// </summary>
    public static async Task SeedSystemSettingsAsync(AppDbContext db)
    {
        // Check if reminder_interval_hours already exists
        var existingSetting = await db.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == "reminder_interval_hours");

        if (existingSetting == null)
        {
            var reminderSetting = new SystemSetting
            {
                Key = "reminder_interval_hours",
                Value = "[48,24,2]", // 48h, 24h, 2h reminder intervals
                UpdatedAt = DateTime.UtcNow
            };

            db.SystemSettings.Add(reminderSetting);
            await db.SaveChangesAsync();
            Console.WriteLine($"[SeedData] SystemSettings seeded: reminder_interval_hours");
        }
        else
        {
            Console.WriteLine($"[SeedData] SystemSettings already exists: reminder_interval_hours");
        }
    }

    /// <summary>
    /// Master data seeding orchestrator. Call this from Program.cs on startup.
    /// All seed methods are idempotent and safe to run multiple times.
    /// </summary>
    public static async Task SeedAllMasterDataAsync(AppDbContext db)
    {
        Console.WriteLine("[SeedData] Starting master data seeding...");

        // Seed specialties (medical service types)
        await SeedSpecialtiesAsync(db);

        // Seed system settings (configuration data)
        await SeedSystemSettingsAsync(db);

        // Note: DummyInsurers table is seeded via EF Core HasData in DummyInsurerConfiguration
        // and does not need runtime seeding. It contains:
        // - BlueCross Shield (BCS)
        // - Aetna Health (AET)
        // - United HealthGroup (UHG)
        // - Cigna Medical (CGN)
        // - Humana Plus (HMN)

        Console.WriteLine("[SeedData] Master data seeding completed.");
    }
}
