using Npgsql;
using System;
using System.IO;
using System.Threading.Tasks;

namespace DatabaseVerification
{
    class ApplySqlScripts
    {
        static async Task Main(string[] args)
        {
            var connectionString = "Host=ep-divine-mode-amg8nhin.c-5.us-east-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_QAz7gjyI8WHk;SSL Mode=Require;Trust Server Certificate=true;";
            
            Console.WriteLine("========================================");
            Console.WriteLine("Applying SQL Scripts to Database");
            Console.WriteLine("========================================");
            Console.WriteLine();

            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();
                
                Console.WriteLine("? Database connected successfully!");
                Console.WriteLine();

                // Apply fix_phi_columns.sql
                Console.WriteLine("Step 1: Applying fix_phi_columns.sql...");
                var phiSql = @"
                    -- Update date_of_birth from DATE to TEXT
                    ALTER TABLE patients 
                    ALTER COLUMN date_of_birth TYPE text 
                    USING date_of_birth::text;

                    -- Update name from VARCHAR(200) to TEXT  
                    ALTER TABLE patients 
                    ALTER COLUMN name TYPE text;

                    -- Update phone from VARCHAR(30) to TEXT
                    ALTER TABLE patients 
                    ALTER COLUMN phone TYPE text;
                ";

                await using (var cmd = new NpgsqlCommand(phiSql, conn))
                {
                    await cmd.ExecuteNonQueryAsync();
                    Console.WriteLine("  ? PHI columns converted to TEXT successfully!");
                }

                Console.WriteLine();

                // Apply add_patient_id_to_refresh_tokens.sql
                Console.WriteLine("Step 2: Applying add_patient_id_to_refresh_tokens.sql...");
                var refreshTokenSql = @"
                    -- Step 1: Drop existing composite index
                    DROP INDEX IF EXISTS ix_refresh_tokens_user_id_family_id;

                    -- Step 2: Drop existing FK constraint
                    ALTER TABLE refresh_tokens 
                    DROP CONSTRAINT IF EXISTS fk_refresh_tokens_users_user_id;

                    -- Step 3: Make user_id nullable
                    ALTER TABLE refresh_tokens 
                    ALTER COLUMN user_id DROP NOT NULL;

                    -- Step 4: Add patient_id column (nullable)
                    ALTER TABLE refresh_tokens 
                    ADD COLUMN IF NOT EXISTS patient_id uuid NULL;

                    -- Step 5: Re-create FK constraint to users table
                    ALTER TABLE refresh_tokens
                    ADD CONSTRAINT fk_refresh_tokens_users_user_id
                    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE;

                    -- Step 6: Create partial composite index on (user_id, family_id) for staff/admin tokens
                    CREATE INDEX IF NOT EXISTS ix_refresh_tokens_user_id_family_id 
                    ON refresh_tokens (user_id, family_id)
                    WHERE user_id IS NOT NULL;

                    -- Step 7: Create partial composite index on (patient_id, family_id) for patient tokens
                    CREATE INDEX IF NOT EXISTS ix_refresh_tokens_patient_id_family_id 
                    ON refresh_tokens (patient_id, family_id)
                    WHERE patient_id IS NOT NULL;

                    -- Step 8: Create FK constraint to patients table
                    ALTER TABLE refresh_tokens
                    ADD CONSTRAINT fk_refresh_tokens_patients_patient_id
                    FOREIGN KEY (patient_id) REFERENCES patients(id) ON DELETE CASCADE;

                    -- Step 9: Add CHECK constraint: exactly one of patient_id or user_id must be non-null
                    ALTER TABLE refresh_tokens
                    DROP CONSTRAINT IF EXISTS ck_refresh_tokens_patient_or_user;
                    
                    ALTER TABLE refresh_tokens
                    ADD CONSTRAINT ck_refresh_tokens_patient_or_user
                    CHECK (
                        (patient_id IS NOT NULL AND user_id IS NULL) OR
                        (patient_id IS NULL AND user_id IS NOT NULL)
                    );
                ";

                await using (var cmd = new NpgsqlCommand(refreshTokenSql, conn))
                {
                    await cmd.ExecuteNonQueryAsync();
                    Console.WriteLine("  ? refresh_tokens table updated with patient_id support!");
                }

                Console.WriteLine();

                // Verify changes
                Console.WriteLine("Step 3: Verifying changes...");
                Console.WriteLine();

                // Verify PHI columns
                var phiVerifyQuery = @"
                    SELECT column_name, data_type
                    FROM information_schema.columns
                    WHERE table_name = 'patients'
                    AND column_name IN ('date_of_birth', 'name', 'phone')
                    ORDER BY column_name;";

                await using (var cmd = new NpgsqlCommand(phiVerifyQuery, conn))
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    Console.WriteLine("  PHI Columns:");
                    Console.WriteLine("  Column Name       | Data Type");
                    Console.WriteLine("  ------------------|----------");
                    
                    while (await reader.ReadAsync())
                    {
                        var columnName = reader.GetString(0);
                        var dataType = reader.GetString(1);
                        Console.WriteLine($"  {columnName,-18}| {dataType}");
                    }
                }

                Console.WriteLine();

                // Verify refresh_tokens
                var refreshVerifyQuery = @"
                    SELECT column_name, data_type, is_nullable
                    FROM information_schema.columns 
                    WHERE table_name = 'refresh_tokens' 
                    AND column_name IN ('patient_id', 'user_id')
                    ORDER BY column_name;";

                await using (var cmd = new NpgsqlCommand(refreshVerifyQuery, conn))
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    Console.WriteLine("  Refresh Token Columns:");
                    Console.WriteLine("  Column Name       | Data Type    | Nullable");
                    Console.WriteLine("  ------------------|--------------|----------");
                    
                    while (await reader.ReadAsync())
                    {
                        var columnName = reader.GetString(0);
                        var dataType = reader.GetString(1);
                        var isNullable = reader.GetString(2);
                        Console.WriteLine($"  {columnName,-18}| {dataType,-13}| {isNullable}");
                    }
                }

                Console.WriteLine();
                Console.WriteLine("========================================");
                Console.WriteLine("?? All SQL scripts applied successfully!");
                Console.WriteLine("========================================");
                Console.WriteLine();
                Console.WriteLine("Summary of changes:");
                Console.WriteLine("  ? PHI columns (name, phone, date_of_birth) converted to TEXT");
                Console.WriteLine("  ? refresh_tokens table now supports both patients and users");
                Console.WriteLine("  ? Database is ready for use!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("Stack trace:");
                Console.WriteLine(ex.StackTrace);
                Environment.Exit(1);
            }
        }
    }
}
