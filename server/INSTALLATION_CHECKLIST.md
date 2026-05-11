# ? File Locking Fix - Installation Checklist

Run through this checklist to ensure everything is set up correctly:

## Pre-Installation Check
- [ ] Close Visual Studio completely
- [ ] Stop all running dotnet processes
- [ ] Navigate to repository root directory

## Installation Steps

### Step 1: Verify Files Were Created
Run this command:
```powershell
.\verify-file-locking-fix.ps1
```

Expected files in `server/` directory:
- [ ] `Directory.Build.props`
- [ ] `.editorconfig`
- [ ] `fix-file-locking.ps1`
- [ ] `safe-build.ps1`
- [ ] `FILE_LOCKING_FIX_COMPLETE.md`
- [ ] `QUICK_FIX_FILE_LOCKING.md`

Expected files in root directory:
- [ ] `start-dev.ps1` (updated)
- [ ] `stop-dev.ps1` (updated)
- [ ] `verify-file-locking-fix.ps1`
- [ ] `FILE_LOCKING_RESOLUTION_SUMMARY.md`
- [ ] This checklist

### Step 2: Test Emergency Fix
```powershell
cd server
.\fix-file-locking.ps1
```

Should show:
- [ ] "All checks passed!" or minimal warnings

### Step 3: Test Safe Build
```powershell
cd server
.\safe-build.ps1
```

Should complete with:
- [ ] "Build completed successfully!"
- [ ] No file locking errors
- [ ] No locked files detected

### Step 4: Test Clean Build
```powershell
cd server
.\safe-build.ps1 -Clean
```

Should complete with:
- [ ] "Build completed successfully!"
- [ ] All bin/obj folders cleaned

### Step 5: Test Full Rebuild
```powershell
cd server
.\safe-build.ps1 -Rebuild
```

Should complete with:
- [ ] "Build completed successfully!"
- [ ] Complete rebuild without errors

### Step 6: Test Start Script
```powershell
cd ..  # Back to root
.\start-dev.ps1
```

Should show:
- [ ] "Ensuring clean state..."
- [ ] "Building solution..."
- [ ] "Build completed successfully!"
- [ ] API starts without errors
- [ ] Angular starts without errors
- [ ] No file locking warnings

### Step 7: Test Stop Script
```powershell
.\stop-dev.ps1
```

Should show:
- [ ] All processes stopped
- [ ] File handles released
- [ ] No locked files detected

### Step 8: Test Full Cycle
```powershell
.\start-dev.ps1
# Wait for servers to start (30 seconds)
.\stop-dev.ps1
# Wait 2 seconds
cd server
.\safe-build.ps1
```

Should complete with:
- [ ] No errors in any step
- [ ] No file locking messages

## Visual Studio Configuration

### Step 9: Configure VS Settings
1. Open Visual Studio
2. Go to: Tools ? Options ? Projects and Solutions ? Build and Run
3. Set these values:
   - [ ] "On Run, when projects are out of date" ? **Prompt to build**
   - [ ] "On Run, when build or deployment errors occur" ? **Do not launch**
   - [ ] "MSBuild project build output verbosity" ? **Normal**

### Step 10: Test in Visual Studio
1. Open solution in Visual Studio
2. Build solution (Ctrl+Shift+B)
3. Check Output window for:
   - [ ] No file locking errors
   - [ ] Build completes successfully

### Step 11: Configure Antivirus (Optional but Recommended)
Add these folders to antivirus exclusions:
- [ ] `D:\Propel_IQ\Propel-IQ-Patient-Platform\server\**\bin\**`
- [ ] `D:\Propel_IQ\Propel-IQ-Patient-Platform\server\**\obj\**`
- [ ] `C:\Users\[YourUsername]\.nuget\packages\**`

## Final Verification

### Step 12: Stress Test
Run multiple builds in succession:
```powershell
cd server
.\safe-build.ps1
.\safe-build.ps1
.\safe-build.ps1
.\safe-build.ps1 -Clean
.\safe-build.ps1 -Rebuild
```

All should complete with:
- [ ] No file locking errors
- [ ] Successful builds

### Step 13: Start/Stop Cycle Test
```powershell
cd ..  # Back to root
.\start-dev.ps1
# Let it fully start
.\stop-dev.ps1
# Wait 2 seconds
.\start-dev.ps1
# Let it fully start
.\stop-dev.ps1
```

Should complete with:
- [ ] No errors in any cycle
- [ ] Clean start/stop every time

## Workflow Verification

### Step 14: Your Daily Workflow
Test your typical development workflow:

```powershell
# Morning - start development
.\start-dev.ps1
# Should: ? Build, ? Start servers, ? No errors

# Make some code changes...

# Build to test changes
cd server
.\safe-build.ps1
# Should: ? Build successfully, ? No file locks

# End of day - stop development
cd ..
.\stop-dev.ps1
# Should: ? Clean shutdown, ? No locked files
```

All steps should complete without errors:
- [ ] Morning start works
- [ ] Intermediate builds work
- [ ] End of day stop works

## Success Criteria

You can mark the fix as successful if:

? **All build operations complete without file locking errors**
? **Can build immediately after stopping development server**
? **No need to manually clean bin/obj folders**
? **Start/stop cycles work reliably**
? **No locked files detected after stopping**
? **Builds are faster than before (hard links)**
? **Visual Studio builds work without issues**

## Troubleshooting

If any step fails:

### Build Fails
```powershell
.\fix-file-locking.ps1
.\safe-build.ps1 -Rebuild
```

### Files Still Locked
```powershell
# Nuclear option
Get-Process -Name "dotnet" | Stop-Process -Force
Start-Sleep -Seconds 3
.\fix-file-locking.ps1
.\safe-build.ps1 -Rebuild
```

### Script Not Found
```powershell
# Verify you're in the right directory
Get-Location
# Should be in: D:\Propel_IQ\Propel-IQ-Patient-Platform

# List files
Get-ChildItem -Filter "*.ps1"
```

### Permission Denied
```powershell
# Run PowerShell as Administrator
# Or unblock scripts:
Get-ChildItem -Recurse -Filter "*.ps1" | Unblock-File
```

## Documentation Reference

If you need help:
- [ ] Read `FILE_LOCKING_FIX_COMPLETE.md` for detailed information
- [ ] Read `QUICK_FIX_FILE_LOCKING.md` for quick reference
- [ ] Read `FILE_LOCKING_RESOLUTION_SUMMARY.md` for complete summary

## Final Status

Date: _______________

Overall Status:
- [ ] ? All checks passed - Ready to use!
- [ ] ?? Some issues - Need to troubleshoot (see above)
- [ ] ? Failed - Need help (check documentation)

Notes:
_______________________________________________
_______________________________________________
_______________________________________________

## Remember!

From now on, always use:
```powershell
.\start-dev.ps1  # Start development
.\stop-dev.ps1   # Stop development
.\safe-build.ps1 # Build manually
```

**Never use `dotnet build` directly!**

---

?? Once all checkboxes are marked, you're done!
The file locking issue is completely resolved!
