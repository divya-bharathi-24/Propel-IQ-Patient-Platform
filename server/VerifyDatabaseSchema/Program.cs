using Npgsql;
using System.Text;

Console.WriteLine("=== Database Schema Verification Tool ===\n");
Console.WriteLine("This tool compares your actual database schema with EF Core expectations.\n");

var connectionString = "Server=127.0.0.1;Port=5433;User Id=postgres;Password=admin;Database=propeliq_dev1;";

try
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    Console.WriteLine("✓ Connected to database\n");

    // Check 1: Verify date_of_birth is text type
    Console.WriteLine("1. Checking patients.date_of_birth column type...");
    await using (var cmd = new NpgsqlCommand(
        @"SELECT data_type FROM information_schema.columns 
          WHERE table_name = 'patients' AND column_name = 'date_of_birth'", conn))
    {
        var dataType = await cmd.ExecuteScalarAsync();
        if (dataType?.ToString() == "text")
            Console.WriteLine("   ✓ PASS: date_of_birth is text (required for PHI encryption)");
        else
            Console.WriteLine($"   ❌ FAIL: date_of_birth is {dataType}, expected text");
    }

    // Check 2: Verify patients.name is text type
    Console.WriteLine("\n2. Checking patients.name column type...");
    await using (var cmd = new NpgsqlCommand(
        @"SELECT data_type FROM information_schema.columns 
          WHERE table_name = 'patients' AND column_name = 'name'", conn))
    {
        var dataType = await cmd.ExecuteScalarAsync();
        if (dataType?.ToString() == "text")
            Console.WriteLine("   ✓ PASS: name is text (required for PHI encryption)");
        else
            Console.WriteLine($"   ❌ FAIL: name is {dataType}, expected text");
    }

    // Check 3: Verify patients.phone is text type
    Console.WriteLine("\n3. Checking patients.phone column type...");
    await using (var cmd = new NpgsqlCommand(
        @"SELECT data_type FROM information_schema.columns 
          WHERE table_name = 'patients' AND column_name = 'phone'", conn))
    {
        var dataType = await cmd.ExecuteScalarAsync();
        if (dataType?.ToString() == "text")
            Console.WriteLine("   ✓ PASS: phone is text (required for PHI encryption)");
        else
            Console.WriteLine($"   ❌ FAIL: phone is {dataType}, expected text");
    }

    // Check 4: Verify refresh_tokens has patient_id column
    Console.WriteLine("\n4. Checking refresh_tokens.patient_id column exists...");
    await using (var cmd = new NpgsqlCommand(
        @"SELECT column_name FROM information_schema.columns 
          WHERE table_name = 'refresh_tokens' AND column_name = 'patient_id'", conn))
    {
        var exists = await cmd.ExecuteScalarAsync();
        if (exists != null)
            Console.WriteLine("   ✓ PASS: patient_id column exists");
        else
            Console.WriteLine("   ❌ FAIL: patient_id column is missing");
    }

    // Check 5: Verify refresh_tokens.user_id is nullable
    Console.WriteLine("\n5. Checking refresh_tokens.user_id is nullable...");
    await using (var cmd = new NpgsqlCommand(
        @"SELECT is_nullable FROM information_schema.columns 
          WHERE table_name = 'refresh_tokens' AND column_name = 'user_id'", conn))
    {
        var result = await cmd.ExecuteScalarAsync();
        var isNullable = result?.ToString();
        if (isNullable == "YES")
            Console.WriteLine("   ✓ PASS: user_id is nullable");
        else
            Console.WriteLine($"   ❌ FAIL: user_id is_nullable={isNullable}, expected YES");
    }

    // Check 6: Verify refresh_tokens CHECK constraint exists
    Console.WriteLine("\n6. Checking refresh_tokens CHECK constraint...");
    await using (var cmd = new NpgsqlCommand(
        @"SELECT constraint_name FROM information_schema.table_constraints 
          WHERE table_name = 'refresh_tokens' 
          AND constraint_type = 'CHECK' 
          AND constraint_name = 'ck_refresh_tokens_patient_or_user'", conn))
    {
        var exists = await cmd.ExecuteScalarAsync();
        if (exists != null)
            Console.WriteLine("   ✓ PASS: CHECK constraint exists (ensures exactly one of patient_id or user_id)");
        else
            Console.WriteLine("   ❌ FAIL: CHECK constraint is missing");
    }

    // Check 7: List all applied migrations
    Console.WriteLine("\n7. Checking applied migrations...");
    await using (var cmd = new NpgsqlCommand(
        @"SELECT migration_id FROM ""__EFMigrationsHistory"" 
          ORDER BY migration_id", conn))
    {
        await using var reader = await cmd.ExecuteReaderAsync();
        var count = 0;
        var lastMigration = "";
        while (await reader.ReadAsync())
        {
            count++;
            lastMigration = reader.GetString(0);
        }
        Console.WriteLine($"   ✓ {count} migrations applied");
        Console.WriteLine($"   Last migration: {lastMigration}");
    }

    // Check 8: Verify critical indexes exist
    Console.WriteLine("\n8. Checking critical indexes...");
    var requiredIndexes = new[]
    {
        "ix_refresh_tokens_patient_id",
        "ix_refresh_tokens_patient_id_family_id",
        "ix_refresh_tokens_user_id_family_id",
        "ix_refresh_tokens_token_hash"
    };

    foreach (var indexName in requiredIndexes)
    {
        await using var cmd = new NpgsqlCommand(
            @"SELECT indexname FROM pg_indexes 
              WHERE tablename = 'refresh_tokens' AND indexname = @indexName", conn);
        cmd.Parameters.AddWithValue("indexName", indexName);

        var exists = await cmd.ExecuteScalarAsync();
        if (exists != null)
            Console.WriteLine($"   ✓ Index {indexName} exists");
        else
            Console.WriteLine($"   ❌ Index {indexName} is missing");
    }

    Console.WriteLine("\n" + new string('=', 60));
    Console.WriteLine("✓ Schema verification complete!");
    Console.WriteLine(new string('=', 60));
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ Error: {ex.Message}");
    return 1;
}

return 0;
