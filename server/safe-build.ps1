# Safe Build Script - Prevents File Locking Issues
# Run this instead of direct 'dotnet build'

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    
    [Parameter(Mandatory=$false)]
    [switch]$Clean,
    
    [Parameter(Mandatory=$false)]
    [switch]$Rebuild
)

Write-Host "=== Safe Build Process ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host ""

# Step 1: Stop all running processes
Write-Host "[1/5] Stopping processes that might lock files..." -ForegroundColor Yellow
Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name "Propel.*" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

# Step 2: Clean if requested
if ($Clean -or $Rebuild) {
    Write-Host "[2/5] Cleaning build artifacts..." -ForegroundColor Yellow
    dotnet clean --configuration $Configuration --verbosity quiet
    
    # Also manually clean bin/obj folders
    $folders = Get-ChildItem -Path "." -Include "bin","obj" -Recurse -Directory
    foreach ($folder in $folders) {
        Remove-Item -Path $folder.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "[2/5] Skipping clean step" -ForegroundColor Gray
}

# Step 3: Wait to ensure files are unlocked
Write-Host "[3/5] Waiting for file handles to be released..." -ForegroundColor Yellow
Start-Sleep -Seconds 2

# Step 4: Restore packages (this rarely causes locks)
Write-Host "[4/5] Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore --no-cache

# Step 5: Build with optimized settings
Write-Host "[5/5] Building solution..." -ForegroundColor Yellow
$buildArgs = @(
    "build",
    "--configuration", $Configuration,
    "--no-restore",
    "/p:UseSharedCompilation=true",
    "/p:ProduceReferenceAssembly=false",
    "/p:CreateHardLinksForCopyLocalIfPossible=true",
    "/maxcpucount"
)

$result = dotnet @buildArgs

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "? Build completed successfully!" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host ""
    Write-Host "? Build failed with errors" -ForegroundColor Red
    Write-Host ""
    exit $LASTEXITCODE
}

# Check for any remaining locked files
Write-Host "Checking for locked files..." -ForegroundColor Yellow
$lockedFiles = Get-ChildItem -Path "." -Include "*.pdb","*.dll" -Recurse -File -ErrorAction SilentlyContinue | 
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
    Write-Host "Warning: Some files are still locked:" -ForegroundColor Yellow
    $lockedFiles | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
} else {
    Write-Host "No locked files detected!" -ForegroundColor Green
}

Write-Host ""
Write-Host "Build process complete!" -ForegroundColor Cyan
