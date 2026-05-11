using Npgsql;

// Connection string from launchSettings.json
const string connStr = "Host=ep-divine-mode-amg8nhin.c-5.us-east-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_QAz7gjyI8WHk;SSL Mode=Require;Trust Server Certificate=true";

Console.WriteLine("Connecting to Neon database...");

await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();

Console.WriteLine("Connected. Applying missing columns...");

// 1. Add severity column to no_show_risks if missing
await ExecuteSqlAsync(conn, """
    DO $$ 
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM information_schema.columns 
            WHERE table_name = 'no_show_risks' AND column_name = 'severity'
        ) THEN
            ALTER TABLE no_show_risks 
            ADD COLUMN severity VARCHAR(10) NOT NULL DEFAULT 'Medium';
            RAISE NOTICE 'Added severity column to no_show_risks';
        ELSE
            RAISE NOTICE 'severity column already exists in no_show_risks';
        END IF;
    END $$;
    """, "Add severity to no_show_risks");

// 2. Remove appointment_id1 shadow FK column from calendar_syncs if still exists
await ExecuteSqlAsync(conn, """
    DO $$ 
    BEGIN
        IF EXISTS (
            SELECT 1 FROM information_schema.columns 
            WHERE table_name = 'calendar_syncs' AND column_name = 'appointment_id1'
        ) THEN
            ALTER TABLE calendar_syncs DROP COLUMN appointment_id1;
            RAISE NOTICE 'Dropped appointment_id1 from calendar_syncs';
        ELSE
            RAISE NOTICE 'appointment_id1 does not exist in calendar_syncs (already clean)';
        END IF;
    END $$;
    """, "Remove appointment_id1 from calendar_syncs");

// 3. Remove appointment_id1 shadow FK column from notifications if still exists
await ExecuteSqlAsync(conn, """
    DO $$ 
    BEGIN
        IF EXISTS (
            SELECT 1 FROM information_schema.columns 
            WHERE table_name = 'notifications' AND column_name = 'appointment_id1'
        ) THEN
            ALTER TABLE notifications DROP COLUMN appointment_id1;
            RAISE NOTICE 'Dropped appointment_id1 from notifications';
        ELSE
            RAISE NOTICE 'appointment_id1 does not exist in notifications (already clean)';
        END IF;
    END $$;
    """, "Remove appointment_id1 from notifications");

// 4. Create risk_interventions table if it doesn't exist
await ExecuteSqlAsync(conn, """
    CREATE TABLE IF NOT EXISTS risk_interventions (
        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
        appointment_id UUID NOT NULL REFERENCES appointments(id) ON DELETE CASCADE,
        no_show_risk_id UUID NOT NULL REFERENCES no_show_risks(id) ON DELETE CASCADE,
        staff_id UUID NULL REFERENCES users(id) ON DELETE SET NULL,
        type VARCHAR(30) NOT NULL CHECK (type IN ('AdditionalReminder', 'CallbackRequest')),
        status VARCHAR(20) NOT NULL DEFAULT 'Pending' CHECK (status IN ('Pending', 'Accepted', 'Dismissed', 'AutoCleared')),
        dismissal_reason VARCHAR(500) NULL,
        acknowledged_at TIMESTAMPTZ NULL,
        created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
    );
    CREATE INDEX IF NOT EXISTS "IX_risk_interventions_pending" ON risk_interventions (appointment_id) WHERE status = 'Pending';
    CREATE INDEX IF NOT EXISTS "ix_risk_interventions_no_show_risk_id" ON risk_interventions (no_show_risk_id);
    CREATE INDEX IF NOT EXISTS "ix_risk_interventions_staff_id" ON risk_interventions (staff_id);
    """, "Create risk_interventions table");

// Verify the current columns in no_show_risks
Console.WriteLine("\nVerifying no_show_risks columns:");
await using var verifyCmd = conn.CreateCommand();
verifyCmd.CommandText = "SELECT column_name, data_type FROM information_schema.columns WHERE table_name='no_show_risks' ORDER BY ordinal_position;";
await using var reader = await verifyCmd.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    Console.WriteLine($"  {reader.GetString(0)} ({reader.GetString(1)})");
}
await reader.CloseAsync();

// List specialties
Console.WriteLine("\nSpecialties in DB:");
await using var specCmd = conn.CreateCommand();
specCmd.CommandText = "SELECT id, name FROM specialties LIMIT 5;";
await using var specReader = await specCmd.ExecuteReaderAsync();
while (await specReader.ReadAsync())
{
    Console.WriteLine($"  {specReader.GetGuid(0)}\t{specReader.GetString(1)}");
}

Console.WriteLine("\nAll migrations applied successfully.");

static async Task ExecuteSqlAsync(NpgsqlConnection conn, string sql, string description)
{
    Console.Write($"  Applying: {description}... ");
    try
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
        Console.WriteLine("OK");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR: {ex.Message}");
        throw;
    }
}
