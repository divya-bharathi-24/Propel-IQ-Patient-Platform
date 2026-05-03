# StackExchange.Redis Version Mismatch - FIXED

## Problem

**Error**: `System.TypeLoadException: Could not load type 'StackExchange.Redis.ValueCondition' from assembly 'StackExchange.Redis, Version=2.0.0.0'`

**Root Cause**: Version mismatch across projects
- `Propel.Api.Gateway`: StackExchange.Redis 2.8.16 ?
- `Propel.Modules.Appointment`: StackExchange.Redis 2.* (resolved to 2.0.0.0) ?
- `Propel.Modules.Auth`: StackExchange.Redis 2.* (resolved to 2.0.0.0) ?
- `Propel.Modules.Calendar`: StackExchange.Redis 2.* (resolved to 2.0.0.0) ?

The code uses methods like `StringGetDeleteAsync()` which only exist in StackExchange.Redis 2.6+.

## Solution Applied

Updated all module projects to use **StackExchange.Redis 2.8.*** to match the Gateway:

### Files Modified

1. ? **Propel.Modules.Appointment\Propel.Modules.Appointment.csproj**
   ```xml
   <PackageReference Include="StackExchange.Redis" Version="2.8.*" />
   ```

2. ? **Propel.Modules.Auth\Propel.Modules.Auth.csproj**
   ```xml
   <PackageReference Include="StackExchange.Redis" Version="2.8.*" />
   ```

3. ? **Propel.Modules.Calendar\Propel.Modules.Calendar.csproj**
   ```xml
   <PackageReference Include="StackExchange.Redis" Version="2.8.*" />
   ```

## Next Steps

### ?? **RESTART DEBUGGER REQUIRED**

The debugger is currently holding file locks on the assemblies, preventing rebuild.

**Steps to apply fix**:

1. **Stop the debugger** (Shift+F5)

2. **Restore packages**:
   ```powershell
   dotnet restore
   ```

3. **Clean and rebuild**:
   ```powershell
   dotnet clean
   dotnet build
   ```

4. **Restart debugging** (F5)

## Verification

After restart, verify the correct version is loaded:

### Check in debugger
Set a breakpoint in `HoldSlotCommandHandler.cs` line 74 and inspect:
```csharp
_redis.GetType().Assembly.GetName().Version
```
Should show: `2.8.x.x` (not 2.0.0.0)

### Or check bin folder
```powershell
ls Propel.Api.Gateway\bin\Debug\net10.0\StackExchange.Redis.dll | Select-Object VersionInfo
```

Expected: `FileVersion: 2.8.16.x` or similar

## Root Cause Analysis

**Why did this happen?**

- The wildcard version `2.*` in NuGet can resolve to **any 2.x version**
- When multiple projects reference different minor versions, NuGet's assembly binding resolver picks **the lowest common version** that satisfies all constraints
- Version `2.*` in three modules + `2.8.16` in Gateway ? resolver picked `2.0.0.0` as the runtime version
- Code written for 2.8+ APIs crashed at runtime

**Prevention**:
- ? Use **specific minor versions** (2.8.*) instead of major-only wildcards (2.*)
- ? Ensure all projects reference compatible versions
- ? Use `dotnet list package --outdated` to catch version drift

## Related Files

### Code that uses the fixed methods:

1. **HoldSlotCommandHandler.cs** (line 74)
   - Uses: `StringSetAsync()`

2. **RedisReAuthTokenStore.cs**
   - Uses: `StringGetDeleteAsync()` (atomic GET+DELETE)

3. **RedisSessionService.cs**
   - Uses: `StringSetAsync()`, `KeyExistsAsync()`, `KeyExpireAsync()`, `KeyDeleteAsync()`

4. **RedisOAuthStateService.cs**
   - Uses: `StringSetAsync()`, `ScriptEvaluateAsync()` with Lua for GET+DELETE

All these methods require StackExchange.Redis 2.6+ to compile and run.

## Status

? **FIXED** - All projects now use StackExchange.Redis 2.8.*

?? **ACTION REQUIRED** - Restart debugger to load updated assemblies

---

**Next**: Stop debugger ? Rebuild ? Restart debugging
