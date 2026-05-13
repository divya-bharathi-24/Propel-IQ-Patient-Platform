#!/usr/bin/env dotnet-script
// Seed sample ExtractedData rows for a patient so the 360-view page shows clinical sections.
// Run: dotnet script seed-360-view.csx
// Or:  dotnet-script seed-360-view.csx
//
// If dotnet-script is not installed: dotnet tool install -g dotnet-script

#r "nuget: Npgsql, 8.0.3"

using Npgsql;

var patientId  = "e3208ca6-a4d9-4f09-b9e1-4277ded85be6";
var documentId = "ea85f535-ab3b-4855-a161-4f9be5d2c0d9";
var connStr    = "Host=ep-divine-mode-amg8nhin.c-5.us-east-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_QAz7gjyI8WHk;SSL Mode=Require;Trust Server Certificate=true";

await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();
Console.WriteLine("Connected to Neon PostgreSQL.");

// 1. Mark document Completed
await using (var cmd = new NpgsqlCommand("UPDATE clinical_documents SET processing_status='Completed' WHERE id=@id", conn))
{
    cmd.Parameters.AddWithValue("id", Guid.Parse(documentId));
    int rows = await cmd.ExecuteNonQueryAsync();
    Console.WriteLine(rows > 0 ? $"Document {documentId} -> Completed." : $"Document {documentId} not found (skipped).");
}

// 2. Clear existing seed rows
await using (var cmd = new NpgsqlCommand("DELETE FROM extracted_data WHERE patient_id=@pid AND document_id=@did", conn))
{
    cmd.Parameters.AddWithValue("pid", Guid.Parse(patientId));
    cmd.Parameters.AddWithValue("did", Guid.Parse(documentId));
    int deleted = await cmd.ExecuteNonQueryAsync();
    Console.WriteLine($"Cleared {deleted} previous seed rows.");
}

// 3. Sample data rows: (dataType, fieldName, value, confidence)
var samples = new (string Type, string Field, string Value, decimal Conf)[]
{
    // Vitals
    ("Vital", "Blood Pressure",     "120/80 mmHg",                        0.95m),
    ("Vital", "Heart Rate",         "72 bpm",                             0.97m),
    ("Vital", "Body Temperature",   "98.6 F",                             0.93m),
    ("Vital", "Oxygen Saturation",  "98%",                                0.96m),
    ("Vital", "Weight",             "75 kg",                              0.91m),
    ("Vital", "Height",             "175 cm",                             0.90m),
    // Medications
    ("Medication", "Metformin",     "500 mg twice daily",                 0.92m),
    ("Medication", "Lisinopril",    "10 mg once daily",                   0.88m),
    ("Medication", "Atorvastatin",  "20 mg at bedtime",                   0.85m),
    // Diagnoses
    ("Diagnosis", "Primary Diagnosis",   "Type 2 Diabetes Mellitus E11.9", 0.94m),
    ("Diagnosis", "Secondary Diagnosis", "Essential Hypertension I10",     0.89m),
    ("Diagnosis", "Tertiary Diagnosis",  "Hyperlipidemia E78.5",           0.76m),
    // Allergies
    ("Allergy", "Drug Allergy",   "Penicillin - rash and hives",           0.98m),
    ("Allergy", "Food Allergy",   "Shellfish - anaphylaxis",               0.95m),
    ("Allergy", "Environmental",  "Pollen - seasonal rhinitis",            0.72m),
    // Immunizations (History)
    ("History", "COVID-19 Vaccine",  "Completed 2 doses Pfizer 2022",      0.97m),
    ("History", "Influenza Vaccine", "Annual last October 2025",           0.93m),
    ("History", "Tetanus Td",        "Booster 2020",                       0.88m),
    // Surgical History (History)
    ("History", "Appendectomy",      "2015 laparoscopic no complications", 0.91m),
    ("History", "Knee Arthroscopy",  "Right knee 2019",                    0.84m),
};

const string insertSql = @"
INSERT INTO extracted_data
    (id, document_id, patient_id, data_type, field_name, value, confidence,
     source_page_number, priority_review, is_canonical, deduplication_status)
VALUES
    (@id, @did, @pid, @dtype, @fname, @val, @conf, 1, @pr, true, 'Unprocessed')
ON CONFLICT DO NOTHING";

int inserted = 0;
foreach (var (type, field, value, conf) in samples)
{
    await using var cmd = new NpgsqlCommand(insertSql, conn);
    cmd.Parameters.AddWithValue("id",    Guid.NewGuid());
    cmd.Parameters.AddWithValue("did",   Guid.Parse(documentId));
    cmd.Parameters.AddWithValue("pid",   Guid.Parse(patientId));
    cmd.Parameters.AddWithValue("dtype", type);
    cmd.Parameters.AddWithValue("fname", field);
    cmd.Parameters.AddWithValue("val",   value);
    cmd.Parameters.AddWithValue("conf",  conf);
    cmd.Parameters.AddWithValue("pr",    conf < 0.80m);
    await cmd.ExecuteNonQueryAsync();
    inserted++;
}
Console.WriteLine($"Inserted {inserted} sample extracted data rows.");

// 4. Verify
await using (var cmd = new NpgsqlCommand(
    "SELECT data_type, COUNT(*) FROM extracted_data WHERE patient_id=@pid GROUP BY data_type ORDER BY data_type", conn))
{
    cmd.Parameters.AddWithValue("pid", Guid.Parse(patientId));
    await using var reader = await cmd.ExecuteReaderAsync();
    Console.WriteLine($"\nExtractedData summary for patient {patientId}:");
    while (await reader.ReadAsync())
        Console.WriteLine($"  {reader.GetString(0)}: {reader.GetInt64(1)} rows");
}

Console.WriteLine("\nDone! Refresh the 360-view page -- all clinical sections should now appear.");
