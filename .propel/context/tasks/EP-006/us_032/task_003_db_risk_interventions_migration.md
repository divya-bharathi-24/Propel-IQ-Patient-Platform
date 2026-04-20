# Task - TASK_003

## Requirement Reference

- **User Story**: US_032 — High-Risk Appointment Flag with Recommended Interventions
- **Story Location**: `.propel/context/tasks/EP-006/us_032/us_032.md`
- **Acceptance Criteria**:
  - AC-2: Intervention is marked as accepted with staff ID and timestamp (requires `staff_id` and `acknowledged_at` columns).
  - AC-3: Dismissal is recorded with staff ID and an optional dismissal reason (requires `dismissal_reason` column).
  - AC-4: Unacknowledged flags surfaced in "Requires Attention" (requires efficient `WHERE status = 'Pending'` query on `risk_interventions`).
- **Edge Cases**:
  - Score drops to Medium before acknowledgment: `status = 'AutoCleared'` is a valid status value captured by the `CHECK` constraint; auto-cleared rows are retained for audit history.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | No |
| **Figma URL** | N/A |
| **Wireframe Status** | N/A |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | N/A |
| **Screen Spec** | N/A |
| **UXR Requirements** | N/A |
| **Design Tokens** | N/A |

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Backend | ASP.NET Core Web API | .NET 9 |
| ORM | Entity Framework Core | 9.x |
| Database | PostgreSQL | 16+ |
| AI/ML | N/A | N/A |
| Vector Store | N/A | N/A |
| AI Gateway | N/A | N/A |
| Mobile | N/A | N/A |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Add an additive EF Core 9 migration creating the `risk_interventions` table for `US_032`. This is the only schema change required — the `no_show_risks` table already exists from the `AddClinicalEntities` migration (EP-DATA/us_007). The `NoShowRisk` entity in design.md has `score (decimal 0-1)` and `factors (JSONB)` which are sufficient for severity derivation (`score > 0.66 = High`) without a stored `severity` column.

**`risk_interventions` table:**

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | `UUID` PK | `DEFAULT gen_random_uuid()` |
| `no_show_risk_id` | `UUID` FK | NOT NULL → `no_show_risks.id` ON DELETE CASCADE |
| `appointment_id` | `UUID` FK | NOT NULL → `appointments.id` ON DELETE CASCADE (denormalized for query efficiency — avoids join through `no_show_risks`) |
| `type` | `VARCHAR(30)` | NOT NULL, CHECK IN ('AdditionalReminder', 'CallbackRequest') |
| `status` | `VARCHAR(20)` | NOT NULL DEFAULT 'Pending', CHECK IN ('Pending', 'Accepted', 'Dismissed', 'AutoCleared') |
| `staff_id` | `UUID` FK | NULL → `users.id` ON DELETE SET NULL |
| `acknowledged_at` | `TIMESTAMPTZ` | NULL |
| `dismissal_reason` | `VARCHAR(500)` | NULL |
| `created_at` | `TIMESTAMPTZ` | NOT NULL DEFAULT `NOW()` |

**Index:** Partial index `IX_risk_interventions_pending` on `(appointment_id) WHERE status = 'Pending'` — optimizes the `GetRequiresAttentionQuery` which filters for pending interventions per appointment.

**`Down()` migration:** Drops the partial index first, then the table. No existing table is modified.

**EF Core entity configuration** (`RiskInterventionConfiguration : IEntityTypeConfiguration<RiskIntervention>`):
- `HasKey(e => e.Id)`
- `HasOne(e => e.NoShowRisk).WithMany(r => r.RiskInterventions).HasForeignKey(e => e.NoShowRiskId).OnDelete(DeleteBehavior.Cascade)`
- `HasOne(e => e.Appointment).WithMany().HasForeignKey(e => e.AppointmentId).OnDelete(DeleteBehavior.Cascade)`
- `HasOne(e => e.Staff).WithMany().HasForeignKey(e => e.StaffId).OnDelete(DeleteBehavior.SetNull).IsRequired(false)`
- `Property(e => e.Type).HasConversion<string>().HasMaxLength(30)`
- `Property(e => e.Status).HasConversion<string>().HasMaxLength(20)`
- `Property(e => e.DismissalReason).HasMaxLength(500).IsRequired(false)`
- `Property(e => e.CreatedAt).HasColumnType("timestamptz").HasDefaultValueSql("NOW()")`
- Partial index: `HasIndex(e => e.AppointmentId).HasFilter("status = 'Pending'").HasDatabaseName("IX_risk_interventions_pending")`

**`NoShowRisk` entity update** (no migration change): Add navigation property `public ICollection<RiskIntervention> RiskInterventions { get; set; } = [];` to the existing `NoShowRisk` entity class (code-only change, no new migration column).

## Dependent Tasks

- **US_007 (EP-DATA)** — `AddClinicalEntities` migration must be applied; `no_show_risks` table must exist.
- **US_006 (EP-DATA)** — `appointments` table and `users` table must exist for FK references.
- This task must be completed before TASK_002 (BE feature layer) can be implemented.

