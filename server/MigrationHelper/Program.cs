using Npgsql;

var connectionString = "Server=127.0.0.1;Port=5433;User Id=postgres;Password=admin;Database=propeliq_dev1;";

var sql = @"
ALTER TABLE patients 
ALTER COLUMN date_of_birth TYPE text 
USING date_of_birth::text;
";

try
{
    using var connection = new NpgsqlConnection(connectionString);
    connection.Open();

    using var command = connection.CreateCommand();
    command.CommandText = sql;

    var result = command.ExecuteNonQuery();
    Console.WriteLine($"Successfully altered date_of_birth column type from date to text.");
    Console.WriteLine($"Migration applied successfully!");
}
catch (Exception ex)
{
    Console.WriteLine($"Error executing SQL: {ex.Message}");
    Environment.Exit(1);
}
