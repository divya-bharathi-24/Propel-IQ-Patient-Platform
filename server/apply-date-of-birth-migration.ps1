$connectionString = "Server=127.0.0.1;Port=5433;User Id=postgres;Password=admin;Database=propeliq_dev1;"

$sql = @"
ALTER TABLE patients 
ALTER COLUMN date_of_birth TYPE text 
USING date_of_birth::text;
"@

try {
	Add-Type -Path ".\Propel.Api.Gateway\bin\Debug\net10.0\Npgsql.dll"

	$connection = New-Object Npgsql.NpgsqlConnection($connectionString)
	$connection.Open()

	$command = $connection.CreateCommand()
	$command.CommandText = $sql

	$result = $command.ExecuteNonQuery()
	Write-Host "Successfully altered date_of_birth column type from date to text." -ForegroundColor Green
	Write-Host "Rows affected: $result" -ForegroundColor Green

	$connection.Close()
	Write-Host "Migration applied successfully!" -ForegroundColor Green
}
catch {
	Write-Host "Error executing SQL: $_" -ForegroundColor Red
	exit 1
}
