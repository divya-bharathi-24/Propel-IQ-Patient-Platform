# Fix File Locking Issues
# This script stops all processes that might be locking build files

Write-Host "=== Fixing File Locking Issues ===" -ForegroundColor Cyan
Write-Host ""

# Stop all dotnet processes
Write-Host "Stopping all .NET processes..." -ForegroundColor Yellow
Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name "Propel.*" -ErrorAction SilentlyContinue | Stop-Process -Force

# Wait a moment
Start-Sleep -Seconds 2

# Clean all bin and obj folders
Write-Host "Cleaning build artifacts..." -ForegroundColor Yellow
$folders = Get-ChildItem -Path "." -Include "bin","obj" -Recurse -Directory
foreach ($folder in $folders) {
    Write-Host "  Removing: $($folder.FullName)" -ForegroundColor Gray
    Remove-Item -Path $folder.FullName -Recurse -Force -ErrorAction SilentlyContinue
}

# Check for locked files
Write-Host ""
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
    Write-Host "Found locked files:" -ForegroundColor Red
    $lockedFiles | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
} else {
    Write-Host "No locked files found!" -ForegroundColor Green
}

Write-Host ""
Write-Host "File locking issue should be resolved!" -ForegroundColor Green
Write-Host "You can now rebuild your solution." -ForegroundColor Green
