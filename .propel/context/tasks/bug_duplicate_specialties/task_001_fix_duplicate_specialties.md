# Bug Fix Task - bug_duplicate_specialties

## Bug Report Reference

- **Bug ID**: bug_duplicate_specialties
- **Source**: Visual observation — booking wizard specialty dropdown shows duplicate entries
- **Branch**: `feature/figmaGeneration`
- **Mode**: New file creation

---

## Bug Summary

### Issue Classification

- **Priority**: High
- **Severity**: Core booking workflow broken — patients cannot reliably select a specialty
- **Affected Version**: Current HEAD (feature/figmaGeneration)
- **Environment**: Windows, Chrome, Angular 18 SPA, ASP.NET Core 10 backend

### Steps to Reproduce

1. Start the Angular app (`cd app && ng serve`)
2. Start the backend with a live PostgreSQL DB that has been migrated + seeded
3. Log in as a Patient and navigate to **Book Appointment**
4. Open the **Select specialty** dropdown

**Expected**: Each specialty appears exactly once (10 unique options)
**Actual**: "General Practice" and "Cardiology" appear **twice** in the dropdown

**Error Output**:

```text
GET /api/appointments/specialties → HTTP 200
Response body (example):
[
  { "id": "00000000-0000-0000-0000-000000000001", "name": "General Practice" },
  { "id": "00000000-0000-0000-0000-000000000002", "name": "Cardiology" },
  { "id": "<random-guid-1>", "name": "General Practice" },   ← duplicate
  { "id": "<random-guid-2>", "name": "Cardiology" },         ← duplicate
  { "id": "<random-guid-3>", "name": "Orthopaedics" },
  ...8 more from SeedData
]
```

---

## Root Cause Analysis

- **File 1**: `server/Propel.Api.Gateway/Data/Configurations/SpecialtyConfiguration.cs` (line 33–37)
- **File 2**: `server/Propel.Api.Gateway/Data/SeedData.cs` (lines 12–23)
- **Component**: Database seeding — two independent seeding mechanisms both insert the same specialty names
- **Function**: `SpecialtyConfiguration.Configure()` → `builder.HasData(...)` / `SeedData.SeedSpecialtiesAsync()`

### Cause

There are **two independent seeding sources** for the `specialties` table:

1. **EF Core `HasData` (migration-time seed)** — `SpecialtyConfiguration.cs`:
   Seeds "General Practice" (GUID `00000001`) and "Cardiology" (GUID `00000002`) with **stable, hardcoded GUIDs** via a migration. These rows are inserted whenever `dotnet ef database update` is run on a fresh DB.

2. **Runtime seed — `SeedData.SeedSpecialtiesAsync()`** — called from `Program.cs` on startup:
   Seeds 10 specialties including "General Practice" and "Cardiology" with **new random GUIDs** (no ID assigned → EF generates new UUIDs). The guard `if (await db.Specialties.AnyAsync()) return;` only skips if the table is fully empty.

**Trigger sequence producing duplicates:**
- Step 1: `dotnet ef database update` runs → `HasData` migration inserts 2 rows (GP + Cardiology with stable GUIDs)
- Step 2: App starts → `SeedData.SeedSpecialtiesAsync()` calls `AnyAsync()` → returns `true` (2 rows exist) → **returns early** — so the remaining 8 specialties are never seeded
- **OR** (if migrations were applied to a DB where `SeedData` had already run):
  - Step 1: App starts first → `AnyAsync()` returns `false` → inserts 10 rows with random GUIDs
  - Step 2: `HasData` migration runs → inserts GP (GUID 00000001) and Cardiology (GUID 00000002) → **2 name duplicates in DB**

Additionally, `GetSpecialtiesQueryHandler` performs **no deduplication** — it maps all rows 1:1 from the repository, so DB duplicates propagate directly to the API response and the Angular dropdown.

**Missing database constraint:** The `specialties` table has no `UNIQUE` constraint on the `name` column, allowing silent duplicate insertion.

---

## Impact Assessment

- **Affected Features**: Booking wizard Step 1 (specialty selection), Walk-in booking, any dropdown consuming `GET /api/appointments/specialties`
- **User Impact**: Patients see the same specialty twice; selecting either entry returns the same results but creates confusion and undermines trust in the UI
- **Data Integrity Risk**: Yes — duplicate specialty rows can cause ambiguous foreign key references in `appointments.specialty_id`
- **Security Implications**: None

---

## Fix Overview

Three-layer fix:

1. **Database constraint** — Add a `UNIQUE` constraint on `specialties.name` to prevent future duplicate insertion at the DB level.
2. **Seed conflict resolution** — Remove "General Practice" and "Cardiology" from `SeedData.SpecialtyNames` since they are already owned by `HasData` with stable GUIDs. `SeedData` should only seed the remaining 8 specialties **using their stable HasData GUIDs** as a skip-check, OR replace `HasData` entirely with `SeedData` using all stable GUIDs.
3. **Query-layer deduplication (defence-in-depth)** — Add `.DistinctBy(s => s.Name)` in `GetSpecialtiesQueryHandler` so that even if DB duplicates exist, the API never returns them.

---

## Fix Dependencies

- No migrations have been applied by users in production (dev-only environment confirmed)
- EF Core migration tooling available: `dotnet ef migrations add`

---

## Impacted Components

### Backend — Data Layer

