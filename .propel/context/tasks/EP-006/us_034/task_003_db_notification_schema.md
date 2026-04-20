# Task - task_003_db_notification_schema

## Requirement Reference

- **User Story:** us_034 — Manual Ad-Hoc Reminder Trigger & Delivery Logging
- **Story Location:** `.propel/context/tasks/EP-006/us_034/us_034.md`
- **Acceptance Criteria:**
  - AC-2: A `Notification` record is created for each channel with `status = Sent|Failed`, UTC timestamp, and `triggeredBy = staffId` — requires `TriggeredBy` column on `Notifications` table
  - AC-4: Failure reason from the delivery provider is logged — requires `ErrorReason` column on `Notifications` table
- **Edge Cases:**
  - `TriggeredBy` is nullable — automated reminders (system-triggered) have `NULL`; only manually triggered reminders carry a staff `userId`
  - `ErrorReason` is nullable — populated only when `Status = Failed`

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value |
| ---------------------- | ----- |
| **UI Impact**          | No    |
| **Figma URL**          | N/A   |
| **Wireframe Status**   | N/A   |
| **Wireframe Type**     | N/A   |
| **Wireframe Path/URL** | N/A   |
| **Screen Spec**        | N/A   |
| **UXR Requirements**   | N/A   |
| **Design Tokens**      | N/A   |

---

## Applicable Technology Stack

| Layer              | Technology              | Version |
| ------------------ | ----------------------- | ------- |
| Database           | PostgreSQL              | 16+     |
| ORM                | Entity Framework Core   | 9.x     |
| Backend            | ASP.NET Core Web API    | .NET 9  |
| Testing — Unit     | xUnit                   | —       |
| AI/ML              | N/A                     | N/A     |
| Mobile             | N/A                     | N/A     |

> All code and libraries MUST be compatible with versions above.

---

## AI References (AI Tasks Only)

| Reference Type           | Value |
| ------------------------ | ----- |
| **AI Impact**            | No    |
| **AIR Requirements**     | N/A   |
| **AI Pattern**           | N/A   |
| **Prompt Template Path** | N/A   |
| **Guardrails Config**    | N/A   |
| **Model Provider**       | N/A   |

---

## Mobile References (Mobile Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **Mobile Impact**    | No    |
| **Platform Target**  | N/A   |
| **Min OS Version**   | N/A   |
| **Mobile Framework** | N/A   |

---

## Task Overview

Extend the existing `Notifications` table schema to support the manual ad-hoc reminder trigger feature introduced in US_034. The `Notification` entity (DR-015) currently tracks channel, status, sentAt, retryCount, and lastRetryAt. Two new nullable columns are required:

- **`TriggeredBy` (UUID nullable FK → `Users.Id`)**: Identifies the staff member who manually triggered the reminder. `NULL` for automated (system-scheduled) reminders, preserving backward compatibility with US_033 records.
- **`ErrorReason` (varchar(1000) nullable)**: Stores the raw error message or code returned by SendGrid or Twilio when delivery fails. `NULL` for successful deliveries.

An EF Core 9 code-first migration is created to apply these changes with a rollback-safe `Down()` method. A composite index on `(AppointmentId, SentAt DESC)` is added to support efficient debounce queries and last-manual-reminder lookups required by task_002.

---

## Dependent Tasks

- **EP-001/us_008** — `Notification` entity and `Notifications` table must already exist (this task extends that schema)

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| MODIFY | `Notification` domain entity | `Server/src/Domain/Entities/Notification.cs` |
| MODIFY | `NotificationConfiguration` (EF Core Fluent API) | `Server/src/Infrastructure/Persistence/Configurations/NotificationConfiguration.cs` |
| CREATE | EF Core migration `Add_Notification_TriggeredBy_ErrorReason` | `Server/src/Infrastructure/Persistence/Migrations/` |
| MODIFY | `INotificationRepository` | Add `GetLatestSentManualReminderAsync(Guid appointmentId, int withinMinutes)` method signature |
| MODIFY | `NotificationRepository` | Implement `GetLatestSentManualReminderAsync` using the new composite index |

