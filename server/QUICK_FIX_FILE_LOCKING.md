# ?? File Locking Quick Fix

## Problem
`The process cannot access the file '*.pdb' because it is being used by another process`

## Immediate Fix (Run This)
```powershell
.\fix-file-locking.ps1
```

## Prevention (Use These Commands)

### Starting Development
```powershell
.\start-dev.ps1  # Already includes safe build!
```

### Stopping Development
```powershell
.\stop-dev.ps1   # Ensures clean shutdown
```

### Building Safely
```powershell
.\safe-build.ps1              # Debug build
.\safe-build.ps1 -Clean       # Clean first
.\safe-build.ps1 -Rebuild     # Full rebuild
```

## File Structure Created
```
server/
??? Directory.Build.props      # ? Prevents file locking at MSBuild level
??? .editorconfig              # ? Build optimization settings
??? fix-file-locking.ps1      # ? Emergency fix
??? safe-build.ps1            # ? Use instead of 'dotnet build'
??? FILE_LOCKING_FIX_COMPLETE.md  # ? Full documentation
```

## What Was Fixed

### 1. MSBuild Configuration (`Directory.Build.props`)
- ? Uses hard links instead of copying files
- ? Optimizes compiler process reuse
- ? Prevents unnecessary file locks
- ? Enables deterministic builds

### 2. Safe Build Script (`safe-build.ps1`)
- ? Stops all processes before building
- ? Waits for file handles to be released
- ? Uses optimized MSBuild settings
- ? Checks for locked files after build

### 3. Start Script (`start-dev.ps1`)
- ? Ensures clean state before starting
- ? Uses safe build automatically
- ? Prevents file locking from the start

### 4. Stop Script (`stop-dev.ps1`)
- ? Stops all dotnet processes
- ? Releases file handles
- ? Checks for locked files

## Best Practices

### ? DO
- Use `start-dev.ps1` to start development
- Use `stop-dev.ps1` before building
- Use `safe-build.ps1` instead of `dotnet build`
- Wait 2 seconds after stopping processes

### ? DON'T
- Don't build while application is running
- Don't use `dotnet build` directly
- Don't have multiple instances open
- Don't skip the stop script

## If Issues Persist

### Nuclear Option (Complete Reset)
```powershell
# 1. Close Visual Studio

# 2. Run this:
Get-Process -Name "dotnet","MSBuild","devenv" -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet clean
Get-ChildItem -Path "." -Include "bin","obj" -Recurse -Directory | Remove-Item -Recurse -Force
dotnet nuget locals all --clear
dotnet restore
.\safe-build.ps1 -Rebuild

# 3. Reopen Visual Studio
```

## Success Indicators

You'll know it's working when:
- ? No "file in use" errors during build
- ? Faster builds (hard links are faster)
- ? Can build immediately after stopping server
- ? No need to manually clean between builds

## Additional Settings

### Visual Studio Options
Tools ? Options ? Projects and Solutions ? Build and Run
- "On Run, when projects are out of date" ? **Prompt to build**
- "MSBuild project build output verbosity" ? **Normal**

### Antivirus Exclusions
Add these folders:
```
D:\Propel_IQ\Propel-IQ-Patient-Platform\server\**\bin\**
D:\Propel_IQ\Propel-IQ-Patient-Platform\server\**\obj\**
```

## Support Commands

```powershell
# Check what's locking files
.\fix-file-locking.ps1

# See all dotnet processes
Get-Process -Name "dotnet"

# Force stop everything
Get-Process -Name "dotnet","MSBuild","devenv" | Stop-Process -Force

# Check file locks manually
$file = "path\to\locked\file.pdb"
try { 
    [System.IO.File]::Open($file, 'Open', 'ReadWrite', 'None').Close()
    Write-Host "Not locked"
} catch { 
    Write-Host "Locked!"
}
```

## ?? That's It!

The file locking issue should now be completely resolved!

Just remember:
1. **Start**: Use `start-dev.ps1`
2. **Build**: Use `safe-build.ps1` (if needed separately)
3. **Stop**: Use `stop-dev.ps1`

---

**For full details, see**: `FILE_LOCKING_FIX_COMPLETE.md`
