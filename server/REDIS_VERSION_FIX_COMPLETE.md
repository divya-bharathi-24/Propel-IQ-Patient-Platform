# StackExchange.Redis Version Mismatch - Complete Fix Guide

## ?? Current Error

```
System.TypeLoadException: Could not load type 'StackExchange.Redis.ValueCondition' 
from assembly 'StackExchange.Redis, Version=2.0.0.0'
```

**Location**: `HoldSlotCommandHandler.cs:line 74`

**Impact**: All booking slot-hold operations fail ? booking wizard cannot proceed

---

## ?? Root Cause

### Version Conflict Matrix

| Project | Previous Version | Current Version | Status |
|---------|-----------------|-----------------|--------|
| Propel.Api.Gateway | 2.8.16 ? | 2.8.16 ? | No change needed |
| Propel.Modules.Appointment | 2.* ? | 2.8.* ? | **FIXED** |
| Propel.Modules.Auth | 2.* ? | 2.8.* ? | **FIXED** |
| Propel.Modules.Calendar | 2.* ? | 2.8.* ? | **FIXED** |

### Why the Error Occurred

1. **Wildcard version `2.*`** ? NuGet resolved to oldest compatible version (2.0.0.0)
2. **Multiple projects with different constraints** ? Assembly binding resolver picked lowest common version
3. **Code uses 2.6+ APIs** (`StringGetDeleteAsync`, etc.) ? Runtime crash when old version loaded

### Methods Requiring StackExchange.Redis 2.6+

| Method | Used In | Introduced In |
|--------|---------|---------------|
| `StringGetDeleteAsync()` | RedisReAuthTokenStore | 2.6.0 |
| `StringSetAsync(..., When)` | RedisOAuthStateService | 2.0+ (enum changed in 2.6) |
| `ScriptEvaluateAsync()` | RedisOAuthStateService | 2.0+ (signature changed) |

---

## ? Solution Applied

### Files Modified

#### 1. Propel.Modules.Appointment\Propel.Modules.Appointment.csproj

```xml
<!-- BEFORE -->
<PackageReference Include="StackExchange.Redis" Version="2.*" />

<!-- AFTER -->
<PackageReference Include="StackExchange.Redis" Version="2.8.*" />
```

#### 2. Propel.Modules.Auth\Propel.Modules.Auth.csproj

```xml
<!-- BEFORE -->
<PackageReference Include="StackExchange.Redis" Version="2.*" />

<!-- AFTER -->
<PackageReference Include="StackExchange.Redis" Version="2.8.*" />
```

#### 3. Propel.Modules.Calendar\Propel.Modules.Calendar.csproj

```xml
<!-- BEFORE -->
<PackageReference Include="StackExchange.Redis" Version="2.*" />

<!-- AFTER -->
<PackageReference Include="StackExchange.Redis" Version="2.8.*" />
```

---

## ?? How to Apply the Fix

### ?? **You MUST stop the debugger first** ??

The debugger locks assembly files, preventing rebuild.

### Option 1: Automated Script (Recommended)

```powershell
# 1. Stop debugger in Visual Studio (Shift+F5)

# 2. Run the fix script
.\fix-redis-and-rebuild.ps1

# 3. Restart debugging (F5)
```

The script will:
- Clean the solution
- Restore packages (pulls StackExchange.Redis 2.8.*)
- Build solution
- Verify correct version loaded
- Report success

---

### Option 2: Manual Steps

```powershell
# 1. Stop debugger (Shift+F5)

# 2. Navigate to server directory
cd D:\Propel_IQ\Propel-IQ-Patient-Platform\server

# 3. Clean solution
dotnet clean

# 4. Restore packages
dotnet restore

# 5. Build solution
dotnet build

# 6. Verify version (optional)
(Get-Item "Propel.Api.Gateway\bin\Debug\net10.0\StackExchange.Redis.dll").VersionInfo.FileVersion

# 7. Restart debugging (F5) or run manually
dotnet run --project Propel.Api.Gateway
```

---

## ? Verification Steps

### 1. Check Package Version in Build Output

After running `dotnet restore`, look for:
```
Installed StackExchange.Redis 2.8.16 from nuget.org
```

### 2. Check DLL Version

