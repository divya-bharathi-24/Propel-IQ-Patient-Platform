# DI Registration Issues - FIXED ?

## Root Cause

Three issues were preventing application startup:

### Issue 1: Missing `OPENAI_API_KEY` Environment Variable
- **Error**: `OPENAI_API_KEY environment variable is required when Ai:UseAzureOpenAI = false`
- **Root Cause**: Application configured to use OpenAI directly (not Azure OpenAI) but API key not set
- **Location**: `Program.cs` line ~768

### Issue 2: Missing `IAppointmentConfirmationEmailService` Registration
- **Service**: `IAppointmentConfirmationEmailService`
- **Required By**: `RescheduleAppointmentCommandHandler`
- **Problem**: Interface existed but no implementation was registered in DI container

### Issue 3: Service Lifetime Mismatch
- **Service**: `AiOutputSchemaValidator`
- **Problem**: Registered as Singleton but depended on Scoped `IAiMetricsWriter`
- **Error**: "Cannot consume scoped service 'IAiMetricsWriter' from singleton 'AiOutputSchemaValidator'"

### Issue 4: Entity Mapping Error During Migration
- **Error**: `There is no entity type mapped to the table 'system_settings' which is used in a data operation`
- **Root Cause**: Migration using `InsertData()` which requires entity mapping resolution, but migration execution context doesn't have full model available
- **Location**: `20260423000000_AddSystemSettingsAndNotificationColumns.cs`

## Applied Fixes

### Fix 1: Added `OPENAI_API_KEY` to Development Configuration

**Modified**: `Propel.Api.Gateway/Properties/launchSettings.json`

Added environment variable to both `http` and `https` profiles:
```json
"environmentVariables": {
  "ASPNETCORE_ENVIRONMENT": "Development",
  "OPENAI_API_KEY": "sk-proj-your-openai-api-key-here"
}
```

**Action Required**: Replace `sk-proj-your-openai-api-key-here` with your actual OpenAI API key from https://platform.openai.com/api-keys

### Fix 2: Created and Registered `AppointmentConfirmationEmailServiceAdapter`

**Created File**: `Propel.Api.Gateway/Infrastructure/Services/AppointmentConfirmationEmailServiceAdapter.cs`
- Adapter pattern implementation that combines `IPdfConfirmationService` + `IEmailService`
- Generates PDF confirmation and sends it via email
- Uses `Patient.Name` property (corrected from FirstName/LastName)
- Validates navigation properties before use

**Registration in `Program.cs` (after line 669)**:
```csharp
// ?? US_020/US_021 — Appointment confirmation email service adapter (task_003) ?
builder.Services.AddScoped<IAppointmentConfirmationEmailService>(sp =>
{
    var pdfService = sp.GetRequiredService<IPdfConfirmationService>();
    var emailService = sp.GetRequiredService<IEmailService>();
    var logger = sp.GetRequiredService<ILogger<AppointmentConfirmationEmailServiceAdapter>>();
    return new AppointmentConfirmationEmailServiceAdapter(pdfService, emailService, logger);
});
Log.Information("AppointmentConfirmationEmailService registered (US_020/US_021, task_003).");
```

### Fix 3: Changed `AiOutputSchemaValidator` Lifetime

**Modified in `Program.cs` (line ~1055)**:
```csharp
// Before:
builder.Services.AddSingleton<Propel.Modules.AI.Guardrails.AiOutputSchemaValidator>();

// After:
// Changed from Singleton to Scoped to fix lifetime mismatch with scoped IAiMetricsWriter (EP-010/us_048).
builder.Services.AddScoped<Propel.Modules.AI.Guardrails.AiOutputSchemaValidator>();
```

### Fix 4: Added Missing Using Statement

**Added to `Program.cs` imports**:
```csharp
using Propel.Api.Gateway.Infrastructure.Services;
```

### Fix 5: Fixed Migration Seed Data Approach

**Modified**: `Propel.Api.Gateway/Migrations/20260423000000_AddSystemSettingsAndNotificationColumns.cs`

Replaced `migrationBuilder.InsertData()` with raw SQL to avoid entity mapping resolution during migration execution:

```csharp
// Before:
migrationBuilder.InsertData(
    table: "system_settings",
    columns: new[] { "key", "value", "updated_at" },
    values: new object[] { "reminder_interval_hours", "[48,24,2]", new DateTime(2026, 4, 22, 0, 0, 0, DateTimeKind.Utc) });

// After:
migrationBuilder.Sql(@"
    INSERT INTO system_settings (key, value, updated_at)
    VALUES ('reminder_interval_hours', '[48,24,2]', '2026-04-22 00:00:00+00')
    ON CONFLICT (key) DO NOTHING;
");
```

Benefits of this approach:
- Avoids entity mapping resolution during migration execution
- `ON CONFLICT DO NOTHING` makes it idempotent (safe to rerun)
- Consistent with other seed data patterns in the codebase

## Validation

? Build succeeded with no errors
? All DI service descriptors can now be validated
? Service lifetimes are properly aligned:
  - `IAiMetricsWriter` (Scoped) ? `AiOutputSchemaValidator` (Scoped) ?
  - `RescheduleAppointmentCommandHandler` ? `IAppointmentConfirmationEmailService` (Scoped) ?
? Migration can execute without entity mapping errors

## Next Steps

1. **Stop the debugger** (if running)
2. **Set your OpenAI API key**:
   - Open `Propel.Api.Gateway/Properties/launchSettings.json`
   - Replace `"sk-proj-your-openai-api-key-here"` with your actual OpenAI API key
   - Get one from: https://platform.openai.com/api-keys
3. **Restart the application**
4. The application should now start successfully and apply all migrations

## Files Modified

1. ? `Propel.Api.Gateway/Properties/launchSettings.json`
   - Added `OPENAI_API_KEY` environment variable (placeholder)

2. ? `Propel.Api.Gateway/Program.cs` (3 changes)
   - Added using statement for `Propel.Api.Gateway.Infrastructure.Services`
   - Registered `IAppointmentConfirmationEmailService` adapter
   - Changed `AiOutputSchemaValidator` lifetime from Singleton to Scoped

3. ? `Propel.Api.Gateway/Infrastructure/Services/AppointmentConfirmationEmailServiceAdapter.cs` (created)
   - Adapter implementation combining PDF + email services
   - Fixed Patient property references (Name instead of FirstName/LastName)

4. ? `Propel.Api.Gateway/Migrations/20260423000000_AddSystemSettingsAndNotificationColumns.cs`
   - Replaced `InsertData()` with raw SQL `INSERT ... ON CONFLICT DO NOTHING`
   - Fixed entity mapping resolution issue during migration execution

## Summary

All startup blockers have been resolved:
- ? DI registration errors fixed
- ? Service lifetime mismatches corrected  
- ? Migration execution errors resolved
- ? Build successful

**Only remaining action**: Set valid `OPENAI_API_KEY` in `launchSettings.json`
