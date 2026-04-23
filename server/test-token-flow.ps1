# Test Token Flow
# Run this after restarting both backend and frontend

Write-Host "=== Token Flow Verification ===" -ForegroundColor Cyan
Write-Host ""

# Check if backend is running
Write-Host "1. Checking backend..." -ForegroundColor Yellow
try {
    $healthCheck = Invoke-RestMethod -Uri "http://localhost:5000/health" -Method Get -ErrorAction Stop
    Write-Host "   ? Backend is running" -ForegroundColor Green
} catch {
    Write-Host "   ? Backend is NOT running!" -ForegroundColor Red
    Write-Host "   Run: cd server; dotnet run --project Propel.Api.Gateway" -ForegroundColor Yellow
    exit 1
}

Write-Host ""

# Test login endpoint
Write-Host "2. Testing login endpoint..." -ForegroundColor Yellow
$loginBody = @{
    email = "patient@example.com"
    password = "Patient123!"
} | ConvertTo-Json

try {
    $loginResponse = Invoke-RestMethod -Uri "http://localhost:5000/api/auth/login" `
        -Method Post `
        -Body $loginBody `
        -ContentType "application/json" `
        -ErrorAction Stop
    
    Write-Host "   ? Login successful" -ForegroundColor Green
    
    # Check response structure
    if ($loginResponse.accessToken) {
        Write-Host "   ? accessToken present" -ForegroundColor Green
    } else {
        Write-Host "   ? accessToken MISSING!" -ForegroundColor Red
    }
    
    if ($loginResponse.refreshToken) {
        Write-Host "   ? refreshToken present" -ForegroundColor Green
    } else {
        Write-Host "   ? refreshToken MISSING!" -ForegroundColor Red
    }
    
    if ($loginResponse.userId) {
        Write-Host "   ? userId present: $($loginResponse.userId)" -ForegroundColor Green
    } else {
        Write-Host "   ? userId MISSING! (This is the problem!)" -ForegroundColor Red
        Write-Host "   Backend needs to be rebuilt and restarted" -ForegroundColor Yellow
    }
    
    if ($loginResponse.role) {
        Write-Host "   ? role present: $($loginResponse.role)" -ForegroundColor Green
    } else {
        Write-Host "   ? role MISSING! (This is the problem!)" -ForegroundColor Red
        Write-Host "   Backend needs to be rebuilt and restarted" -ForegroundColor Yellow
    }
    
    Write-Host ""
    Write-Host "Full response:" -ForegroundColor Cyan
    $loginResponse | ConvertTo-Json -Depth 10
    
    # Save token for dashboard test
    $global:accessToken = $loginResponse.accessToken
    
} catch {
    Write-Host "   ? Login failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== RESULT ===" -ForegroundColor Cyan

if ($loginResponse.userId -and $loginResponse.role) {
    Write-Host "? Backend is configured correctly!" -ForegroundColor Green
    Write-Host "  If Angular still fails, restart the Angular dev server:" -ForegroundColor Yellow
    Write-Host "  cd ..\app" -ForegroundColor Gray
    Write-Host "  npm start" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Then login and check browser console for [AuthService] logs" -ForegroundColor Yellow
} else {
    Write-Host "? Backend is NOT returning userId/role!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Fix steps:" -ForegroundColor Yellow
    Write-Host "1. Stop the debugger in Visual Studio" -ForegroundColor White
    Write-Host "2. Run: dotnet build" -ForegroundColor White
    Write-Host "3. Restart the application" -ForegroundColor White
    Write-Host "4. Run this script again" -ForegroundColor White
}

Write-Host ""