## Impacted Components

| Component | Status | Location |
|-----------|--------|----------|
| `RiskIntervention` entity class | NEW | `Server/Entities/RiskIntervention.cs` |
| `RiskInterventionConfiguration` | NEW | `Server/Data/Configurations/RiskInterventionConfiguration.cs` |
| `<timestamp>_AddRiskInterventions` migration | NEW | `Server/Migrations/<timestamp>_AddRiskInterventions.cs` |
| `AppDbContext` | MODIFY | Add `DbSet<RiskIntervention> RiskInterventions` property |
| `NoShowRisk` entity class | MODIFY | Add `ICollection<RiskIntervention> RiskInterventions` navigation property |

## Implementation Plan

1. **`RiskIntervention` entity class**:

   ```csharp
   public class RiskIntervention
   {
       public Guid Id { get; set; }
       public Guid NoShowRiskId { get; set; }
       public Guid AppointmentId { get; set; }
       public InterventionType Type { get; set; }
       public InterventionStatus Status { get; set; } = InterventionStatus.Pending;
       public Guid? StaffId { get; set; }
       public DateTime? AcknowledgedAt { get; set; }
       public string? DismissalReason { get; set; }
       public DateTime CreatedAt { get; set; }

       // Navigation
       public NoShowRisk NoShowRisk { get; set; } = null!;
       public Appointment Appointment { get; set; } = null!;
       public User? Staff { get; set; }
   }

   public enum InterventionType   { AdditionalReminder, CallbackRequest }
   public enum InterventionStatus { Pending, Accepted, Dismissed, AutoCleared }
   ```

2. **`RiskInterventionConfiguration`** (`IEntityTypeConfiguration<RiskIntervention>`):

   ```csharp
   public void Configure(EntityTypeBuilder<RiskIntervention> builder)
   {
       builder.HasKey(e => e.Id);
       builder.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
       builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
       builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
       builder.Property(e => e.DismissalReason).HasMaxLength(500).IsRequired(false);
       builder.Property(e => e.CreatedAt).HasColumnType("timestamptz").HasDefaultValueSql("NOW()");
       builder.Property(e => e.AcknowledgedAt).HasColumnType("timestamptz").IsRequired(false);

       builder.HasOne(e => e.NoShowRisk)
           .WithMany(r => r.RiskInterventions)
           .HasForeignKey(e => e.NoShowRiskId)
           .OnDelete(DeleteBehavior.Cascade);

       builder.HasOne(e => e.Appointment)
           .WithMany()
           .HasForeignKey(e => e.AppointmentId)
           .OnDelete(DeleteBehavior.Cascade);

       builder.HasOne(e => e.Staff)
           .WithMany()
           .HasForeignKey(e => e.StaffId)
           .OnDelete(DeleteBehavior.SetNull)
           .IsRequired(false);

       // Partial index for requires-attention query performance
       builder.HasIndex(e => e.AppointmentId)
           .HasFilter("status = 'Pending'")
           .HasDatabaseName("IX_risk_interventions_pending");

       builder.ToTable("risk_interventions");
   }
   ```

3. **Generated migration Up()** (EF Core tooling generates the SQL; verify the following are present):

   ```csharp
   migrationBuilder.CreateTable(
       name: "risk_interventions",
       columns: table => new {
           id = table.Column<Guid>(nullable: false, defaultValueSql: "gen_random_uuid()"),
           no_show_risk_id = table.Column<Guid>(nullable: false),
           appointment_id = table.Column<Guid>(nullable: false),
           type = table.Column<string>(maxLength: 30, nullable: false),
           status = table.Column<string>(maxLength: 20, nullable: false, defaultValue: "Pending"),
           staff_id = table.Column<Guid>(nullable: true),
           acknowledged_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
           dismissal_reason = table.Column<string>(maxLength: 500, nullable: true),
           created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "NOW()")
       },
       constraints: table => {
           table.PrimaryKey("PK_risk_interventions", x => x.id);
           table.CheckConstraint("CK_risk_interventions_type",
               "type IN ('AdditionalReminder', 'CallbackRequest')");
           table.CheckConstraint("CK_risk_interventions_status",
               "status IN ('Pending', 'Accepted', 'Dismissed', 'AutoCleared')");
           table.ForeignKey("FK_risk_interventions_no_show_risks_no_show_risk_id",
               x => x.no_show_risk_id, "no_show_risks", "id", onDelete: ReferentialAction.Cascade);
           table.ForeignKey("FK_risk_interventions_appointments_appointment_id",
               x => x.appointment_id, "appointments", "id", onDelete: ReferentialAction.Cascade);
           table.ForeignKey("FK_risk_interventions_users_staff_id",
               x => x.staff_id, "users", "id", onDelete: ReferentialAction.SetNull);
       });

   migrationBuilder.CreateIndex(
       name: "IX_risk_interventions_pending",
       table: "risk_interventions",
       column: "appointment_id",
       filter: "status = 'Pending'");
   ```

