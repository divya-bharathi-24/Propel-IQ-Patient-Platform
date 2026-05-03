# EF Core Relationship Mapping Fix - COMPLETE

## Problem

The booking API was failing with a `DbUpdateException` when trying to save a Notification record:

```
PostgresException: 42703: column "appointment_id1" of relation "notifications" does not exist
POSITION: 48
```

**Error Location:** `AppDbContext.SaveChangesAsync()` line 156, called from `BookingConfirmedEventHandler.Handle()` line 78.

## Root Cause

The issue was caused by **conflicting EF Core relationship mappings** between `Appointment` and `Notification` entities:

### The Conflict

1. **`Notification` entity** had an explicit foreign key property:
   ```csharp
   public Guid? AppointmentId { get; set; }
   ```

2. **`Appointment` entity** had a collection navigation property:
   ```csharp
   public ICollection<Notification> Notifications { get; set; } = [];
   ```

3. **`NotificationConfiguration`** was setting up a relationship WITHOUT connecting to the collection:
   ```csharp
   builder.HasOne<Appointment>()
          .WithMany()  // ? No parameter - creates shadow FK!
          .HasForeignKey(n => n.AppointmentId)
          .IsRequired(false)
          .OnDelete(DeleteBehavior.Restrict);
   ```

### What Went Wrong

When EF Core saw:
- The explicit `AppointmentId` property in `Notification`
- The `Notifications` collection in `Appointment`  
- The `.WithMany()` call **without a parameter** in the configuration

It thought these were **two separate relationships** and tried to create:
1. `appointment_id` - for the explicit property
2. `appointment_id1` - as a shadow property for the collection

But the database only has `appointment_id`, causing the insert to fail.

## The Fix

Changed the relationship configuration to **explicitly connect** the collection navigation:

```csharp
// ? BEFORE - Creates shadow property
builder.HasOne<Appointment>()
       .WithMany()  // No parameter!
       .HasForeignKey(n => n.AppointmentId)
       ...

// ? AFTER - Uses the existing collection
builder.HasOne<Appointment>()
       .WithMany(a => a.Notifications)  // Connect to collection!
       .HasForeignKey(n => n.AppointmentId)
       ...
```

### Why This Works

By passing `a => a.Notifications` to `.WithMany()`, we tell EF Core:
- "This `AppointmentId` foreign key is used BY the `Notifications` collection in `Appointment`"
- "Don't create a shadow property - use the explicit `AppointmentId` property"
- Both the explicit FK and the collection navigation now point to the **same relationship**

## Changes Made

**File: `Propel.Api.Gateway\Data\Configurations\NotificationConfiguration.cs`**

```diff
- // Configured without navigation property on Notification side.
+ // Configured WITH the Appointment.Notifications collection navigation to prevent
+ // EF Core from creating a shadow AppointmentId1 property.
  builder.HasOne<Appointment>()
-        .WithMany()
+        .WithMany(a => a.Notifications)
         .HasForeignKey(n => n.AppointmentId)
         .IsRequired(false)
         .OnDelete(DeleteBehavior.Restrict);
```

## EF Core Relationship Patterns

### Pattern 1: One-to-Many WITHOUT Navigation on "One" Side
```csharp
// Notification ? Patient (no collection on Patient)
builder.HasOne(n => n.Patient)
       .WithMany()  // ? OK - Patient doesn't have Notifications collection
       .HasForeignKey(n => n.PatientId);
```

### Pattern 2: One-to-Many WITH Navigation on "One" Side
```csharp
// Notification ? Appointment (Appointment HAS Notifications collection)
builder.HasOne<Appointment>()
       .WithMany(a => a.Notifications)  // ? Must specify collection!
       .HasForeignKey(n => n.AppointmentId);
```

### Pattern 3: One-to-Many with BOTH Navigation Properties
```csharp
// Full bidirectional relationship
builder.HasOne(n => n.Appointment)  // Navigation on Notification
       .WithMany(a => a.Notifications)  // Collection on Appointment
       .HasForeignKey(n => n.AppointmentId);
```

## Testing the Fix

### Build Status
? Build successful with zero errors

### Database State
The database schema is correct:
- `notifications.appointment_id` column exists
- No `appointment_id1` column (shadow property eliminated)

### Next Steps
1. **Stop the debugger**
2. **Restart the application**
3. **Test the booking flow end-to-end**:
   ```json
   POST /api/appointments/book
   {
       "slotSpecialtyId": "...",
       "slotDate": "2026-05-01",
       "slotTimeStart": "09:30",
       "slotTimeEnd": "10:00",
       "intakeMode": "Manual",
       "insuranceName": null,
       "insuranceId": null
   }
   ```

The booking should now:
1. ? Create the `Appointment` record
2. ? Create the `Notification` records (Email + SMS)
3. ? Save both successfully to the database
4. ? Return a success response with booking confirmation

## Common EF Core Relationship Pitfalls

### ? Pitfall 1: Forgetting the Collection Parameter
```csharp
// WRONG - Creates shadow FK
.WithMany()

// RIGHT
.WithMany(a => a.Notifications)
```

### ? Pitfall 2: Mismatched Relationships
```csharp
// Entity A has: public ICollection<B> Items { get; set; }
// Configuration says: .WithMany() // ? Doesn't use Items

// Should be: .WithMany(a => a.Items) // ? Uses Items
```

### ? Pitfall 3: Duplicate Relationships
```csharp
// If you configure the same relationship twice with different settings,
// EF Core will create shadow properties. Configure each relationship ONCE.
```

## Key Takeaways

1. **Always connect collection navigations** when they exist on the principal entity
2. **`.WithMany()` without parameters** means "no collection navigation" - creates shadow FK
3. **`.WithMany(x => x.Collection)`** means "use this collection" - connects to existing FK
4. **Check both sides** of a relationship before configuring in Fluent API
5. **Shadow properties** (like `AppointmentId1`) indicate a configuration mismatch

## Related Documentation

- [EF Core Relationships Overview](https://learn.microsoft.com/en-us/ef/core/modeling/relationships)
- [EF Core Shadow Properties](https://learn.microsoft.com/en-us/ef/core/modeling/shadow-properties)
- [Configuring One-to-Many Relationships](https://learn.microsoft.com/en-us/ef/core/modeling/relationships/one-to-many)

---

**Status:** ? **FIXED** - Ready to restart and test booking flow

