# ?? DEBUG SESSION EXPIRED ERROR

## What Just Happened

I added **comprehensive debug logging** to track exactly where the "session_expired" error is coming from.

## The Error

```
System.UnauthorizedAccessException: session_expired
  at RefreshTokenCommandHandler.Handle() line 87
```

Line 87 is the Redis/InMemory session check that's failing.

## Debug Logging Added

### 1. RefreshTokenCommandHandler
- Logs when refresh starts with deviceId
- Logs token lookup result
- Logs session check parameters (userId + deviceId)
- Logs session check result (true/false)
- Logs why session check failed

### 2. InMemoryRedisSessionService  
- Logs every session SET operation with key, userId, deviceId, expiry
- Logs every session EXISTS check with the lookup key
- Logs if session is FOUND, NOT FOUND, or EXPIRED
- Shows ALL available session keys when lookup fails

## What To Do Now

### Step 1: Stop the Debugger
Press **Shift+F5** in Visual Studio

### Step 2: Rebuild
```powershell
cd server
dotnet build
```

### Step 3: Start Fresh
```powershell
dotnet run --project Propel.Api.Gateway\Propel.Api.Gateway.csproj
```

### Step 4: Watch the Logs CAREFULLY

When you login, you'll see:

```
[SESSION DEBUG] Session SET - Key: session:{userId}:{deviceId}, ...
```

This tells you what session was created.

When refresh fails, you'll see:

```
[REFRESH DEBUG] Starting refresh. DeviceId: {deviceId}
[REFRESH DEBUG] Found token. StoredDeviceId: {deviceId1}, RequestDeviceId: {deviceId2}
[REFRESH DEBUG] Checking Redis session. DeviceId: {deviceId}
[SESSION DEBUG] Session EXISTS check - Key: session:{userId}:{deviceId}
[SESSION DEBUG] Session NOT FOUND - Available keys: {list of keys}
```

## What The Logs Will Tell Us

### Scenario 1: DeviceId Mismatch
```
[SESSION DEBUG] Session SET - Key: session:123:auto-abc
[REFRESH DEBUG] RequestDeviceId: auto-xyz
[SESSION DEBUG] Session NOT FOUND - Key: session:123:auto-xyz
Available keys: session:123:auto-abc
```

**Problem**: Angular is sending a different deviceId than what was used during login.

**Fix**: Backend must return the deviceId in login response.

### Scenario 2: Session Not Created
```
[SESSION DEBUG] Session SET - (no log)
[REFRESH DEBUG] RequestDeviceId: auto-abc
[SESSION DEBUG] Session NOT FOUND - Key: session:123:auto-abc
Available keys: (empty)
```

**Problem**: Login handler isn't creating the session.

**Fix**: Check LoginCommandHandler.

### Scenario 3: Session Expired
```
[SESSION DEBUG] Session SET - ExpiresAt: 12:00:00
[SESSION DEBUG] Session EXPIRED - Was valid until: 12:00:00
```

**Problem**: 15 minutes passed since login.

**Fix**: Expected behavior, re-login required.

## Quick Test Script

I'll create a script that shows the exact problem:

```powershell
# test-refresh-flow.ps1
.\test-backend-deviceid.ps1

Write-Host ""
Write-Host "Now watch the backend console logs for:" -ForegroundColor Yellow
Write-Host "1. [SESSION DEBUG] Session SET - when you login" -ForegroundColor White
Write-Host "2. [REFRESH DEBUG] - when refresh is called" -ForegroundColor White
Write-Host "3. [SESSION DEBUG] Session EXISTS check - showing the mismatch" -ForegroundColor White
```

## Expected Next Steps

Once you run with the new logging:

1. **Login** ? Watch for session SET log with deviceId
2. **Navigate to dashboard** ? Interceptor tries to refresh
3. **Error occurs** ? Logs show EXACTLY why

Then we'll know:
- ? Is the session being created?
- ? What deviceId was used?
- ? What deviceId is Angular sending?
- ? Why doesn't the session match?

## The Final Fix

Based on what we see in the logs, the fix will be ONE of:

1. **Backend not returning deviceId** ? Already fixed in code, needs restart
2. **Angular not storing deviceId** ? Already fixed in code, needs restart  
3. **DeviceId mismatch in token** ? Need to debug JWT generation
4. **Session not persisting** ? Need to check InMemoryRedisSessionService

The debug logs will tell us EXACTLY which one! ??