4. **Down() migration** — drop in reverse dependency order:

   ```csharp
   migrationBuilder.DropIndex(
       name: "IX_risk_interventions_pending",
       table: "risk_interventions");

   migrationBuilder.DropTable(name: "risk_interventions");
   ```

5. **`AppDbContext` update**:

   ```csharp
   public DbSet<RiskIntervention> RiskInterventions => Set<RiskIntervention>();
   ```

6. **`NoShowRisk` entity update** (code-only, no migration):

   ```csharp
   // Add navigation property to existing NoShowRisk entity
   public ICollection<RiskIntervention> RiskInterventions { get; set; } = [];
   ```

## Current Project State

```
Server/
├── Data/
│   ├── AppDbContext.cs                                       ← MODIFY (add DbSet)
│   └── Configurations/
│       ├── AppointmentConfiguration.cs                      (existing)
│       ├── NoShowRiskConfiguration.cs                       (existing, from US_007)
│       └── RiskInterventionConfiguration.cs                 ← NEW
├── Entities/
│   ├── NoShowRisk.cs                                         ← MODIFY (add nav property)
│   └── RiskIntervention.cs                                   ← NEW
└── Migrations/
    ├── <timestamp>_Initial.cs                                (US_006)
    ├── <timestamp>_AddClinicalEntities.cs                    (US_007)
    ├── <timestamp>_AddInsuranceValidation.cs                 (US_019)
    └── <timestamp>_AddRiskInterventions.cs                   ← NEW
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Entities/RiskIntervention.cs` | Entity class: all columns; `InterventionType` and `InterventionStatus` enums |
| CREATE | `Server/Data/Configurations/RiskInterventionConfiguration.cs` | EF Core `IEntityTypeConfiguration<RiskIntervention>`: FK cascade rules, enum-to-string conversions, partial index `IX_risk_interventions_pending` |
| CREATE | `Server/Migrations/<timestamp>_AddRiskInterventions.cs` | Additive EF Core migration: `risk_interventions` table, 2 CHECK constraints, 3 FK constraints, partial index; `Down()` drops index then table |
| MODIFY | `Server/Data/AppDbContext.cs` | Add `DbSet<RiskIntervention> RiskInterventions => Set<RiskIntervention>()` |
| MODIFY | `Server/Entities/NoShowRisk.cs` | Add `ICollection<RiskIntervention> RiskInterventions { get; set; } = []` navigation property (code-only, no migration column) |

## External References

- [EF Core — `HasFilter()` for partial indexes in PostgreSQL](https://learn.microsoft.com/en-us/ef/core/modeling/indexes#index-filter)
- [EF Core — `HasConversion<string>()` for enum-to-string storage](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions)
- [Npgsql EF Core — `HasColumnType("timestamptz")`](https://www.npgsql.org/efcore/mapping/datetime.html)
- [EF Core — `DeleteBehavior.SetNull` for nullable FK](https://learn.microsoft.com/en-us/ef/core/saving/cascade-delete)
- [DR-004 / design.md — JSONB flexible storage pattern for clinical data](design.md#DR-004)

## Build Commands

- Refer to: `.propel/build/backend-build.md`
- Migration: `dotnet ef migrations add AddRiskInterventions --project Server`
- Apply: `dotnet ef database update --project Server`

## Implementation Validation Strategy

- [ ] Migration applies cleanly to staging DB without errors
- [ ] `risk_interventions` table created with correct columns, types, and constraints
- [ ] CHECK constraint for `type` rejects values outside `('AdditionalReminder', 'CallbackRequest')`
- [ ] CHECK constraint for `status` rejects values outside `('Pending', 'Accepted', 'Dismissed', 'AutoCleared')`
- [ ] FK to `no_show_risks` CASCADE DELETE verified (delete NoShowRisk → interventions deleted)
- [ ] FK to `users` SET NULL verified (delete Staff user → `staff_id` nulled, intervention retained)
- [ ] `Down()` migration rolls back cleanly (index dropped before table)
- [ ] Partial index `IX_risk_interventions_pending` present in `pg_indexes`

## Implementation Checklist

- [ ] Create `RiskIntervention` entity (`Server/Entities/RiskIntervention.cs`): all 9 columns with correct C# types; `InterventionType` and `InterventionStatus` enums defined in same file or shared enums folder
- [ ] Create `RiskInterventionConfiguration` (`IEntityTypeConfiguration<RiskIntervention>`): `HasConversion<string>()` on both enums; `HasDefaultValueSql("gen_random_uuid()")` on Id; CASCADE on `no_show_risk_id` + `appointment_id`; `SetNull` on `staff_id`; partial index `HasFilter("status = 'Pending'")` on `appointment_id`
- [ ] Generate migration `AddRiskInterventions` via EF Core tooling; verify generated SQL includes 2 CHECK constraints and partial index before applying
- [ ] `Down()` drops partial index first, then drops table — no orphaned constraints
- [ ] Modify `AppDbContext` to add `DbSet<RiskIntervention>` property; modify `NoShowRisk` entity to add `ICollection<RiskIntervention> RiskInterventions = []` navigation property (enables EF Core navigation in `GetRequiresAttentionQueryHandler`)
