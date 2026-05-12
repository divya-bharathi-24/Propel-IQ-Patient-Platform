# File Locking Issue - Complete Fix Guide

## Problem
Getting error: "The process cannot access the file '*.pdb' because it is being used by another process"

## Root Causes
1. Running application/debugger holding file locks
2. Visual Studio design-time build processes
3. Antivirus scanning build outputs
4. MSBuild shadow copy not working correctly
5. File handles not being released properly

## Immediate Fix (Run This First)
```powershell
# Stop all processes and clean
.\fix-file-locking.ps1
```

## Permanent Solution

### Step 1: Add Directory.Build.props
A `Directory.Build.props` file has been created in your solution root. This file:
- Enables hard links instead of file copying
- Optimizes MSBuild to reduce file locks
- Prevents unnecessary file generation
- Speeds up builds

**Location**: `Directory.Build.props` (already created)

### Step 2: Update Visual Studio Settings
1. Open Visual Studio
2. Go to Tools ? Options ? Projects and Solutions ? Build and Run
3. Set these values:
   - "On Run, when projects are out of date" ? **Prompt to build**
   - "On Run, when build or deployment errors occur" ? **Do not launch**
   - "MSBuild project build output verbosity" ? **Normal**
   - "MSBuild project build log file verbosity" ? **Normal**

4. Go to Tools ? Options ? Environment ? Preview Features
5. Enable:
   - ? Use out-of-process task host for project load
   - ? Use faster project load

### Step 3: Configure Antivirus Exclusions
Add these folders to your antivirus exclusions:
```
D:\Propel_IQ\Propel-IQ-Patient-Platform\server\**\bin\**
D:\Propel_IQ\Propel-IQ-Patient-Platform\server\**\obj\**
C:\Users\[YourUsername]\.nuget\packages\**
```

### Step 4: Use Safe Build Script
Instead of directly using `dotnet build`, use the new safe build script:

```powershell
# Regular build
.\safe-build.ps1

# Clean build
.\safe-build.ps1 -Clean

# Rebuild
.\safe-build.ps1 -Rebuild

# Release build
.\safe-build.ps1 -Configuration Release
```

### Step 5: Update Your Development Workflow

#### Before Building:
```powershell
# 1. Stop all running processes
.\stop-dev.ps1

# 2. Wait 2 seconds for file handles to release
Start-Sleep -Seconds 2

# 3. Build safely
.\safe-build.ps1
```

#### Before Running:
```powershell
# Always use the safe build before starting
.\safe-build.ps1
.\start-dev.ps1
```

## Additional Troubleshooting

### If Issue Persists - Nuclear Option
```powershell
# This will completely reset your build environment
# Warning: Will delete all build outputs

# 1. Close Visual Studio
# 2. Stop all processes
Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name "MSBuild" -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name "devenv" -ErrorAction SilentlyContinue | Stop-Process -Force

# 3. Clean everything
dotnet clean
Get-ChildItem -Path "." -Include "bin","obj" -Recurse -Directory | Remove-Item -Recurse -Force

# 4. Clear NuGet cache
dotnet nuget locals all --clear

# 5. Restore and rebuild
dotnet restore
.\safe-build.ps1 -Rebuild

# 6. Reopen Visual Studio
```

### Check What's Locking Files
```powershell
# Install Handle tool (one-time)
# Download from: https://download.sysinternals.com/files/Handle.zip

# Check what process is locking a file
handle.exe "Propel.Modules.Risk.pdb"

# Kill the locking process
taskkill /F /PID [ProcessID]
```

## Prevention Checklist

? **Do This:**
- Always use `safe-build.ps1` instead of direct `dotnet build`
- Stop development server before building
- Wait 2 seconds after stopping processes
- Exclude build folders from antivirus
- Use `start-dev.ps1` and `stop-dev.ps1` scripts

? **Don't Do This:**
- Don't build while application is running
- Don't manually copy DLL/PDB files while they're in use
- Don't have multiple Visual Studio instances open with same solution
- Don't run builds from multiple terminals simultaneously

## Quick Reference Commands

```powershell
# Fix file locking immediately
.\fix-file-locking.ps1

# Safe build
.\safe-build.ps1

# Full clean and rebuild
.\safe-build.ps1 -Rebuild

# Stop everything and build
.\stop-dev.ps1
Start-Sleep -Seconds 2
.\safe-build.ps1

# Start development (will auto-build if needed)
.\start-dev.ps1
```

## File Structure
```
server/
??? Directory.Build.props          ? MSBuild configuration (prevents locks)
??? .editorconfig                  ? Editor configuration
??? fix-file-locking.ps1          ? Emergency fix script
??? safe-build.ps1                 ? Safe build script (use this!)
??? start-dev.ps1                  ? Development start script
??? stop-dev.ps1                   ? Development stop script
```

## Technical Details

### What Directory.Build.props Does:
- **Hard Links**: Instead of copying files, creates hard links (faster, no locks)
- **Shared Compilation**: Reuses compiler processes (reduces file handles)
- **Deterministic Builds**: Produces same output for same input (better caching)
- **Reference Assembly**: Disabled to reduce intermediate files
- **Design-Time Build**: Optimized to not lock files during IDE operations

### What safe-build.ps1 Does:
1. Stops all dotnet processes
2. Cleans build artifacts if requested
3. Waits for file handles to be released
4. Restores NuGet packages
5. Builds with optimized MSBuild settings
6. Checks for remaining locked files
7. Reports status

## Monitoring

After implementing these fixes, you should see:
- ? No more "file is being used" errors
- ? Faster builds (hard links are faster than copying)
- ? More reliable incremental builds
- ? Better IDE responsiveness

## Support

If you still get file locking errors after implementing all fixes:
1. Run `.\fix-file-locking.ps1` immediately
2. Check antivirus is not scanning build folders
3. Verify only one Visual Studio instance is open
4. Check Windows Search is not indexing build folders
5. Restart Visual Studio and try again

## Success Indicators

You'll know the fix is working when:
- ? Builds complete without "file in use" errors
- ? Incremental builds are faster
- ? Can build immediately after stopping development server
- ? No need to manually clean between builds
- ? Visual Studio design-time builds don't fail

---

**Remember**: Always use `safe-build.ps1` instead of direct `dotnet build`!
