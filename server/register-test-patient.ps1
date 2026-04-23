# Quick Patient Registration and Verification

Write-Host "=== PATIENT REGISTRATION & VERIFICATION ===" -ForegroundColor Cyan
Write-Host ""

$email = "patient@example.com"
$password = "Patient123!"
$name = "Test Patient"

Write-Host "Step 1: Registering patient..." -ForegroundColor Yellow
Write-Host "  Email: $email" -ForegroundColor Gray
Write-Host "  Password: $password" -ForegroundColor Gray
Write-Host ""

$registerBody = @{
    email = $email
    password = $password
    name = $name
    phone = "1234567890"
    dateOfBirth = "1990-01-01"
} | ConvertTo-Json

try {
    $registerResponse = Invoke-RestMethod -Uri "http://localhost:5000/api/auth/register" `
        -Method Post `
        -Body $registerBody `
        -ContentType "application/json" `
        -ErrorAction Stop
    
    Write-Host "? Registration successful!" -ForegroundColor Green
    Write-Host "  PatientId: $($registerResponse.patientId)" -ForegroundColor Green
    Write-Host ""
    Write-Host "  $($registerResponse.message)" -ForegroundColor Yellow
    Write-Host ""
    
    $patientId = $registerResponse.patientId
    
    # Get verification token from database
    Write-Host "Step 2: Getting verification token from database..." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  You need to run this SQL query to get the token:" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  SELECT token FROM email_verification_tokens" -ForegroundColor Cyan
    Write-Host "  WHERE patient_id = '$patientId'" -ForegroundColor Cyan
    Write-Host "  AND used_at IS NULL" -ForegroundColor Cyan
    Write-Host "  ORDER BY created_at DESC LIMIT 1;" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  OR manually verify with:" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  UPDATE patients SET email_verified = true" -ForegroundColor Cyan
    Write-Host "  WHERE id = '$patientId';" -ForegroundColor Cyan
    Write-Host ""
    
    # For development, let's try to verify directly
    Write-Host "Step 3: Manual Verification (Development Only)..." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  In production, patient would click email link." -ForegroundColor Gray
    Write-Host "  For development, we can manually verify in the database." -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Run this SQL command:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  UPDATE patients" -ForegroundColor Cyan
    Write-Host "  SET email_verified = true" -ForegroundColor Cyan
    Write-Host "  WHERE id = '$patientId';" -ForegroundColor Cyan
    Write-Host ""
    
} catch {
    $statusCode = $_.Exception.Response.StatusCode.Value__
    
    if ($statusCode -eq 409) {
        Write-Host "? Patient already exists!" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  The patient is already registered." -ForegroundColor Gray
        Write-Host "  Checking if email is verified..." -ForegroundColor Gray
        Write-Host ""
        Write-Host "  Run this SQL to check:" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  SELECT id, email, email_verified, created_at" -ForegroundColor Cyan
        Write-Host "  FROM patients" -ForegroundColor Cyan
        Write-Host "  WHERE email = '$email';" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "  If email_verified is false, run:" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  UPDATE patients" -ForegroundColor Cyan
        Write-Host "  SET email_verified = true" -ForegroundColor Cyan
        Write-Host "  WHERE email = '$email';" -ForegroundColor Cyan
        Write-Host ""
    } else {
        Write-Host "? Registration failed: HTTP $statusCode" -ForegroundColor Red
        Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host ""
        
        if ($_.ErrorDetails.Message) {
            try {
                $errorBody = $_.ErrorDetails.Message | ConvertFrom-Json
                Write-Host "  Details: $($errorBody | ConvertTo-Json -Depth 5)" -ForegroundColor Gray
            } catch {
                Write-Host "  Raw error: $($_.ErrorDetails.Message)" -ForegroundColor Gray
            }
        }
    }
    exit 1
}

Write-Host ""
Write-Host "=== NEXT STEPS ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Run the SQL command above to verify email" -ForegroundColor White
Write-Host "2. Then test login:" -ForegroundColor White
Write-Host ""
Write-Host "   .\test-backend-deviceid.ps1" -ForegroundColor Green
Write-Host ""
Write-Host "3. If login succeeds, test the full flow!" -ForegroundColor White
Write-Host ""
