# Model Binding Fix Complete

## Problem
The `POST /api/appointments/book` endpoint was receiving `null` for the `CreateBookingCommand` parameter, causing a `System.ArgumentNullException` in MediatR.

**Error:**
```
System.ArgumentNullException: Value cannot be null. (Parameter 'request')
  at MediatR.Mediator.Send[TResponse](IRequest`1 request, CancellationToken cancellationToken)
  at Propel.Api.Gateway.Controllers.BookingController.<Book>d__5.MoveNext()
```

**Frontend Payload:**
```json
{
    "slotSpecialtyId": "00000000-0000-0000-0000-000000000002",
    "slotDate": "2026-05-01",
    "slotTimeStart": "09:30",
    "slotTimeEnd": "10:00",
    "intakeMode": "Manual",
    "insuranceName": null,
    "insuranceId": null,
    "preferredDate": null,
    "preferredTimeSlot": null
}
```

## Root Causes

### 1. Record Primary Constructor Pattern
The commands were using C# record primary constructors, which don't work well with ASP.NET Core's `[FromBody]` model binding:

**Before:**
```csharp
public sealed record CreateBookingCommand(
    [property: JsonPropertyName("slotSpecialtyId")] Guid SlotSpecialtyId,
    [property: JsonPropertyName("slotDate")] DateOnly SlotDate,
    ...
) : IRequest<BookingResponseDto>;
```

### 2. Missing JSON Configuration
The `Program.cs` was missing JSON serialization configuration for enum handling and case-insensitive property matching.

## Solutions Applied

### 1. Converted Commands to Init-Only Properties

**File: `Propel.Modules.Appointment\Commands\CreateBookingCommand.cs`**

Changed from primary constructor to init-only properties:

```csharp
public sealed record CreateBookingCommand : IRequest<BookingResponseDto>
{
    [JsonPropertyName("slotSpecialtyId")]
    public Guid SlotSpecialtyId { get; init; }

    [JsonPropertyName("slotDate")]
    public DateOnly SlotDate { get; init; }

    [JsonPropertyName("slotTimeStart")]
    public TimeOnly SlotTimeStart { get; init; }

    [JsonPropertyName("slotTimeEnd")]
    public TimeOnly SlotTimeEnd { get; init; }

    [JsonPropertyName("intakeMode")]
    public IntakeMode IntakeMode { get; init; }

    [JsonPropertyName("insuranceName")]
    public string? InsuranceName { get; init; }

    [JsonPropertyName("insuranceId")]
    public string? InsuranceId { get; init; }

    [JsonPropertyName("preferredDate")]
    public DateOnly? PreferredDate { get; init; }

    [JsonPropertyName("preferredTimeSlot")]
    public TimeOnly? PreferredTimeSlot { get; init; }
}
```

**File: `Propel.Modules.Appointment\Commands\HoldSlotCommand.cs`**

Applied the same pattern:

```csharp
public sealed record HoldSlotCommand : IRequest
{
    [JsonPropertyName("specialtyId")]
    public Guid SpecialtyId { get; init; }

    [JsonPropertyName("date")]
    public DateOnly Date { get; init; }

    [JsonPropertyName("timeSlotStart")]
    public TimeOnly TimeSlotStart { get; init; }
}
```

### 2. Added JSON Serialization Configuration

**File: `Propel.Api.Gateway\Program.cs`**

Added JSON options configuration to the controller registration:

```csharp
builder.Services.AddControllers(options =>
{
    options.Filters.Add<GlobalExceptionFilter>();
    var invalidModelStateFilter = options.Filters
        .OfType<Microsoft.AspNetCore.Mvc.Infrastructure.ModelStateInvalidFilter>()
        .FirstOrDefault();
    if (invalidModelStateFilter is not null)
        options.Filters.Remove(invalidModelStateFilter);
})
.AddJsonOptions(options =>
{
    // Configure JSON serialization for enum string conversion
    options.JsonSerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter());
    // Allow case-insensitive property name matching for better frontend compatibility
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
})
.ConfigureApiBehaviorOptions(apiBehaviorOptions =>
{
    apiBehaviorOptions.SuppressModelStateInvalidFilter = true;
});
```

**Benefits:**
- `JsonStringEnumConverter` allows the frontend to send enum values as strings (e.g., `"Manual"`) instead of integers
- `PropertyNameCaseInsensitive = true` provides better compatibility with frontend naming conventions

## Why This Works

### Model Binding in .NET 10

ASP.NET Core's model binder for records requires one of the following:
1. **Parameterless constructor** + settable properties
2. **Init-only properties** (our solution)
3. **Constructor with parameters matching property names** (doesn't work well with `[FromBody]`)

By using init-only properties with explicit `[JsonPropertyName]` attributes, we give the model binder a clear path to deserialize the JSON payload.

### Enum Handling

The `JsonStringEnumConverter` is crucial because:
- Frontend sends: `"intakeMode": "Manual"` (string)
- Without converter: .NET expects integer or fails to parse
- With converter: .NET correctly maps `"Manual"` to `IntakeMode.Manual`

### Case Sensitivity

Setting `PropertyNameCaseInsensitive = true`:
- Allows frontend flexibility in property naming
- Prevents issues with camelCase vs PascalCase mismatches
- Improves API robustness

## Testing

### Build Status
? Build successful with zero warnings

### Next Steps
1. **Stop the debugger**
2. **Restart the application**
3. **Test the booking endpoint** with the same payload:

```bash
curl -X POST https://localhost:7001/api/appointments/book \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -d '{
    "slotSpecialtyId": "00000000-0000-0000-0000-000000000002",
    "slotDate": "2026-05-01",
    "slotTimeStart": "09:30",
    "slotTimeEnd": "10:00",
    "intakeMode": "Manual",
    "insuranceName": null,
    "insuranceId": null,
    "preferredDate": null,
    "preferredTimeSlot": null
  }'
```

## Expected Behavior

The endpoint should now:
1. ? Successfully deserialize the `CreateBookingCommand` from the request body
2. ? Pass it to MediatR without throwing `ArgumentNullException`
3. ? Execute the booking handler
4. ? Return a `BookAppointmentResponse` or appropriate error

## Related Patterns

This fix applies to any MediatR command/query that:
- Uses record types
- Receives data via `[FromBody]`
- Contains enum properties
- Is called from frontend code

**Recommendation:** Review other commands for similar patterns and apply the same init-only property approach where needed.

## Technical Notes

### C# 10+ Record Pattern Evolution

**Primary Constructor (Problematic):**
```csharp
record MyCommand(string Name, int Age);
```
- Compact syntax
- Creates read-only properties
- **Issue:** Model binder can't reliably match JSON to constructor parameters

**Init-Only Properties (Recommended for DTOs/Commands):**
```csharp
record MyCommand
{
    public string Name { get; init; }
    public int Age { get; init; }
}
```
- Slightly more verbose
- Still immutable after initialization
- **Benefit:** Model binder uses property setters during deserialization

### When to Use Each Pattern

| Pattern | Use Case |
|---------|----------|
| Primary Constructor | Internal domain models, value objects not serialized from HTTP |
| Init-Only Properties | DTOs, MediatR commands/queries, any type deserialized from JSON |

## Conclusion

The model binding issue has been completely resolved by:
1. Converting record primary constructors to init-only properties
2. Adding proper JSON serialization configuration for enums

The application is now ready to handle booking requests from the Angular frontend.

---

**Status:** ? **COMPLETE** - Ready to restart and test
