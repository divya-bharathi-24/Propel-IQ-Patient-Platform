# ?? ACTION PLAN - Fix Session Expired Error

## What I Just Did

? Added **debug logging** to:
- `RefreshTokenCommandHandler.cs` - Tracks refresh flow
- `InMemoryRedisSessionService.cs` - Tracks session operations

## What You Need To Do (5 Minutes)

### Step 1: Stop Debugger (30 seconds)
Press **Shift+F5** in Visual Studio

### Step 2: Rebuild (1 minute)
```powershell
cd server
dotnet build
```

Wait for "Build succeeded"

### Step 3: Start Backend (30 seconds)
```powershell
dotnet run --project Propel.Api.Gateway\Propel.Api.Gateway.csproj
```

Wait for "Now listening on: http://localhost:5000"

### Step 4: Run Test Script (1 minute)
In a **NEW PowerShell window**:

```powershell
.\test-refresh-flow.ps1
```

### Step 5: Read The Backend Logs (2 minutes)

The script will:
1. Login and get deviceId
2. Try to refresh token
3. Show you where to look in backend console

**Go to your backend console window and look for the debug logs!**

---

## What The Debug Logs Will Reveal

### Example 1: DeviceId Mismatch (Most Likely)

**Login creates session:**
```
[SESSION DEBUG] Session SET - Key: session:abc-123:auto-xyz-456
```

**Refresh tries to use different deviceId:**
```
[REFRESH DEBUG] StoredDeviceId: auto-xyz-456, RequestDeviceId: auto-abc-789
[SESSION DEBUG] Session NOT FOUND - Key: session:abc-123:auto-abc-789
[SESSION DEBUG] Available keys: session:abc-123:auto-xyz-456
```

**Problem**: Angular is sending wrong deviceId.

**Solution**: The backend login response must include deviceId so Angular knows what to use.

---

### Example 2: Session Never Created

**No session log during login:**
```
(no [SESSION DEBUG] Session SET message)
```

**Refresh fails:**
```
[SESSION DEBUG] Session NOT FOUND - Available keys: (empty)
```

**Problem**: LoginCommandHandler isn't calling `SetAsync`.

**Solution**: Check LoginCommandHandler implementation.

---

### Example 3: Session Expired

**Session created:**
```
[SESSION DEBUG] Session SET - ExpiresAt: 10:00:00
```

**Much later, refresh fails:**
```
[SESSION DEBUG] Session EXPIRED - Was valid until: 10:00:00
Current time: 10:16:00
```

**Problem**: 15 minutes passed, session expired.

**Solution**: This is expected behavior, user needs to re-login.

---

## After Running The Test

**Share this information:**

1. The **test script output** (what it printed)
2. The **backend console logs** with [DEBUG] messages
3. Screenshot if possible

With those logs, I can tell you EXACTLY what's wrong and how to fix it! ??

---

## Files Ready To Use

| File | Purpose |
|------|---------|
| `test-refresh-flow.ps1` | **Run this first** - Tests the flow |
| `DEBUG_SESSION_LOGS.md` | Explains what to look for in logs |
| `START_HERE.md` | Full restart instructions |

---

## Quick Commands

```powershell
# Stop everything
Get-Process -Name "dotnet" | Stop-Process -Force

# Rebuild
cd server
dotnet build

# Start backend
dotnet run --project Propel.Api.Gateway\Propel.Api.Gateway.csproj

# In NEW window - run test
.\test-refresh-flow.ps1
```

Then **copy the debug logs** from the backend console! ??
