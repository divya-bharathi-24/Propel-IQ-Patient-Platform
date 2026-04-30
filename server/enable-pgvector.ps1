# Enable pgvector Extension in PostgreSQL
# Run this script to install the pgvector extension required for AI RAG features

Write-Host "=== Enabling pgvector Extension ===" -ForegroundColor Cyan

# Get connection string from environment or appsettings
$connectionString = $env:DATABASE_URL
if (-not $connectionString) {
    $appsettings = Get-Content "Propel.Api.Gateway\appsettings.Development.json" | ConvertFrom-Json
    $connectionString = $appsettings.ConnectionStrings.DefaultConnection
}

if (-not $connectionString) {
    Write-Host "ERROR: Could not find DATABASE_URL or ConnectionStrings:DefaultConnection" -ForegroundColor Red
    Write-Host "Please set DATABASE_URL environment variable or update appsettings.Development.json" -ForegroundColor Yellow
    exit 1
}

Write-Host "Connection string found: $($connectionString.Substring(0, 50))..." -ForegroundColor Green

# Create SQL command to enable vector extension
$sqlCommand = @"
-- Enable pgvector extension (required for AI RAG features - US_040)
-- This must be run by a superuser or database owner
CREATE EXTENSION IF NOT EXISTS vector;

-- Verify extension is installed
SELECT extname, extversion FROM pg_extension WHERE extname = 'vector';
"@

# Save to temp file
$tempSqlFile = "enable-pgvector.sql"
$sqlCommand | Out-File -FilePath $tempSqlFile -Encoding UTF8

Write-Host "`nSQL Command to execute:" -ForegroundColor Cyan
Write-Host $sqlCommand -ForegroundColor Gray

Write-Host "`n=== Attempting to enable pgvector extension ===" -ForegroundColor Cyan

try {
    # Try using psql if available
    $psqlPath = Get-Command psql -ErrorAction SilentlyContinue
    
    if ($psqlPath) {
        Write-Host "Found psql, executing SQL..." -ForegroundColor Green
        psql $connectionString -f $tempSqlFile
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "`n? pgvector extension enabled successfully!" -ForegroundColor Green
        } else {
            Write-Host "`n??  psql returned exit code $LASTEXITCODE" -ForegroundColor Yellow
        }
    } else {
        Write-Host "`npsql command not found in PATH" -ForegroundColor Yellow
        Write-Host "`nPlease install pgvector extension manually:" -ForegroundColor Cyan
        Write-Host "1. Connect to your PostgreSQL database" -ForegroundColor White
        Write-Host "2. Run: CREATE EXTENSION IF NOT EXISTS vector;" -ForegroundColor White
        Write-Host "`nOr install psql and run this script again" -ForegroundColor White
    }
}
catch {
    Write-Host "`nError executing SQL: $_" -ForegroundColor Red
    Write-Host "`nPlease enable pgvector extension manually:" -ForegroundColor Cyan
    Write-Host "1. Connect to your PostgreSQL database as superuser" -ForegroundColor White
    Write-Host "2. Run: CREATE EXTENSION IF NOT EXISTS vector;" -ForegroundColor White
}
finally {
    # Cleanup
    if (Test-Path $tempSqlFile) {
        Remove-Item $tempSqlFile
    }
}

Write-Host "`n=== Next Steps ===" -ForegroundColor Cyan
Write-Host "1. Ensure pgvector extension is enabled (see above)" -ForegroundColor White
Write-Host "2. Restart the application" -ForegroundColor White
Write-Host "3. Migrations will run automatically on startup" -ForegroundColor White
