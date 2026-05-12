# Verify File Locking Fix Installation
# Checks that all fixes are properly installed

Write-Host "?? Verifying File Locking Fix Installation..." -ForegroundColor Cyan
Write-Host ""

$allGood = $true
$serverPath = Join-Path $PSScriptRoot "server"

# Check 1: Directory.Build.props
Write-Host "[1/5] Checking Directory.Build.props..." -ForegroundColor Yellow
$directoryBuildProps = Join-Path $serverPath "Directory.Build.props"
if (Test-Path $directoryBuildProps) {
    $content = Get-Content $directoryBuildProps -Raw
    if ($content -match "CreateHardLinksForCopyLocalIfPossible" -and 
        $content -match "UseSharedCompilation") {
        Write-Host "  ? Directory.Build.props is properly configured" -ForegroundColor Green
    } else {
        Write-Host "  ??  Directory.Build.props exists but may be incomplete" -ForegroundColor Yellow
        $allGood = $false
    }
} else {
    Write-Host "  ? Directory.Build.props not found!" -ForegroundColor Red
    Write-Host "     Expected at: $directoryBuildProps" -ForegroundColor Gray
    $allGood = $false
}
Write-Host ""

# Check 2: .editorconfig
Write-Host "[2/5] Checking .editorconfig..." -ForegroundColor Yellow
$editorConfig = Join-Path $serverPath ".editorconfig"
if (Test-Path $editorConfig) {
    Write-Host "  ? .editorconfig found" -ForegroundColor Green
} else {
    Write-Host "  ??  .editorconfig not found (optional)" -ForegroundColor Yellow
}
Write-Host ""

# Check 3: Scripts
Write-Host "[3/5] Checking required scripts..." -ForegroundColor Yellow
$scripts = @(
    "fix-file-locking.ps1",
    "safe-build.ps1",
    "start-dev.ps1",
    "stop-dev.ps1"
)

foreach ($script in $scripts) {
    $scriptPath = if ($script -like "*fix-file-locking*" -or $script -like "*safe-build*") {
        Join-Path $serverPath $script
    } else {
        Join-Path $PSScriptRoot $script
    }
    
    if (Test-Path $scriptPath) {
        Write-Host "  ? $script" -ForegroundColor Green
    } else {
        Write-Host "  ? $script not found!" -ForegroundColor Red
        Write-Host "     Expected at: $scriptPath" -ForegroundColor Gray
        $allGood = $false
    }
}
Write-Host ""

# Check 4: Documentation
Write-Host "[4/5] Checking documentation..." -ForegroundColor Yellow
$docs = @(
    "FILE_LOCKING_FIX_COMPLETE.md",
    "QUICK_FIX_FILE_LOCKING.md"
)

foreach ($doc in $docs) {
    $docPath = Join-Path $serverPath $doc
    if (Test-Path $docPath) {
        Write-Host "  ? $doc" -ForegroundColor Green
    } else {
        Write-Host "  ??  $doc not found (informational only)" -ForegroundColor Yellow
    }
}
Write-Host ""

# Check 5: Current file locks
Write-Host "[5/5] Checking for currently locked files..." -ForegroundColor Yellow
if (Test-Path $serverPath) {
    $lockedFiles = Get-ChildItem -Path $serverPath -Include "*.pdb","*.dll" -Recurse -File -ErrorAction SilentlyContinue | 
        Where-Object { 
            try {
                $stream = [System.IO.File]::Open($_.FullName, 'Open', 'ReadWrite', 'None')
                $stream.Close()
                $false
            } catch {
                $true
            }
        }
    
    if ($lockedFiles) {
        Write-Host "  ??  Found locked files:" -ForegroundColor Yellow
        $lockedFiles | ForEach-Object { Write-Host "     $_" -ForegroundColor Gray }
        Write-Host ""
        Write-Host "  ?? Run '.\fix-file-locking.ps1' to unlock them" -ForegroundColor Cyan
        $allGood = $false
    } else {
        Write-Host "  ? No locked files found" -ForegroundColor Green
    }
} else {
    Write-Host "  ??  Server path not found" -ForegroundColor Yellow
}
Write-Host ""

# Final Summary
Write-Host "???????????????????????????????????????" -ForegroundColor Cyan
if ($allGood) {
    Write-Host "? All checks passed!" -ForegroundColor Green
    Write-Host "File locking fix is properly installed." -ForegroundColor Green
    Write-Host ""
    Write-Host "You can now:" -ForegroundColor Yellow
    Write-Host "  1. Use .\start-dev.ps1 to start development" -ForegroundColor Gray
    Write-Host "  2. Use .\safe-build.ps1 for manual builds" -ForegroundColor Gray
    Write-Host "  3. Use .\stop-dev.ps1 to stop servers" -ForegroundColor Gray
} else {
    Write-Host "??  Some checks failed!" -ForegroundColor Yellow
    Write-Host "Please review the issues above." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To reinstall missing files, re-run the setup process." -ForegroundColor Gray
}
Write-Host "???????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

# Show usage examples
Write-Host "?? Quick Usage Guide:" -ForegroundColor Cyan
Write-Host ""
Write-Host "Start Development:" -ForegroundColor Yellow
Write-Host "  PS> .\start-dev.ps1" -ForegroundColor White
Write-Host ""
Write-Host "Build Safely:" -ForegroundColor Yellow
Write-Host "  PS> .\safe-build.ps1" -ForegroundColor White
Write-Host "  PS> .\safe-build.ps1 -Clean" -ForegroundColor White
Write-Host "  PS> .\safe-build.ps1 -Rebuild" -ForegroundColor White
Write-Host ""
Write-Host "Stop Development:" -ForegroundColor Yellow
Write-Host "  PS> .\stop-dev.ps1" -ForegroundColor White
Write-Host ""
Write-Host "Fix File Locks:" -ForegroundColor Yellow
Write-Host "  PS> .\fix-file-locking.ps1" -ForegroundColor White
Write-Host ""