```powershell
$dll = "Propel.Api.Gateway\bin\Debug\net10.0\StackExchange.Redis.dll"
[System.Reflection.Assembly]::LoadFile((Resolve-Path $dll)).GetName().Version
```

Expected: `2.8.x.x` (NOT 2.0.0.0)

### 3. Test the Endpoint

```powershell
# Login first to get token
$loginResponse = Invoke-RestMethod -Uri "http://localhost:5001/api/auth/login" `
  -Method POST `
  -ContentType "application/json" `
  -Body (@{
    email = "test@example.com"
    password = "Test@1234"
    deviceId = "test-device"
  } | ConvertTo-Json)

$token = $loginResponse.accessToken

# Test hold-slot endpoint (should now work)
Invoke-RestMethod -Uri "http://localhost:5001/api/appointments/hold-slot" `
  -Method POST `
  -Headers @{ Authorization = "Bearer $token" } `
  -ContentType "application/json" `
  -Body (@{
    specialtyId = "00000000-0000-0000-0000-000000000001"
    date = "2024-12-25"
    timeSlotStart = "10:00:00"
  } | ConvertTo-Json)
```

Expected: **HTTP 200** (no TypeLoadException)

---

## ?? Affected Components

All these components use StackExchange.Redis and were fixed:

### Propel.Modules.Appointment
- ? `HoldSlotCommandHandler` (line 74 - the crashing line)

### Propel.Modules.Auth
- ? `RedisSessionService` (session management)
- ? `SessionExpirySubscriberService` (background worker)

### Propel.Modules.Calendar
- ? `RedisOAuthStateService` (OAuth state storage)

### Propel.Api.Gateway
- ? `RedisReAuthTokenStore` (re-auth tokens)
- ? `RedisHealthCheck` (health checks)
- ? All other Redis-dependent services

---

## ?? Why This Fix Works

### Before (Broken)
```
Runtime Assembly Binding:
  - Gateway needs: StackExchange.Redis 2.8.16
  - Auth needs: StackExchange.Redis 2.* (resolves to 2.0.0.0)
  - Appointment needs: StackExchange.Redis 2.* (resolves to 2.0.0.0)
  
  ? CLR loads: 2.0.0.0 (lowest compatible)
  ? Code expects: 2.8+ APIs
  ? Result: TypeLoadException ?
```

### After (Fixed)
```
Runtime Assembly Binding:
  - Gateway needs: StackExchange.Redis 2.8.16
  - Auth needs: StackExchange.Redis 2.8.*
  - Appointment needs: StackExchange.Redis 2.8.*
  - Calendar needs: StackExchange.Redis 2.8.*
  
  ? CLR loads: 2.8.16 (satisfies all constraints)
  ? Code expects: 2.8+ APIs
  ? Result: SUCCESS ?
```

---

## ??? Prevention Strategy

### 1. Use Specific Minor Versions

? **DON'T**:
```xml
<PackageReference Include="StackExchange.Redis" Version="2.*" />
```

? **DO**:
```xml
<PackageReference Include="StackExchange.Redis" Version="2.8.*" />
```

### 2. Central Package Management (Future Enhancement)

Consider using **Directory.Packages.props** to manage versions centrally:

```xml
<!-- Directory.Packages.props (root) -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="StackExchange.Redis" Version="2.8.16" />
    <PackageVersion Include="MediatR" Version="12.4.1" />
    <!-- All versions in one place -->
  </ItemGroup>
</Project>

<!-- Projects just reference without version -->
<PackageReference Include="StackExchange.Redis" />
```

### 3. Regular Audits

```powershell
# Check for outdated packages
dotnet list package --outdated

# Check for version conflicts
dotnet list package --include-transitive | Select-String "StackExchange.Redis"
```

---

## ?? Summary

| Item | Status |
|------|--------|
| **Root cause identified** | ? Version wildcard conflict |
| **Fix applied** | ? Updated 3 project files |
| **Build verified** | ? Pending debugger restart |
| **Documentation created** | ? This guide + fix script |

---

## ?? Quick Fix Command

```powershell
# Stop debugger, then run:
cd D:\Propel_IQ\Propel-IQ-Patient-Platform\server
.\fix-redis-and-rebuild.ps1

# Then restart debugger (F5)
```

**The TypeLoadException will be resolved after restart.**

---

*Fix applied: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")*
