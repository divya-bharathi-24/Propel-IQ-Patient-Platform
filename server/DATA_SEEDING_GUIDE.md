# Master Table Data Seeding Guide

## Overview

This project uses a **two-layer seeding strategy** to populate master/reference tables:

1. **Compile-time seeding** (EF Core `HasData`)
2. **Runtime seeding** (Startup initialization)

## Master Tables

### 1. Specialties Table (`specialties`)

Medical specialty reference data that drives appointment booking.

**Compile-time seed** (2 records):
- `SpecialtyConfiguration.cs` ? `HasData()` 
- Records: General Practice, Cardiology

**Runtime seed** (10 records):
- `SeedData.SeedSpecialtiesAsync()` 
- Full list: General Practice, Cardiology, Orthopaedics, Neurology, Paediatrics, Dermatology, Oncology, Radiology, Psychiatry, Ophthalmology

**Why both?**
- Compile-time: Ensures base data exists in migrations
- Runtime: Populates full reference set on first startup

---

### 2. Dummy Insurers Table (`dummy_insurers`)

Insurance provider lookup data for soft-check validation during booking.

**Compile-time seed** (5 records):
- `DummyInsurerConfiguration.cs` ? `HasData()`
- Records:
  - BlueCross Shield (BCS)
  - Aetna Health (AET)
  - United HealthGroup (UHG)
  - Cigna Medical (CGN)
  - Humana Plus (HMN)

**Runtime seed**: None (fully seeded at compile-time)

**Location**: `Propel.Api.Gateway\Data\Configurations\DummyInsurerConfiguration.cs`

---

### 3. System Settings Table (`system_settings`)

Configuration data for system-wide settings (e.g., reminder intervals).

**Compile-time seed** (1 record):
- `SystemSettingConfiguration.cs` ? `HasData()`
- Record: `reminder_interval_hours` = `[48,24,2]`

**Runtime seed**:
- `SeedData.SeedSystemSettingsAsync()`
- Idempotent: Checks if key exists before inserting

**Purpose**: Configurable reminder intervals for automated appointment reminders (48h, 24h, 2h before appointment)

---

## Seeding Execution Flow

### On Application Startup (`Program.cs`)

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    
    // 1. Apply all pending EF Core migrations
    await db.Database.MigrateAsync();
    Console.WriteLine("[Startup] Migrations applied successfully.");
    
    // 2. Seed all master/reference data (idempotent)
    await SeedData.SeedAllMasterDataAsync(db);
}
```

### Idempotency Guarantee

All runtime seeding methods are **idempotent**:

```csharp
public static async Task SeedSpecialtiesAsync(AppDbContext db)
{
    // Only seeds if table is empty
    if (await db.Specialties.AnyAsync())
    {
        return;
    }
    
    // ... insert specialty records
}

