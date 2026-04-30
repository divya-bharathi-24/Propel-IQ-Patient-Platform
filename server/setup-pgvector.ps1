# Setup PostgreSQL with pgvector for Propel IQ Development

Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "  Propel IQ - PostgreSQL with pgvector Setup" -ForegroundColor Cyan
Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

# Check if Docker is running
Write-Host "[1/5] Checking Docker..." -ForegroundColor Yellow
$dockerRunning = docker info 2>&1 | Select-String "Server Version"
if (-not $dockerRunning) {
    Write-Host "? ERROR: Docker is not running!" -ForegroundColor Red
    Write-Host "  Please start Docker Desktop and try again." -ForegroundColor Red
    exit 1
}
Write-Host "? Docker is running" -ForegroundColor Green
Write-Host ""

# Stop any existing container
Write-Host "[2/5] Stopping existing PostgreSQL containers..." -ForegroundColor Yellow
docker stop propel-postgres 2>$null
docker rm propel-postgres 2>$null
Write-Host "? Cleaned up existing containers" -ForegroundColor Green
Write-Host ""

# Start PostgreSQL with pgvector
Write-Host "[3/5] Starting PostgreSQL with pgvector..." -ForegroundColor Yellow
docker-compose up -d

if ($LASTEXITCODE -ne 0) {
    Write-Host "? ERROR: Failed to start PostgreSQL" -ForegroundColor Red
    exit 1
}
Write-Host "? PostgreSQL container started" -ForegroundColor Green
Write-Host ""

# Wait for PostgreSQL to be ready
Write-Host "[4/5] Waiting for PostgreSQL to be ready..." -ForegroundColor Yellow
$maxAttempts = 30
$attempt = 0

do {
    $attempt++
    Start-Sleep -Seconds 1
    $ready = docker exec propel-postgres pg_isready -U postgres 2>&1
    if ($ready -match "accepting connections") {
        Write-Host "? PostgreSQL is ready!" -ForegroundColor Green
        break
    }
    Write-Host "  Attempt $attempt/$maxAttempts..." -ForegroundColor Gray
} while ($attempt -lt $maxAttempts)

if ($attempt -eq $maxAttempts) {
    Write-Host "? ERROR: PostgreSQL failed to start within 30 seconds" -ForegroundColor Red
    Write-Host "  Check logs: docker logs propel-postgres" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Verify pgvector extension is available
Write-Host "[5/5] Verifying pgvector extension..." -ForegroundColor Yellow
$extensionCheck = docker exec propel-postgres psql -U postgres -d propeliq -c "CREATE EXTENSION IF NOT EXISTS vector; SELECT extname, extversion FROM pg_extension WHERE extname = 'vector';" 2>&1

if ($extensionCheck -match "vector") {
    Write-Host "? pgvector extension is installed and ready!" -ForegroundColor Green
} else {
    Write-Host "? WARNING: pgvector extension verification failed" -ForegroundColor Yellow
    Write-Host "  This might be okay if it's the first run" -ForegroundColor Yellow
}
Write-Host ""

Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "  Setup Complete!" -ForegroundColor Green
Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""
Write-Host "PostgreSQL Details:" -ForegroundColor Cyan
Write-Host "  Host:     localhost" -ForegroundColor White
Write-Host "  Port:     5432" -ForegroundColor White
Write-Host "  Database: propeliq" -ForegroundColor White
Write-Host "  Username: postgres" -ForegroundColor White
Write-Host "  Password: postgres" -ForegroundColor White
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "  1. Uncomment pgvector code (see PGVECTOR_SETUP_GUIDE.md)" -ForegroundColor White
Write-Host "  2. Run: dotnet run --project Propel.Api.Gateway" -ForegroundColor White
Write-Host ""
Write-Host "Useful Commands:" -ForegroundColor Cyan
Write-Host "  Stop:   docker-compose down" -ForegroundColor Gray
Write-Host "  Logs:   docker logs propel-postgres" -ForegroundColor Gray
Write-Host "  Shell:  docker exec -it propel-postgres psql -U postgres -d propeliq" -ForegroundColor Gray
Write-Host ""