---

## Implementation Plan

1. **`Notification` entity — new properties**:
   ```csharp
   // Server/src/Domain/Entities/Notification.cs
   public Guid? TriggeredBy { get; set; }          // FK to Users.Id; null = automated
   public string? ErrorReason { get; set; }         // Delivery failure reason from provider
   ```

2. **`NotificationConfiguration` — Fluent API additions**:
   ```csharp
   builder.Property(n => n.TriggeredBy)
       .IsRequired(false);

   builder.HasOne<User>()
       .WithMany()
       .HasForeignKey(n => n.TriggeredBy)
       .IsRequired(false)
       .OnDelete(DeleteBehavior.SetNull);

   builder.Property(n => n.ErrorReason)
       .HasMaxLength(1000)
       .IsRequired(false);

   builder.HasIndex(n => new { n.AppointmentId, n.SentAt })
       .HasDatabaseName("IX_Notifications_AppointmentId_SentAt")
       .IsDescending(false, true);   // AppointmentId ASC, SentAt DESC
   ```

3. **EF Core migration** — `Add_Notification_TriggeredBy_ErrorReason`:
   ```csharp
   protected override void Up(MigrationBuilder migrationBuilder)
   {
       migrationBuilder.AddColumn<Guid>(
           name: "TriggeredBy",
           table: "Notifications",
           type: "uuid",
           nullable: true);

       migrationBuilder.AddColumn<string>(
           name: "ErrorReason",
           table: "Notifications",
           type: "character varying(1000)",
           maxLength: 1000,
           nullable: true);

       migrationBuilder.AddForeignKey(
           name: "FK_Notifications_Users_TriggeredBy",
           table: "Notifications",
           column: "TriggeredBy",
           principalTable: "Users",
           principalColumn: "Id",
           onDelete: ReferentialAction.SetNull);

       migrationBuilder.CreateIndex(
           name: "IX_Notifications_AppointmentId_SentAt",
           table: "Notifications",
           columns: new[] { "AppointmentId", "SentAt" },
           descending: new[] { false, true });
   }

   protected override void Down(MigrationBuilder migrationBuilder)
   {
       migrationBuilder.DropIndex(
           name: "IX_Notifications_AppointmentId_SentAt",
           table: "Notifications");

       migrationBuilder.DropForeignKey(
           name: "FK_Notifications_Users_TriggeredBy",
           table: "Notifications");

       migrationBuilder.DropColumn(name: "TriggeredBy", table: "Notifications");
       migrationBuilder.DropColumn(name: "ErrorReason", table: "Notifications");
   }
   ```

4. **`INotificationRepository` extension**:
   ```csharp
   Task<Notification?> GetLatestSentManualReminderAsync(Guid appointmentId, int withinMinutes, CancellationToken ct = default);
   ```

5. **`NotificationRepository` implementation** (uses new index):
   ```csharp
   public async Task<Notification?> GetLatestSentManualReminderAsync(
       Guid appointmentId, int withinMinutes, CancellationToken ct = default)
   {
       var cutoff = DateTime.UtcNow.AddMinutes(-withinMinutes);
       return await _context.Notifications
           .Where(n => n.AppointmentId == appointmentId
                    && n.TriggeredBy != null
                    && n.Status == NotificationStatus.Sent
                    && n.SentAt >= cutoff)
           .OrderByDescending(n => n.SentAt)
           .FirstOrDefaultAsync(ct);
   }
   ```
   - Parameterised LINQ query — no raw SQL concatenation (OWASP A03)

---

## Current Project State