public static async Task SeedSystemSettingsAsync(AppDbContext db)
{
    // Only seeds if specific key doesn't exist
    var existingSetting = await db.SystemSettings
        .FirstOrDefaultAsync(s => s.Key == "reminder_interval_hours");

    if (existingSetting == null)
    {
        // ... insert system setting
    }
}
```

This ensures:
- ? Safe to run on every startup
- ? No duplicate data on restarts
- ? Can be re-run after database cleanup

---

## Code Locations

### Seed Data Implementation
- **File**: `Propel.Api.Gateway\Data\SeedData.cs`
- **Methods**:
  - `SeedSpecialtiesAsync()` - Seeds 10 medical specialties
  - `SeedSystemSettingsAsync()` - Seeds system settings
  - `SeedAllMasterDataAsync()` - Orchestrator method (calls all seed methods)

### Entity Configurations (Compile-time seeds)
- `Propel.Api.Gateway\Data\Configurations\SpecialtyConfiguration.cs`
- `Propel.Api.Gateway\Data\Configurations\DummyInsurerConfiguration.cs`
- `Propel.Api.Gateway\Data\Configurations\SystemSettingConfiguration.cs`

### Startup Registration
- **File**: `Propel.Api.Gateway\Program.cs`
- **Line**: After `db.Database.MigrateAsync()`

---

## Verification

### Check Specialty Data
```sql
SELECT id, name, description FROM specialties ORDER BY name;
```

Expected: 10 rows

### Check Insurance Provider Data
```sql
SELECT id, insurer_name, member_id_prefix, is_active 
FROM dummy_insurers 
ORDER BY insurer_name;
```

Expected: 5 rows

### Check System Settings Data
```sql
SELECT key, value, updated_at FROM system_settings;
```

Expected: 1 row (reminder_interval_hours)

---

## Adding New Master Data

### Option 1: Runtime Seed (Preferred for large datasets)

1. Create seed method in `SeedData.cs`:
```csharp
public static async Task SeedMyNewTableAsync(AppDbContext db)
{
    if (await db.MyNewTable.AnyAsync())
    {
        return;
    }

    var records = new List<MyNewEntity>
    {
        new MyNewEntity { Name = "Record 1" },
        new MyNewEntity { Name = "Record 2" }
    };

    db.MyNewTable.AddRange(records);
    await db.SaveChangesAsync();
    
    Console.WriteLine($"[SeedData] MyNewTable seeded: {records.Count} rows");
}
```

2. Call from `SeedAllMasterDataAsync()`:
```csharp
public static async Task SeedAllMasterDataAsync(AppDbContext db)
{
    Console.WriteLine("[SeedData] Starting master data seeding...");

    await SeedSpecialtiesAsync(db);
    await SeedSystemSettingsAsync(db);
    await SeedMyNewTableAsync(db); // ? Add here

    Console.WriteLine("[SeedData] Master data seeding completed.");
}
```

### Option 2: Compile-time Seed (For critical bootstrap data)

1. Add to your `IEntityTypeConfiguration<T>`:
```csharp
public void Configure(EntityTypeBuilder<MyEntity> builder)
{
    // ... other configuration ...

    builder.HasData(
        new MyEntity { Id = Guid.Parse("..."), Name = "Record 1" },
        new MyEntity { Id = Guid.Parse("..."), Name = "Record 2" }
    );
}
```

2. Generate migration:
```bash
dotnet ef migrations add SeedMyNewTable --project Propel.Api.Gateway
```

---

## Best Practices

### ? DO

- Use **deterministic GUIDs** for compile-time seeds (prevents migration conflicts)
- Make all seed methods **idempotent** (check before insert)
- Log seed operations with row counts
- Use `HasData()` for critical bootstrap data
- Use runtime seeding for large reference datasets

### ? DON'T

- Never hard-code production secrets in seed data
- Don't seed transactional data (only reference/master data)
- Don't use random GUIDs in `HasData()` (causes migration changes on every build)
- Don't seed without idempotency checks

---

## Troubleshooting

### Seed data not appearing

1. **Check console logs** on startup for seed messages:
```
[SeedData] Specialties seeded: 10 rows
[SeedData] SystemSettings seeded: reminder_interval_hours
```

2. **Verify migrations applied**:
```bash
dotnet ef migrations list --project Propel.Api.Gateway
```

3. **Manually run seeding** (Development only):
```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await SeedData.SeedAllMasterDataAsync(db);
}
```

### Duplicate key errors

- Check if `idempotency` logic is working
- Verify table constraints (unique indexes)
- Clear table and re-run: `DELETE FROM specialties;` then restart app

---

## Summary

| Table | Compile-time Seed | Runtime Seed | Idempotent | Records |
|-------|------------------|--------------|------------|---------|
| `specialties` | 2 (base) | 10 (full) | ? | 10 |
| `dummy_insurers` | 5 | - | ? | 5 |
| `system_settings` | 1 | 1 (verify) | ? | 1 |

All seeding happens **automatically on application startup** after migrations are applied. No manual intervention required.
