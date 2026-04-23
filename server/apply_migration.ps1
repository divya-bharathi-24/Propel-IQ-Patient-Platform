# PowerShell script to apply the RefreshToken migration
# Run this script from the server directory

Write-Host "Applying RefreshToken migration..." -ForegroundColor Yellow

$connectionString = "Server=127.0.0.1;Port=5434;User Id=postgres;Password=Jothis@10;Database=propeliq_dev;"
$sqlFilePath = "add_patient_id_to_refresh_tokens.sql"

# Check if SQL file exists
if (!(Test-Path $sqlFilePath)) {
    Write-Host "Error: SQL file not found at $sqlFilePath" -ForegroundColor Red
    exit 1
}

# Read SQL commands
$sql = Get-Content $sqlFilePath -Raw

# Execute using dotnet script
$csCode = @"
using System;
using Npgsql;

var connectionString = @"$connectionString";
var sql = @"$sql";

try {
    using var connection = new NpgsqlConnection(connectionString);
    connection.Open();
    Console.WriteLine("Connected to database");
    
    using var command = new NpgsqlCommand(sql, connection);
    command.ExecuteNonQuery();
    
    Console.WriteLine("Migration applied successfully!");
}
catch (Exception ex) {
    Console.WriteLine("Error: " + ex.Message);
    Environment.Exit(1);
}
"@

# Save C# code to temp file
$tempCsFile = [System.IO.Path]::GetTempFileName() + ".cs"
$csCode | Out-File -FilePath $tempCsFile -Encoding UTF8

# Execute using dotnet-script if available, otherwise provide instructions
try {
    dotnet script $tempCsFile
}
catch {
    Write-Host "dotnet-script not found. Installing..." -ForegroundColor Yellow
    dotnet tool install -g dotnet-script
    dotnet script $tempCsFile
}
finally {
    Remove-Item $tempCsFile -ErrorAction SilentlyContinue
}
"@