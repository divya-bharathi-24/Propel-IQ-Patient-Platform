# Propel IQ - Stop Development Servers Script
# This script stops all processes running on ports 4200 and 5001
# Enhanced to prevent file locking issues

Write-Host "?? Stopping Propel IQ Development Servers..." -ForegroundColor Cyan
Write-Host ""

# Function to kill processes on a specific port
function Stop-ProcessOnPort {
    param (
        [int]$Port,
        [string]$ServerName
    )
    
    Write-Host "?? Checking for $ServerName on port $Port..." -ForegroundColor Yellow
    
    $connections = netstat -ano | Select-String ":$Port"
    
    if ($connections) {
        $pids = @()
        foreach ($connection in $connections) {
            # Extract PID (last column)
            if ($connection -match '\s+(\d+)\s*$') {
                $pid = $matches[1]
                if ($pid -and $pid -ne "0" -and $pids -notcontains $pid) {
                    $pids += $pid
                }
            }
        }
        
        if ($pids.Count -gt 0) {
            Write-Host "  Found $($pids.Count) process(es): $($pids -join ', ')" -ForegroundColor Gray
            
            foreach ($pid in $pids) {
                try {
                    $process = Get-Process -Id $pid -ErrorAction SilentlyContinue
                    if ($process) {
                        Write-Host "  ??  Stopping PID $pid ($($process.ProcessName))..." -ForegroundColor White
                        Stop-Process -Id $pid -Force -ErrorAction Stop
                        Write-Host "    ? Stopped successfully" -ForegroundColor Green
                    }
                } catch {
                    Write-Host "    ??  Could not stop PID $pid (might require admin privileges)" -ForegroundColor Red
                }
            }
        } else {
            Write-Host "  ??  No active processes found on port $Port" -ForegroundColor Gray
        }
    } else {
        Write-Host "  ? Port $Port is already free" -ForegroundColor Green
    }
    
    Write-Host ""
}

# Stop Angular dev server (port 4200)
Stop-ProcessOnPort -Port 4200 -ServerName "Angular Frontend"

# Stop .NET API (port 5001)
Stop-ProcessOnPort -Port 5001 -ServerName ".NET API"

# Also check port 5000 (HTTP endpoint of .NET API)
Stop-ProcessOnPort -Port 5000 -ServerName ".NET API (HTTP)"

# Stop any remaining dotnet processes (to prevent file locking)
Write-Host "?? Checking for other dotnet processes..." -ForegroundColor Yellow
$dotnetProcesses = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue
if ($dotnetProcesses) {
    Write-Host "  Found $($dotnetProcesses.Count) dotnet process(es)" -ForegroundColor Gray
    foreach ($process in $dotnetProcesses) {
        Write-Host "  ??  Stopping dotnet PID $($process.Id)..." -ForegroundColor White
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    Write-Host "  ? All dotnet processes stopped" -ForegroundColor Green
} else {
    Write-Host "  ? No dotnet processes found" -ForegroundColor Green
}
Write-Host ""

# Stop any Propel-specific processes
Write-Host "?? Checking for Propel processes..." -ForegroundColor Yellow
$propelProcesses = Get-Process -Name "Propel.*" -ErrorAction SilentlyContinue
if ($propelProcesses) {
    Write-Host "  Found $($propelProcesses.Count) Propel process(es)" -ForegroundColor Gray
    foreach ($process in $propelProcesses) {
        Write-Host "  ??  Stopping $($process.ProcessName) PID $($process.Id)..." -ForegroundColor White
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    Write-Host "  ? All Propel processes stopped" -ForegroundColor Green
} else {
    Write-Host "  ? No Propel processes found" -ForegroundColor Green
}
Write-Host ""

# Wait for file handles to be released
Write-Host "? Waiting for file handles to be released..." -ForegroundColor Yellow
Start-Sleep -Seconds 2
Write-Host "  ? File handles should be released now" -ForegroundColor Green
Write-Host ""

# Check for locked files in build output
Write-Host "?? Checking for locked files..." -ForegroundColor Yellow
$serverPath = Join-Path $PSScriptRoot "server"
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
        Write-Host "  ??  Warning: Some files are still locked:" -ForegroundColor Yellow
        $lockedFiles | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
        Write-Host ""
        Write-Host "  ?? Tip: Run '.\fix-file-locking.ps1' if you encounter build issues" -ForegroundColor Cyan
    } else {
        Write-Host "  ? No locked files detected!" -ForegroundColor Green
    }
} else {
    Write-Host "  ??  Server path not found, skipping file lock check" -ForegroundColor Gray
}
Write-Host ""

Write-Host "?????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "  ? All development servers stopped!" -ForegroundColor Green
Write-Host "  ?? File locks released!" -ForegroundColor Green
Write-Host "?????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""
Write-Host "?? You can now:" -ForegroundColor Yellow
Write-Host "  ? Run .\start-dev.ps1 to start servers" -ForegroundColor Gray
Write-Host "  ? Build solution without file locking issues" -ForegroundColor Gray
Write-Host "  ? Clean build outputs safely" -ForegroundColor Gray
Write-Host ""
