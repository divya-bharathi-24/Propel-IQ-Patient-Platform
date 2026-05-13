#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Safely starts the Propel API Gateway for local development.
    Kills any previously running instance first to avoid file-lock errors on
    bin/Debug/net10.0/*.pdb and *.dll files during rebuild.
.EXAMPLE
    .\dev-start.ps1
    .\dev-start.ps1 -NoBuild
#>

param(
    [switch]$NoBuild
)

$ProjectPath = "$PSScriptRoot\Propel.Api.Gateway\Propel.Api.Gateway.csproj"
$env:ASPNETCORE_ENVIRONMENT = "Development"

Write-Host "[dev-start] Stopping any running Propel.Api.Gateway instance..." -ForegroundColor Yellow

# Kill compiled self-contained executable instances
Get-Process -Name "Propel.Api.Gateway" -ErrorAction SilentlyContinue | ForEach-Object {
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
    Write-Host "[dev-start] Killed Propel.Api.Gateway PID $($_.Id)" -ForegroundColor DarkYellow
}

# Kill dotnet-hosted instances
Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        $cmd = (Get-WmiObject Win32_Process -Filter "ProcessId=$($_.Id)" -ErrorAction SilentlyContinue).CommandLine
        if ($cmd -like "*Propel.Api.Gateway*") {
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
            Write-Host "[dev-start] Killed dotnet PID $($_.Id)" -ForegroundColor DarkYellow
        }
    } catch { }
}

# Free ports 5000 and 5001 in case anything else grabbed them
foreach ($port in @(5000, 5001)) {
    $pids = (netstat -ano | Select-String ":$port\s" | ForEach-Object {
        ($_ -split '\s+')[-1]
    } | Sort-Object -Unique) -ne '0'
    foreach ($pid in $pids) {
        try {
            $proc = Get-Process -Id $pid -ErrorAction SilentlyContinue
            if ($proc -and $proc.Name -notin @('System', 'svchost')) {
                Stop-Process -Id $pid -Force -ErrorAction SilentlyContinue
                Write-Host "[dev-start] Released port $port (PID $pid / $($proc.Name))" -ForegroundColor DarkYellow
            }
        } catch { }
    }
}

# Give the OS a moment to release all file handles and sockets
Start-Sleep -Milliseconds 1500

if (-not $NoBuild) {
    Write-Host "[dev-start] Building..." -ForegroundColor Cyan
    dotnet build $ProjectPath -c Debug
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[dev-start] Build failed. Aborting." -ForegroundColor Red
        exit 1
    }
}

Write-Host "[dev-start] Starting Propel.Api.Gateway..." -ForegroundColor Green
dotnet run --no-build --project $ProjectPath
