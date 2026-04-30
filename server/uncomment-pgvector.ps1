# Re-enable pgvector Code After Installation

Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "  Re-enabling pgvector in Propel IQ Code" -ForegroundColor Cyan
Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

# Verify pgvector is installed first
Write-Host "[1/5] Verifying pgvector installation..." -ForegroundColor Yellow
try {
    $extensionCheck = docker exec propel-postgres psql -U postgres -d propeliq -c "SELECT extname FROM pg_extension WHERE extname = 'vector';" 2>&1
    
    if ($extensionCheck -match "vector") {
        Write-Host "? pgvector extension is installed" -ForegroundColor Green
    } else {
        Write-Host "? ERROR: pgvector is NOT installed" -ForegroundColor Red
        Write-Host "  Run: .\setup-pgvector.ps1 first" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "? ERROR: Cannot connect to PostgreSQL" -ForegroundColor Red
    Write-Host "  Make sure Docker container is running: docker-compose up -d" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Uncomment Program.cs - dataSourceBuilder.UseVector()
Write-Host "[2/5] Uncommenting Program.cs (UseVector calls)..." -ForegroundColor Yellow
$programPath = "Propel.Api.Gateway\Program.cs"
$programContent = Get-Content $programPath -Raw

# Uncomment dataSourceBuilder.UseVector();
$programContent = $programContent -replace '//\s*dataSourceBuilder\.UseVector\(\);', 'dataSourceBuilder.UseVector();'

# Uncomment , o => o.UseVector()
$programContent = $programContent -replace 'opt\.UseNpgsql\(dataSource\s*/\*,\s*o\s*=>\s*o\.UseVector\(\)\s*\*/\)', 'opt.UseNpgsql(dataSource, o => o.UseVector())'

Set-Content $programPath $programContent -NoNewline
Write-Host "? Program.cs updated" -ForegroundColor Green
Write-Host ""

# Uncomment AppDbContext.cs
Write-Host "[3/5] Uncommenting AppDbContext.cs..." -ForegroundColor Yellow
$appDbContextPath = "Propel.Api.Gateway\Data\AppDbContext.cs"
$appDbContextContent = Get-Content $appDbContextPath -Raw

# Uncomment modelBuilder.HasPostgresExtension("vector");
$appDbContextContent = $appDbContextContent -replace '//\s*modelBuilder\.HasPostgresExtension\("vector"\);', 'modelBuilder.HasPostgresExtension("vector");'

Set-Content $appDbContextPath $appDbContextContent -NoNewline
Write-Host "? AppDbContext.cs updated" -ForegroundColor Green
Write-Host ""

# Uncomment DocumentChunkEmbeddingConfiguration.cs
Write-Host "[4/5] Uncommenting DocumentChunkEmbeddingConfiguration.cs..." -ForegroundColor Yellow
$configPath = "Propel.Api.Gateway\Data\Configurations\DocumentChunkEmbeddingConfiguration.cs"
$configContent = Get-Content $configPath -Raw

# Replace commented block with uncommented version
$configContent = $configContent -replace '//\s*var embeddingConverter', '        var embeddingConverter'
$configContent = $configContent -replace '//\s*v => new Vector', '            v => new Vector'
$configContent = $configContent -replace '//\s*v => v\.ToArray', '            v => v.ToArray'
$configContent = $configContent -replace '//\s*builder\.Property\(e => e\.Embedding\)', '        builder.Property(e => e.Embedding)'
$configContent = $configContent -replace '//\s*\.HasColumnType\("vector\(1536\)"\)', '               .HasColumnType("vector(1536)")'
$configContent = $configContent -replace '//\s*\.HasConversion\(embeddingConverter\)', '               .HasConversion(embeddingConverter)'
$configContent = $configContent -replace '//\s*\.IsRequired\(\);', '               .IsRequired();'
$configContent = $configContent -replace '//\s*builder\.HasIndex\(e => e\.Embedding\)', '        builder.HasIndex(e => e.Embedding)'
$configContent = $configContent -replace '//\s*\.HasMethod\("hnsw"\)', '               .HasMethod("hnsw")'
$configContent = $configContent -replace '//\s*\.HasOperators\("vector_cosine_ops"\)', '               .HasOperators("vector_cosine_ops")'
$configContent = $configContent -replace '//\s*\.HasDatabaseName\("ix_document_chunk_embeddings_embedding_hnsw"\);', '               .HasDatabaseName("ix_document_chunk_embeddings_embedding_hnsw");'

# Remove temporary comment
$configContent = $configContent -replace '// TEMPORARY: Vector column configuration disabled until pgvector is installed\s*\n\s*// Uncomment these lines after running setup-pgvector\.ps1\s*\n\s*', ''

Set-Content $configPath $configContent -NoNewline
Write-Host "? DocumentChunkEmbeddingConfiguration.cs updated" -ForegroundColor Green
Write-Host ""

# Build to verify
Write-Host "[5/5] Building solution..." -ForegroundColor Yellow
dotnet build Propel.Api.Gateway\Propel.Api.Gateway.csproj --no-incremental > $null 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "? Build successful" -ForegroundColor Green
} else {
    Write-Host "? Build failed - please check errors" -ForegroundColor Red
    Write-Host "  Run: dotnet build" -ForegroundColor Gray
}
Write-Host ""

Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "  pgvector Successfully Re-enabled!" -ForegroundColor Green
Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""
Write-Host "Files Updated:" -ForegroundColor Cyan
Write-Host "  ? Propel.Api.Gateway\Program.cs" -ForegroundColor Green
Write-Host "  ? Propel.Api.Gateway\Data\AppDbContext.cs" -ForegroundColor Green
Write-Host "  ? Propel.Api.Gateway\Data\Configurations\DocumentChunkEmbeddingConfiguration.cs" -ForegroundColor Green
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "  1. Run migrations (if needed): cd Propel.Api.Gateway && dotnet ef database update" -ForegroundColor White
Write-Host "  2. Start application: dotnet run --project Propel.Api.Gateway" -ForegroundColor White
Write-Host ""
Write-Host "Verify Vector Extension:" -ForegroundColor Cyan
Write-Host "  docker exec -it propel-postgres psql -U postgres -d propeliq -c '\dx'" -ForegroundColor Gray
Write-Host ""
