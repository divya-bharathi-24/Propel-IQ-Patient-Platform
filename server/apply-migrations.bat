@echo off
REM Propel Database Migration Script
REM Applies pending EF Core migrations to the PostgreSQL database

setlocal enabledelayedexpansion

echo ================================================
echo   Propel Database Migration Script
echo ================================================
echo.

cd /d "%~dp0"

echo [1/3] Checking for dotnet CLI...
where dotnet >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: .NET SDK not found. Please install .NET 10 SDK.
    exit /b 1
)

for /f "delims=" %%i in ('dotnet --version') do set DOTNET_VERSION=%%i
echo       Found: .NET SDK %DOTNET_VERSION%
echo.

echo [2/3] Checking for pending migrations...
echo       Project: Propel.Api.Gateway
echo.

echo [3/3] Applying migrations to database...
echo.

dotnet ef database update --project server\Propel.Api.Gateway --verbose

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ================================================
    echo   SUCCESS: Migrations applied successfully!
    echo ================================================
    echo.
    echo The refresh_tokens table now includes:
    echo   - patient_id column (nullable^)
    echo   - user_id column (now nullable^)
    echo   - CHECK constraint ensuring exactly one is non-null
    echo   - Foreign keys and indexes for patient authentication
    echo.
    echo You can now restart your application.
    echo.
) else (
    echo.
    echo ================================================
    echo   ERROR: Migration failed!
    echo ================================================
    echo.
    echo Troubleshooting steps:
    echo 1. Ensure PostgreSQL is running and accessible
    echo 2. Check your connection string in appsettings.json
    echo 3. Verify database credentials are correct
    echo 4. Check Docker containers if using docker-compose:
    echo    docker-compose ps
    echo.
    exit /b 1
)
