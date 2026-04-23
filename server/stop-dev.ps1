# Propel IQ - Stop Development Servers Script
# This script stops all processes running on ports 4200 and 5001

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

Write-Host "???????????????????????????????????????" -ForegroundColor Cyan
Write-Host "? All development servers stopped!" -ForegroundColor Green
Write-Host "???????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""
Write-Host "?? You can now run .\start-dev.ps1 to start them again" -ForegroundColor Yellow
Write-Host ""