```
Server/
├── src/
│   ├── Domain/
│   │   └── Entities/
│   │       └── Notification.cs                    # MODIFY — add TriggeredBy, ErrorReason
│   └── Infrastructure/
│       └── Persistence/
│           ├── Configurations/
│           │   └── NotificationConfiguration.cs   # MODIFY — Fluent API config + index
│           ├── Migrations/
│           │   └── <new migration file>           # CREATE
│           └── Repositories/
│               └── NotificationRepository.cs      # MODIFY — add GetLatestSentManualReminderAsync
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| MODIFY | `Server/src/Domain/Entities/Notification.cs` | Add `TriggeredBy: Guid?` and `ErrorReason: string?` properties |
| MODIFY | `Server/src/Infrastructure/Persistence/Configurations/NotificationConfiguration.cs` | Add Fluent API rules for nullable FK, max-length constraint, composite index |
| CREATE | `Server/src/Infrastructure/Persistence/Migrations/<timestamp>_Add_Notification_TriggeredBy_ErrorReason.cs` | EF Core migration with `Up()` and rollback-safe `Down()` |
| MODIFY | `Server/src/Application/Notifications/Interfaces/INotificationRepository.cs` | Add `GetLatestSentManualReminderAsync` method signature |
| MODIFY | `Server/src/Infrastructure/Persistence/Repositories/NotificationRepository.cs` | Implement `GetLatestSentManualReminderAsync` using parameterised LINQ |

---

## External References

- [EF Core 9 — Migrations overview](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [EF Core 9 — Indexes (Fluent API)](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)
- [EF Core 9 — Relationships (nullable FK)](https://learn.microsoft.com/en-us/ef/core/modeling/relationships/navigations)
- [PostgreSQL 16 — Partial indexes](https://www.postgresql.org/docs/16/indexes-partial.html)
- [OWASP A03 — Injection (parameterised queries)](https://cheatsheetseries.owasp.org/cheatsheets/DotNet_Security_Cheat_Sheet.html#parameterized-queries)

---

## Build Commands

- EF migration add: `dotnet ef migrations add Add_Notification_TriggeredBy_ErrorReason --project src/Infrastructure --startup-project src/API`
- EF migration apply: `dotnet ef database update --project src/Infrastructure --startup-project src/API`
- EF migration rollback: `dotnet ef database update <PreviousMigrationName> --project src/Infrastructure --startup-project src/API`
- Backend build: `dotnet build` (from `Server/` folder)
- Backend tests: `dotnet test`

---

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] EF Core migration applies cleanly on a fresh PostgreSQL 16 database (`dotnet ef database update`)
- [ ] Migration `Down()` rolls back cleanly without errors
- [ ] `Notifications.TriggeredBy` column is `uuid NULL` with FK constraint referencing `Users.Id`
- [ ] `Notifications.ErrorReason` column is `varchar(1000) NULL`
- [ ] `IX_Notifications_AppointmentId_SentAt` index exists (verified via `\d notifications` in psql)
- [ ] Existing `Notification` records (from US_033 automated reminders) are unaffected — `TriggeredBy` and `ErrorReason` remain `NULL`
- [ ] `GetLatestSentManualReminderAsync` returns correct record within 5-minute window and `null` outside it

---

## Implementation Checklist

- [ ] Add `TriggeredBy: Guid?` and `ErrorReason: string?` properties to `Notification` domain entity
- [ ] Add Fluent API configuration in `NotificationConfiguration`: nullable FK to `Users`, `HasMaxLength(1000)` for `ErrorReason`, composite index `IX_Notifications_AppointmentId_SentAt` (AppointmentId ASC, SentAt DESC)
- [ ] Generate EF Core migration `Add_Notification_TriggeredBy_ErrorReason` via `dotnet ef migrations add`; verify generated `Up()` and `Down()` methods match the specification above
- [ ] Add `GetLatestSentManualReminderAsync` method signature to `INotificationRepository`
- [ ] Implement `GetLatestSentManualReminderAsync` in `NotificationRepository` using parameterised LINQ (no raw SQL)
- [ ] Verify existing automated-reminder Notification rows are unaffected by applying migration against a seeded database
