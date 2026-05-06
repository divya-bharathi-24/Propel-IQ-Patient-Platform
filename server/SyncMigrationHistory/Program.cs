using Npgsql;

Console.WriteLine("=== EF Migrations History Sync Tool ===\n");
Console.WriteLine("This tool ensures the __EFMigrationsHistory table includes all applied schema changes.\n");

var connectionString = "Server=127.0.0.1;Port=5433;User Id=postgres;Password=admin;Database=propeliq_dev1;";

// Migrations that were applied manually but not recorded in __EFMigrationsHistory
var missingMigrations = new[]
{
    ("20260422030000_AddPatientIdToRefreshTokens", "10.0.0"),
    ("20260501163721_ConvertDateOfBirthToTextForEncryption", "10.0.0")
};

try
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    Console.WriteLine("✓ Connected to database\n");

    foreach (var (migrationId, productVersion) in missingMigrations)
    {
        Console.WriteLine($"Checking migration: {migrationId}");

        // Check if already recorded
        await using var checkCmd = new NpgsqlCommand(
            @"SELECT COUNT(*) FROM ""__EFMigrationsHistory"" WHERE migration_id = @migrationId", conn);
        checkCmd.Parameters.AddWithValue("migrationId", migrationId);

        var count = (long)(await checkCmd.ExecuteScalarAsync() ?? 0L);

        if (count > 0)
        {
            Console.WriteLine($"  ✓ Already recorded in history\n");
        }
        else
        {
            Console.WriteLine($"  ⚠ Not recorded - adding to history...");

            await using var insertCmd = new NpgsqlCommand(
                @"INSERT INTO ""__EFMigrationsHistory"" (migration_id, product_version) 
                  VALUES (@migrationId, @productVersion)", conn);
            insertCmd.Parameters.AddWithValue("migrationId", migrationId);
            insertCmd.Parameters.AddWithValue("productVersion", productVersion);

            await insertCmd.ExecuteNonQueryAsync();
            Console.WriteLine($"  ✓ Added to migration history\n");
        }
    }

    // Display current migration history
    Console.WriteLine("=== Current Migration History ===");
    await using (var cmd = new NpgsqlCommand(
        @"SELECT migration_id FROM ""__EFMigrationsHistory"" ORDER BY migration_id", conn))
    {
        await using var reader = await cmd.ExecuteReaderAsync();
        var count = 0;
        while (await reader.ReadAsync())
        {
            count++;
            Console.WriteLine($"  {count,3}. {reader.GetString(0)}");
        }
        Console.WriteLine($"\n✓ Total: {count} migrations recorded");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ Error: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
    return 1;
}

Console.WriteLine("\n✓ Migration history is now synchronized!");
return 0;
