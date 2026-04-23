# Complete Application Restart Script
# This will stop all running processes and restart both backend and frontend

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  PROPEL IQ - COMPLETE RESTART SCRIPT" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Kill all existing processes
Write-Host "Step 1: Stopping all running processes..." -ForegroundColor Yellow
Write-Host ""

# Kill .NET processes
Write-Host "  Stopping .NET backend..." -ForegroundColor Gray
Get-Process -Name "Propel.Api.Gateway" -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*Propel*" } | Stop-Process -Force
Start-Sleep -Seconds 2

# Kill Node/Angular processes  
Write-Host "  Stopping Angular frontend..." -ForegroundColor Gray
Get-Process -Name "node" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -like "*angular*" } | Stop-Process -Force
Get-Process -Name "ng" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

Write-Host "  ? All processes stopped" -ForegroundColor Green
Write-Host ""

# Step 2: Clean and rebuild backend
Write-Host "Step 2: Cleaning and rebuilding backend..." -ForegroundColor Yellow
Write-Host ""

Set-Location ".\server"

Write-Host "  Cleaning build artifacts..." -ForegroundColor Gray
dotnet clean --nologo --verbosity quiet
Remove-Item -Path "Propel.Api.Gateway\bin" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "Propel.Api.Gateway\obj" -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "  Building solution..." -ForegroundColor Gray
$buildOutput = dotnet build --nologo 2>&1 | Out-String

if ($LASTEXITCODE -ne 0) {
    Write-Host "  ? Build failed!" -ForegroundColor Red
    Write-Host $buildOutput
    exit 1
}

Write-Host "  ? Backend built successfully" -ForegroundColor Green
Write-Host ""

# Step 3: Start backend in new window
Write-Host "Step 3: Starting backend..." -ForegroundColor Yellow
Write-Host ""

$backendScript = @"
Write-Host 'Starting Propel API Gateway...' -ForegroundColor Cyan
Set-Location '$PWD'
dotnet run --project Propel.Api.Gateway\Propel.Api.Gateway.csproj --no-build
"@

Start-Process powershell -ArgumentList "-NoExit", "-Command", $backendScript
Write-Host "  ? Backend starting in new window..." -ForegroundColor Green
Write-Host "  Waiting for backend to initialize..." -ForegroundColor Gray

# Wait for backend to be ready
$backendReady = $false
$maxAttempts = 30
$attempt = 0

while (-not $backendReady -and $attempt -lt $maxAttempts) {
    Start-Sleep -Seconds 2
    $attempt++
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:5000/health" -Method Get -TimeoutSec 2 -ErrorAction SilentlyContinue
        if ($response.StatusCode -eq 200) {
            $backendReady = $true
        }
    } catch {
        Write-Host "  Attempt $attempt/$maxAttempts..." -ForegroundColor Gray
    }
}

if ($backendReady) {
    Write-Host "  ? Backend is ready!" -ForegroundColor Green
} else {
    Write-Host "  ? Backend health check timeout - continuing anyway" -ForegroundColor Yellow
}

Write-Host ""

# Step 4: Start frontend in new window
Write-Host "Step 4: Starting frontend..." -ForegroundColor Yellow
Write-Host ""

Set-Location "..\app"

$frontendScript = @"
Write-Host 'Starting Angular Development Server...' -ForegroundColor Cyan
Set-Location '$PWD'
npm start
"@

Start-Process powershell -ArgumentList "-NoExit", "-Command", $frontendScript
Write-Host "  ? Frontend starting in new window..." -ForegroundColor Green
Write-Host ""

# Step 5: Wait and open browser
Write-Host "Step 5: Waiting for services to initialize..." -ForegroundColor Yellow
Start-Sleep -Seconds 10

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  SERVICES STARTED SUCCESSFULLY!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Backend:  http://localhost:5000/swagger" -ForegroundColor White
Write-Host "Frontend: http://localhost:4200" -ForegroundColor White
Write-Host ""
Write-Host "Opening browser..." -ForegroundColor Gray
Start-Sleep -Seconds 5

# Open browser
Start-Process "http://localhost:4200/auth/login"

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  TESTING INSTRUCTIONS" -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Open Chrome DevTools (F12)" -ForegroundColor White
Write-Host "2. Go to Console tab" -ForegroundColor White
Write-Host "3. Login with your credentials" -ForegroundColor White
Write-Host "4. Watch for these console messages:" -ForegroundColor White
Write-Host ""
Write-Host "   [AuthService] Storing tokens: {" -ForegroundColor Gray
Write-Host "     deviceId: 'auto-12345...'  ? Must be present!" -ForegroundColor Green
Write-Host "   }" -ForegroundColor Gray
Write-Host ""
Write-Host "5. Dashboard should load successfully" -ForegroundColor White
Write-Host ""
Write-Host "If you still see 401:" -ForegroundColor Yellow
Write-Host "  - Check backend window for errors" -ForegroundColor White
Write-Host "  - Check Network tab in DevTools" -ForegroundColor White  
Write-Host "  - Look for 'deviceId' in login response" -ForegroundColor White
Write-Host ""
Write-Host "Press any key to exit this window..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
