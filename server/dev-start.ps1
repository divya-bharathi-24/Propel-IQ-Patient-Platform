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

Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        $cmd = (Get-WmiObject Win32_Process -Filter "ProcessId=$($_.Id)" -ErrorAction SilentlyContinue).CommandLine
        if ($cmd -like "*Propel.Api.Gateway*") {
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
            Write-Host "[dev-start] Killed dotnet PID $($_.Id)" -ForegroundColor DarkYellow
        }
    } catch { }
}

# Give the OS a moment to release all file handles
Start-Sleep -Milliseconds 1000

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
