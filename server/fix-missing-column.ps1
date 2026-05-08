# Database Column Fix Script
# Fixes the missing pending_alerts_json column in the patients table

$connectionString = "Server=127.0.0.1;Port=5432;User Id=postgres;Password=admin;Database=propeliq_dev;"

Write-Host "=== Database Column Fix Utility ===" -ForegroundColor Cyan
Write-Host ""

# Load Npgsql
Add-Type -Path "D:\Siva Propel\Propel Latest\Propel-IQ-Patient-Platform\server\Propel.Api.Gateway\bin\Debug\net10.0\Npgsql.dll"

try {
	Write-Host "Connecting to database..." -ForegroundColor Yellow
	$connection = New-Object Npgsql.NpgsqlConnection($connectionString)
	$connection.Open()
	Write-Host "✓ Connected successfully" -ForegroundColor Green
	Write-Host ""

	# Check if column exists
	Write-Host "Checking if column 'pending_alerts_json' exists..." -ForegroundColor Yellow
	$checkColumnSql = @"
		SELECT EXISTS (
			SELECT 1 
			FROM information_schema.columns 
			WHERE table_name = 'patients' 
			  AND column_name = 'pending_alerts_json'
		);
"@

	$cmd = $connection.CreateCommand()
	$cmd.CommandText = $checkColumnSql
	$columnExists = $cmd.ExecuteScalar()

	if ($columnExists -eq $true) {
		Write-Host "✓ Column already exists" -ForegroundColor Green
	} else {
		Write-Host "✗ Column is missing - adding it now..." -ForegroundColor Red

		$addColumnSql = "ALTER TABLE patients ADD COLUMN pending_alerts_json jsonb NULL;"
		$addCmd = $connection.CreateCommand()
		$addCmd.CommandText = $addColumnSql
		$addCmd.ExecuteNonQuery() | Out-Null

		Write-Host "✓ Column added successfully!" -ForegroundColor Green
	}

	Write-Host ""

	# Check migration history
	Write-Host "Checking migration history..." -ForegroundColor Yellow
	$checkMigrationSql = @"
		SELECT EXISTS (
			SELECT 1 
			FROM "__EFMigrationsHistory" 
			WHERE migration_id = '20260422140000_AddPatientPendingAlerts'
		);
"@

	$migCmd = $connection.CreateCommand()
	$migCmd.CommandText = $checkMigrationSql
	$migrationExists = $migCmd.ExecuteScalar()

	if ($migrationExists -eq $true) {
		Write-Host "✓ Migration record already exists" -ForegroundColor Green
	} else {
		Write-Host "✗ Migration record missing - adding it now..." -ForegroundColor Red

		$addMigrationSql = @"
			INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
			VALUES ('20260422140000_AddPatientPendingAlerts', '9.0.15');
"@

		$migAddCmd = $connection.CreateCommand()
		$migAddCmd.CommandText = $addMigrationSql
		$migAddCmd.ExecuteNonQuery() | Out-Null

		Write-Host "✓ Migration record added successfully!" -ForegroundColor Green
	}

	Write-Host ""
	Write-Host "=== Fix Complete ===" -ForegroundColor Cyan
	Write-Host "The database schema is now in sync with the codebase." -ForegroundColor Green
	Write-Host "You can now restart your application and the error should be resolved." -ForegroundColor Green

} catch {
	Write-Host ""
	Write-Host "✗ Error: $($_.Exception.Message)" -ForegroundColor Red
	Write-Host ""
	Write-Host "Stack Trace:" -ForegroundColor Yellow
	Write-Host $_.Exception.StackTrace -ForegroundColor Gray
	exit 1
} finally {
	if ($connection) {
		$connection.Close()
		$connection.Dispose()
	}
}

Write-Host ""
Write-Host "Press any key to exit..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
