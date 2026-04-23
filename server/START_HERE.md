# ?? EXACT STEPS TO FIX 401 ERROR - FOLLOW PRECISELY

## Current Situation
- ? Code changes are saved
- ? Application is running with OLD code
- ? DeviceId is not being returned by backend

## What You Must Do NOW

### STEP 1: Test Current State (30 seconds)

Run this to see if backend is using old or new code:

```powershell
.\test-backend-deviceid.ps1
```

**If you see:**
```
? SUCCESS!
Backend IS returning deviceId: auto-12345...
```
? **Skip to Step 3** (restart Angular only)

**If you see:**
```
? FAILED!
Backend is NOT returning deviceId
```
? **Continue to Step 2** (restart backend)

---

### STEP 2: Restart Backend (2 minutes)

#### A. Stop Everything
```powershell
# In Visual Studio: Press Shift+F5 (Stop Debugging)

# In PowerShell:
Get-Process -Name "Propel.Api.Gateway" -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Stop-Process -Force
```

#### B. Rebuild
```powershell
cd server
dotnet clean
dotnet build
```

**WAIT for "Build succeeded" message!**

#### C. Start Backend
```powershell
dotnet run --project Propel.Api.Gateway\Propel.Api.Gateway.csproj
```

**WAIT for "Now listening on: http://localhost:5000"**

#### D. Verify Fix is Active
```powershell
# In NEW PowerShell window:
.\test-backend-deviceid.ps1
```

**Must see:** `? SUCCESS! Backend IS returning deviceId`

**If not, repeat Step 2 from the beginning.**

---

### STEP 3: Restart Angular (1 minute)

```powershell
# Stop current dev server (Ctrl+C in terminal)

# Then:
cd app
npm start
```

**WAIT for "Angular Live Development Server is listening on localhost:4200"**

---

### STEP 4: Test in Browser (1 minute)

1. Open: `http://localhost:4200/auth/login`
2. Press **F12** (DevTools)
3. Go to **Console** tab
4. Login

**You MUST see:**
```
[AuthService] Storing tokens: {
  deviceId: "auto-12345..."  ? THIS LINE MUST BE THERE
}
```

5. Dashboard should load ?

---

## If It STILL Fails

Run the comprehensive restart:

```powershell
.\restart-all.ps1
```

This does EVERYTHING automatically:
- Stops all processes
- Cleans build
- Rebuilds solution
- Starts backend
- Starts frontend  
- Opens browser

---

## Quick Reference

| Command | Purpose |
|---------|---------|
| `.\test-backend-deviceid.ps1` | **Check if backend has the fix** |
| `.\restart-all.ps1` | **Automatic full restart** |
| `.\check-current-state.ps1` | **Detailed diagnostic** |

---

## Timeline

- **Step 1:** 30 seconds (test)
- **Step 2:** 2 minutes (restart backend if needed)
- **Step 3:** 1 minute (restart frontend)
- **Step 4:** 1 minute (test)

**Total: ~5 minutes maximum**

---

## The EXACT Problem

The backend IS compiled with the new code, but:
- Visual Studio debugger is caching old code
- OR old `dotnet.exe` process is still running
- OR build artifacts are stale

**Solution:** Force a clean rebuild and fresh start.

---

## Start Here

```powershell
.\test-backend-deviceid.ps1
```

Follow the output instructions! ??
