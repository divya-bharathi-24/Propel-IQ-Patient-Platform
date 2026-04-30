# Verify pgvector Setup and Application Status

Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "  Propel IQ - pgvector Status Check" -ForegroundColor Cyan
Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

# Check if code is commented out
Write-Host "[1/4] Checking code status..." -ForegroundColor Yellow
$programCs = Get-Content "Propel.Api.Gateway\Program.cs" -Raw
$appDbContext = Get-Content "Propel.Api.Gateway\Data\AppDbContext.cs" -Raw
$embeddingConfig = Get-Content "Propel.Api.Gateway\Data\Configurations\DocumentChunkEmbeddingConfiguration.cs" -Raw

$useVectorCommented = $programCs -match '//\s*dataSourceBuilder\.UseVector\(\)'
$hasExtensionCommented = $appDbContext -match '//\s*modelBuilder\.HasPostgresExtension'
$embeddingCommented = $embeddingConfig -match '//\s*var embeddingConverter'

if ($useVectorCommented -and $hasExtensionCommented) {
if ($useVectorCommented -and $hasExtensionCommented -and $embeddingCommented) {
    Write-Host "? pgvector code is properly commented out" -ForegroundColor Green
    Write-Host "  Status: Application can run WITHOUT pgvector" -ForegroundColor White
} else {
    Write-Host "? pgvector code is enabled" -ForegroundColor Green
    Write-Host "  Status: Application REQUIRES pgvector to be installed" -ForegroundColor White
}
Write-Host ""

# Check if Docker is running
Write-Host "[2/4] Checking Docker status..." -ForegroundColor Yellow
$dockerRunning = docker info 2>&1 | Select-String "Server Version"
if ($dockerRunning) {
    Write-Host "? Docker is running" -ForegroundColor Green
} else {
    Write-Host "? Docker is not running" -ForegroundColor Red
    Write-Host "  Install Docker Desktop: https://www.docker.com/products/docker-desktop/" -ForegroundColor Gray
}
Write-Host ""

# Check if PostgreSQL container exists
Write-Host "[3/4] Checking PostgreSQL container..." -ForegroundColor Yellow
$containerExists = docker ps -a --filter "name=propel-postgres" --format "{{.Names}}" 2>$null
if ($containerExists) {
    $containerRunning = docker ps --filter "name=propel-postgres" --format "{{.Names}}" 2>$null
    if ($containerRunning) {
        Write-Host "? propel-postgres container is RUNNING" -ForegroundColor Green
        
        # Check if pgvector extension is installed
        Write-Host "[4/4] Checking pgvector extension..." -ForegroundColor Yellow
        $extensionCheck = docker exec propel-postgres psql -U postgres -d propeliq -c "SELECT extname, extversion FROM pg_extension WHERE extname = 'vector';" 2>&1
        
        if ($extensionCheck -match "vector") {
            $version = ($extensionCheck -split "`n" | Select-String "vector" | Out-String).Trim()
            Write-Host "? pgvector extension is INSTALLED" -ForegroundColor Green
            Write-Host "  Version: $version" -ForegroundColor White
        } else {
            Write-Host "? pgvector extension NOT installed" -ForegroundColor Red
            Write-Host "  Run: docker exec propel-postgres psql -U postgres -d propeliq -c ""CREATE EXTENSION vector;""" -ForegroundColor Gray
        }
    } else {
        Write-Host "? propel-postgres container EXISTS but is STOPPED" -ForegroundColor Yellow
        Write-Host "  Start it: docker start propel-postgres" -ForegroundColor Gray
    }
} else {
    Write-Host "? propel-postgres container not found" -ForegroundColor Red
    Write-Host "  Create it: .\setup-pgvector.ps1" -ForegroundColor Gray
}
Write-Host ""

# Summary
Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "  Summary" -ForegroundColor Cyan
Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

if ($useVectorCommented -and $hasExtensionCommented -and $embeddingCommented) {
    Write-Host "? READY TO RUN" -ForegroundColor Green
    Write-Host ""
    Write-Host "Application can start WITHOUT pgvector:" -ForegroundColor White
    Write-Host "  dotnet run --project Propel.Api.Gateway" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "To enable pgvector features:" -ForegroundColor Yellow
    Write-Host "  1. Run: .\setup-pgvector.ps1" -ForegroundColor Gray
    Write-Host "  2. Uncomment 3 lines (see PGVECTOR_SETUP_GUIDE.md)" -ForegroundColor Gray
    Write-Host "  3. Restart application" -ForegroundColor Gray
} else {
    if ($containerRunning -and ($extensionCheck -match "vector")) {
        Write-Host "? PGVECTOR ENABLED & READY" -ForegroundColor Green
        Write-Host ""
        Write-Host "Application is configured to use pgvector:" -ForegroundColor White
        Write-Host "  dotnet run --project Propel.Api.Gateway" -ForegroundColor Cyan
    } else {
        Write-Host "?? NEEDS SETUP" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "pgvector is enabled but not available:" -ForegroundColor White
        Write-Host "  Option 1: Install pgvector: .\setup-pgvector.ps1" -ForegroundColor Cyan
        Write-Host "  Option 2: Comment out code (see PGVECTOR_SETUP_GUIDE.md)" -ForegroundColor Cyan
    }
}
Write-Host ""

# Show connection details if container is running
if ($containerRunning) {
    Write-Host "PostgreSQL Connection:" -ForegroundColor Cyan
    Write-Host "  Host: localhost" -ForegroundColor White
    Write-Host "  Port: 5432" -ForegroundColor White
    Write-Host "  Database: propeliq" -ForegroundColor White
    Write-Host "  Username: postgres" -ForegroundColor White
    Write-Host "  Password: postgres" -ForegroundColor White
    Write-Host ""
}

Write-Host "Documentation:" -ForegroundColor Cyan
Write-Host "  Quick Start: PGVECTOR_QUICKREF.md" -ForegroundColor Gray
Write-Host "  Full Guide:  PGVECTOR_SETUP_GUIDE.md" -ForegroundColor Gray
Write-Host "  Summary:     PGVECTOR_DISABLE_SUMMARY.md" -ForegroundColor Gray
Write-Host ""
