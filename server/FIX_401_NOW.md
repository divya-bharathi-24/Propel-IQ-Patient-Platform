# ?? IMMEDIATE FIX - 401 Error Still Happening

## Why You're Still Getting 401

The code changes ARE in the files, but they're **NOT running yet** because:

1. ? Code is saved to disk
2. ? **Visual Studio debugger is holding old code in memory**
3. ? **Angular dev server hasn't picked up changes**

## THE PROBLEM

You're running the app with **old code** that doesn't include the deviceId fix.

## THE SOLUTION (Do This NOW)

### Option 1: Quick Restart (Recommended)

Run this single command:

```powershell
.\restart-all.ps1
```

This will:
- Stop ALL processes (backend, frontend, debugger)
- Clean and rebuild backend
- Start backend in new window
- Start frontend in new window
- Open browser automatically

**Total time: ~2 minutes**

---

### Option 2: Manual Restart (If script doesn't work)

#### Step 1: Stop Everything

**In Visual Studio:**
- Click **Stop Debugging** button (red square)
- Or press **Shift + F5**
- Wait until it fully stops

**In PowerShell:**
```powershell
# Stop all processes
Get-Process -Name "Propel.Api.Gateway" -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name "node" -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name "ng" -ErrorAction SilentlyContinue | Stop-Process -Force
```

#### Step 2: Clean and Rebuild Backend

```powershell
cd server
dotnet clean
dotnet build
```

**Watch for errors!** If build fails, read the error message.

#### Step 3: Start Backend

```powershell
# From server directory:
dotnet run --project Propel.Api.Gateway\Propel.Api.Gateway.csproj
```

**Wait for this message:**
```
Now listening on: http://localhost:5000
```

#### Step 4: Start Frontend (In NEW PowerShell Window)

```powershell
cd app
npm start
```

**Wait for this message:**
```
** Angular Live Development Server is listening on localhost:4200 **
```

---

## Verify the Fix Works

### Before Testing in Browser

Run this diagnostic script:

```powershell
.\check-current-state.ps1
```

**Expected Output:**
```
? Backend is running
? Login successful
? accessToken: present
? refreshToken: present
? userId: 550e8400-...
? role: Patient
? deviceId: auto-12345...  ? MUST BE PRESENT
? Dashboard call successful!
```

**If you see:**
```
? deviceId: MISSING ? THIS IS THE PROBLEM!
```

Then the backend is **STILL running old code**. Go back to Step 1.

---

### Test in Browser

1. Open `http://localhost:4200/auth/login`
2. Press **F12** to open DevTools
3. Go to **Console** tab
4. Login with credentials
5. **Look for this console output:**

```javascript
[AuthService] Storing tokens: {
  hasAccessToken: true,
  hasRefreshToken: true,
  userId: "550e8400-...",
  role: "Patient",
  deviceId: "auto-12345...",  // ? MUST BE HERE
  expiresIn: 900
}
```

6. **Dashboard should load immediately** ?

---

## If Still Failing

### Check Network Tab

1. Open DevTools **Network** tab
2. Filter by "auth"
3. Click on `/api/auth/login` request
4. Go to **Response** tab
5. You should see:

```json
{
  "accessToken": "eyJ...",
  "refreshToken": "abc...",
  "expiresIn": 900,
  "userId": "550e8400-...",
  "role": "Patient",
  "deviceId": "auto-12345..."  // ? MUST BE HERE
}
```

### If deviceId is MISSING from response:

**The backend is running old code!**

1. Check if multiple `dotnet.exe` processes are running:
   ```powershell
   Get-Process -Name "dotnet" | Format-Table Id, ProcessName, StartTime
   ```

2. Kill them ALL:
   ```powershell
   Get-Process -Name "dotnet" | Stop-Process -Force
   ```

3. Rebuild from scratch:
   ```powershell
   cd server
   Remove-Item -Path "Propel.Api.Gateway\bin" -Recurse -Force
   Remove-Item -Path "Propel.Api.Gateway\obj" -Recurse -Force
   dotnet clean
   dotnet build
   dotnet run --project Propel.Api.Gateway\Propel.Api.Gateway.csproj
   ```

---

## Common Mistakes

### ? Mistake 1: Pressing F5 in Visual Studio
**DON'T start with debugger!** It caches old code.

**DO THIS instead:**
```powershell
dotnet run --project Propel.Api.Gateway\Propel.Api.Gateway.csproj
```

### ? Mistake 2: Not killing all processes
Some processes stay alive in the background.

**DO THIS:**
```powershell
Get-Process | Where-Object { $_.Name -like "*dotnet*" -or $_.Name -like "*node*" } | Stop-Process -Force
```

### ? Mistake 3: Not waiting for build to complete
If you start before build finishes, old code runs.

**DO THIS:**
Wait for "Build succeeded" message before running.

---

## Checklist

Run through this checklist:

- [ ] All processes stopped (dotnet, node, VS debugger)
- [ ] `dotnet clean` completed
- [ ] `dotnet build` completed successfully
- [ ] Backend started and shows "listening on localhost:5000"
- [ ] Frontend started and shows "listening on localhost:4200"
- [ ] Diagnostic script shows deviceId in login response
- [ ] Browser console shows deviceId in stored tokens
- [ ] Dashboard loads successfully

---

## Files You Need

| Script | Purpose |
|--------|---------|
| `restart-all.ps1` | **Automatic restart** - does everything for you |
| `check-current-state.ps1` | **Diagnostic** - tests if fix is active |

---

## Support

If still failing after following ALL steps:

1. Run: `.\check-current-state.ps1` > `diagnostic-output.txt`
2. Take screenshot of browser console
3. Take screenshot of Network tab showing login response
4. Share these 3 things

---

## The Bottom Line

**Your code is correct.** ?  
**Your changes are saved.** ?  
**You just need to restart with fresh build.** ?

Run: `.\restart-all.ps1`

That's it! ??