| File | Change |
|------|--------|
| `server/Propel.Api.Gateway/Data/SeedData.cs` | Remove `"General Practice"` and `"Cardiology"` from `SpecialtyNames` list (they are already seeded by `HasData`) |
| `server/Propel.Api.Gateway/Data/Configurations/SpecialtyConfiguration.cs` | Add `.HasIndex(s => s.Name).IsUnique()` unique constraint |
| `server/Propel.Api.Gateway/Migrations/<new_migration>.cs` | New EF Core migration adding unique index on `specialties.name` |

### Backend — Query Layer

| File | Change |
|------|--------|
| `server/Propel.Modules.Appointment/Handlers/GetSpecialtiesQueryHandler.cs` | Add `.DistinctBy(s => s.Name)` before `.Select(...)` as defence-in-depth |

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | `server/Propel.Api.Gateway/Data/SeedData.cs` | Remove "General Practice" and "Cardiology" from `SpecialtyNames` |
| MODIFY | `server/Propel.Api.Gateway/Data/Configurations/SpecialtyConfiguration.cs` | Add `HasIndex(s => s.Name).IsUnique()` |
| CREATE | `server/Propel.Api.Gateway/Migrations/<timestamp>_AddUniqueIndexOnSpecialtyName.cs` | EF Core migration for unique index |
| MODIFY | `server/Propel.Modules.Appointment/Handlers/GetSpecialtiesQueryHandler.cs` | Add `.DistinctBy(s => s.Name)` |

---

## Implementation Plan

### Step 1 — Fix `SeedData.cs`: Remove duplicated entries

Remove `"General Practice"` and `"Cardiology"` from `SpecialtyNames`:

```csharp
private static readonly IReadOnlyList<string> SpecialtyNames =
[
    "Orthopaedics",
    "Neurology",
    "Paediatrics",
    "Dermatology",
    "Oncology",
    "Radiology",
    "Psychiatry",
    "Ophthalmology"
];
```

> "General Practice" and "Cardiology" are already owned by `HasData` with stable GUIDs `00000001` and `00000002`. `SeedData` only seeds the remaining 8.

### Step 2 — Add unique index in `SpecialtyConfiguration.cs`

```csharp
builder.HasIndex(s => s.Name)
       .IsUnique()
       .HasDatabaseName("ix_specialties_name_unique");
```

### Step 3 — Generate EF Core migration

```powershell
cd server/Propel.Api.Gateway
dotnet ef migrations add AddUniqueIndexOnSpecialtyName
dotnet ef database update
```

### Step 4 — Add `.DistinctBy` in `GetSpecialtiesQueryHandler.cs`

```csharp
var specialties = await _repository.GetAllAsync(cancellationToken);
return specialties
    .DistinctBy(s => s.Name)     // defence-in-depth: never return name duplicates
    .OrderBy(s => s.Name)
    .Select(s => new SpecialtyDto(s.Id, s.Name))
    .ToList()
    .AsReadOnly();
```

### Step 5 — Clean existing DB duplicates (one-time SQL, dev only)

If running against an existing DB with duplicate rows:

```sql
-- Remove duplicate rows keeping the stable HasData GUIDs (lower UUID wins)
DELETE FROM specialties
WHERE id NOT IN (
  SELECT DISTINCT ON (name) id
  FROM specialties
  ORDER BY name, id
);
```

---

## Regression Prevention Strategy

- [ ] Run `GET /api/appointments/specialties` — response must contain exactly 10 distinct specialty names
- [ ] Attempt to insert a duplicate specialty name — DB must reject with unique constraint violation
- [ ] Run booking wizard E2E test — specialty dropdown shows 10 entries with no duplicates
- [ ] Run `npx playwright test tests/booking.spec.ts --project=standalone` — all tests pass

---

## Rollback Procedure

1. Revert `SeedData.cs` change (restore "General Practice" and "Cardiology" to `SpecialtyNames`)
2. Revert `SpecialtyConfiguration.cs` change
3. Run `dotnet ef migrations remove` to remove the unique index migration
4. No data loss — rollback only removes the constraint

---

## External References

- EF Core HasData docs: https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding
- `DistinctBy` (LINQ): https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.distinctby

---

## Build Commands

```powershell
# Apply migration
cd server/Propel.Api.Gateway
dotnet ef migrations add AddUniqueIndexOnSpecialtyName
dotnet ef database update

# Verify API response (no duplicates)
curl http://localhost:5000/api/appointments/specialties | jq '[.[].name] | unique | length'
# Expected output: 10
```

---

## Implementation Validation Strategy

- [ ] API returns exactly 10 specialties with no duplicate names
- [ ] `specialties` table has a unique index on `name`
- [ ] Booking wizard dropdown shows 10 options, none repeated
- [ ] All existing Playwright tests pass

## Implementation Checklist

- [ ] Remove "General Practice" and "Cardiology" from `SeedData.SpecialtyNames`
- [ ] Add `HasIndex(s => s.Name).IsUnique()` to `SpecialtyConfiguration.cs`
- [ ] Generate and apply EF Core migration `AddUniqueIndexOnSpecialtyName`
- [ ] Add `.DistinctBy(s => s.Name)` in `GetSpecialtiesQueryHandler.cs`
- [ ] Run clean DB and verify 10 specialties seeded (2 from HasData + 8 from SeedData)
- [ ] Run `npx playwright test tests/booking.spec.ts --project=standalone` — all pass
