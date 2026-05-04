using Npgsql;
using System;
using System.Threading.Tasks;

namespace DatabaseVerification
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var connectionString = "Host=ep-divine-mode-amg8nhin.c-5.us-east-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_QAz7gjyI8WHk;SSL Mode=Require;Trust Server Certificate=true;";
            
            Console.WriteLine("========================================");
            Console.WriteLine("Database Schema Verification");
            Console.WriteLine("========================================");
            Console.WriteLine();

            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();
                
                Console.WriteLine("? Database connected successfully!");
                Console.WriteLine();

                // Check 1: PHI columns
                Console.WriteLine("Check 1: Verifying PHI column types...");
                var phiColumnsQuery = @"
                    SELECT column_name, data_type, character_maximum_length
                    FROM information_schema.columns
                    WHERE table_name = 'patients'
                    AND column_name IN ('date_of_birth', 'name', 'phone')
                    ORDER BY column_name;";

                await using (var cmd = new NpgsqlCommand(phiColumnsQuery, conn))
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    bool needsPhiFix = false;
                    Console.WriteLine("  Column Name       | Data Type    | Max Length");
                    Console.WriteLine("  ------------------|--------------|------------");
                    
                    while (await reader.ReadAsync())
                    {
                        var columnName = reader.GetString(0);
                        var dataType = reader.GetString(1);
                        var maxLength = reader.IsDBNull(2) ? "N/A" : reader.GetInt32(2).ToString();
                        
                        Console.WriteLine($"  {columnName,-18}| {dataType,-13}| {maxLength}");
                        
                        if (dataType != "text")
                        {
                            needsPhiFix = true;
                        }
                    }
                    
                    Console.WriteLine();
                    if (needsPhiFix)
                    {
                        Console.WriteLine("  ??  PHI columns are NOT text type. fix_phi_columns.sql needs to be applied.");
                    }
                    else
                    {
                        Console.WriteLine("  ? PHI columns are already text type. No action needed.");
                    }
                }

                Console.WriteLine();

                // Check 2: patient_id in refresh_tokens
                Console.WriteLine("Check 2: Verifying patient_id column in refresh_tokens...");
                var patientIdQuery = @"
                    SELECT column_name, data_type, is_nullable
                    FROM information_schema.columns 
                    WHERE table_name = 'refresh_tokens' AND column_name = 'patient_id';";

                await using (var cmd = new NpgsqlCommand(patientIdQuery, conn))
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        var columnName = reader.GetString(0);
                        var dataType = reader.GetString(1);
                        var isNullable = reader.GetString(2);
                        
                        Console.WriteLine($"  Column found: {columnName} ({dataType}, nullable: {isNullable})");
                        Console.WriteLine("  ? patient_id column exists. No action needed.");
                    }
                    else
                    {
                        Console.WriteLine("  ? patient_id column does NOT exist.");
                        Console.WriteLine("  Action required: Run add_patient_id_to_refresh_tokens.sql");
                    }
                }

                Console.WriteLine();

                // Check 3: Migration history
                Console.WriteLine("Check 3: Latest applied migrations...");
                var migrationHistoryQuery = @"
                    SELECT migration_id, product_version 
                    FROM ""__EFMigrationsHistory"" 
                    ORDER BY migration_id DESC 
                    LIMIT 5;";

                await using (var cmd = new NpgsqlCommand(migrationHistoryQuery, conn))
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    Console.WriteLine("  Migration ID                           | EF Version");
                    Console.WriteLine("  ---------------------------------------|------------");
                    
                    while (await reader.ReadAsync())
                    {
                        var migrationId = reader.GetString(0);
                        var version = reader.GetString(1);
                        Console.WriteLine($"  {migrationId,-39}| {version}");
                    }
                }

                Console.WriteLine();
                Console.WriteLine("========================================");
                Console.WriteLine("Verification Complete");
                Console.WriteLine("========================================");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("Stack trace:");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
