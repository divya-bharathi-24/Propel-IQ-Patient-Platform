#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Seeds sample ExtractedData rows for a patient so the 360-view page displays
  clinical sections (Vitals, Medications, Diagnoses, Allergies, Immunizations, SurgicalHistory).
  Also marks the patient's clinical document as Completed so the backend returns 200 (not 202).

.USAGE
  .\seed-360-view-data.ps1 -PatientId "e3208ca6-a4d9-4f09-b9e1-4277ded85be6" `
                            -DocumentId "ea85f535-ab3b-4855-a161-4f9be5d2c0d9"
#>
param(
    [string]$PatientId  = "e3208ca6-a4d9-4f09-b9e1-4277ded85be6",
    [string]$DocumentId = "ea85f535-ab3b-4855-a161-4f9be5d2c0d9"
)

$ConnStr = "Host=ep-divine-mode-amg8nhin.c-5.us-east-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_QAz7gjyI8WHk;SSL Mode=Require;Trust Server Certificate=true"

# ── Install Npgsql if not already available ────────────────────────────────────
$npgsqlPath = Join-Path $PSScriptRoot "npgsql-temp\Npgsql.dll"
if (-not (Test-Path $npgsqlPath)) {
    Write-Host "Downloading Npgsql..." -ForegroundColor Cyan
    $tempDir = Join-Path $PSScriptRoot "npgsql-temp"
    New-Item -ItemType Directory -Force -Path $tempDir | Out-Null
    & dotnet add "$PSScriptRoot/Propel.Api.Gateway/Propel.Api.Gateway.csproj" package Npgsql 2>&1 | Out-Null
    # Use the one already in the project's NuGet cache
    $npgsqlPath = (Get-ChildItem "$env:USERPROFILE\.nuget\packages\npgsql" -Recurse -Filter "Npgsql.dll" |
        Where-Object { $_.FullName -match "net[89]|net10" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1).FullName
    if (-not $npgsqlPath) {
        $npgsqlPath = (Get-ChildItem "$env:USERPROFILE\.nuget\packages\npgsql" -Recurse -Filter "Npgsql.dll" |
            Sort-Object FullName -Descending |
            Select-Object -First 1).FullName
    }
}

if (-not $npgsqlPath -or -not (Test-Path $npgsqlPath)) {
    Write-Error "Could not locate Npgsql.dll in the NuGet cache. Run 'dotnet restore' first."
    exit 1
}

Add-Type -Path $npgsqlPath

function Exec-Sql([string]$sql, [Npgsql.NpgsqlConnection]$conn) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    $cmd.ExecuteNonQuery() | Out-Null
}

function Row-Exists([string]$sql, [Npgsql.NpgsqlConnection]$conn) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    $val = $cmd.ExecuteScalar()
    return ($val -ne $null -and $val -ne [DBNull]::Value -and [int]$val -gt 0)
}

$conn = [Npgsql.NpgsqlConnection]::new($ConnStr)
$conn.Open()
Write-Host "Connected to Neon PostgreSQL." -ForegroundColor Green

# Helper: run parameterised command
function Exec-Param([string]$sql, [hashtable]$params, [Npgsql.NpgsqlConnection]$c) {
    $cmd = $c.CreateCommand()
    $cmd.CommandText = $sql
    foreach ($kv in $params.GetEnumerator()) {
        $cmd.Parameters.AddWithValue($kv.Key, $kv.Value) | Out-Null
    }
    $cmd.ExecuteNonQuery() | Out-Null
}

# ── 1. Ensure document exists and is Completed ─────────────────────────────────
$checkCmd = $conn.CreateCommand()
$checkCmd.CommandText = 'SELECT COUNT(*) FROM clinical_documents WHERE id = @docId'
$checkCmd.Parameters.AddWithValue('docId', [Guid]$DocumentId) | Out-Null
$docCount = [int]$checkCmd.ExecuteScalar()

if ($docCount -eq 0) {
    Write-Host "Document $DocumentId not found in DB — skipping document status update." -ForegroundColor Yellow
} else {
    Exec-Param 'UPDATE clinical_documents SET processing_status = @s WHERE id = @id' @{ s='Completed'; id=[Guid]$DocumentId } $conn
    Write-Host "Document $DocumentId -> Completed." -ForegroundColor Green
}

# ── 2. Delete any existing seed rows for idempotency ──────────────────────────
Exec-Param 'DELETE FROM extracted_data WHERE patient_id = @pid AND document_id = @did' @{ pid=[Guid]$PatientId; did=[Guid]$DocumentId } $conn
Write-Host "Cleared previous seed rows." -ForegroundColor Gray

# ── 3. Insert sample extracted data rows ──────────────────────────────────────
# data_type values match ExtractedDataType enum: Vital, Medication, Diagnosis, Allergy, History
$rows = @(
    # Vitals
    @{ type="Vital";      field="Blood Pressure";     value="120/80 mmHg";                           confidence=0.95 },
    @{ type="Vital";      field="Heart Rate";          value="72 bpm";                                confidence=0.97 },
    @{ type="Vital";      field="Body Temperature";    value="98.6 F";                                confidence=0.93 },
    @{ type="Vital";      field="Oxygen Saturation";   value="98%";                                   confidence=0.96 },
    @{ type="Vital";      field="Weight";              value="75 kg";                                 confidence=0.91 },
    @{ type="Vital";      field="Height";              value="175 cm";                                confidence=0.90 },
    # Medications
    @{ type="Medication"; field="Metformin";           value="500 mg twice daily";                    confidence=0.92 },
    @{ type="Medication"; field="Lisinopril";          value="10 mg once daily";                      confidence=0.88 },
    @{ type="Medication"; field="Atorvastatin";        value="20 mg at bedtime";                      confidence=0.85 },
    # Diagnoses
    @{ type="Diagnosis";  field="Primary Diagnosis";   value="Type 2 Diabetes Mellitus (E11.9)";      confidence=0.94 },
    @{ type="Diagnosis";  field="Secondary Diagnosis"; value="Essential Hypertension (I10)";           confidence=0.89 },
    @{ type="Diagnosis";  field="Tertiary Diagnosis";  value="Hyperlipidemia (E78.5)";                confidence=0.76 },
    # Allergies
    @{ type="Allergy";    field="Drug Allergy";        value="Penicillin - rash, hives";              confidence=0.98 },
    @{ type="Allergy";    field="Food Allergy";        value="Shellfish - anaphylaxis";               confidence=0.95 },
    @{ type="Allergy";    field="Environmental";       value="Pollen - seasonal rhinitis";            confidence=0.72 },
    # Immunizations (History)
    @{ type="History";    field="COVID-19 Vaccine";    value="Completed - 2 doses (Pfizer, 2022)";    confidence=0.97 },
    @{ type="History";    field="Influenza Vaccine";   value="Annual - last October 2025";            confidence=0.93 },
    @{ type="History";    field="Tetanus (Td)";        value="Booster 2020";                          confidence=0.88 },
    # Surgical History (History)
    @{ type="History";    field="Appendectomy";        value="2015 - laparoscopic, no complications"; confidence=0.91 },
    @{ type="History";    field="Knee Arthroscopy";    value="Right knee, 2019";                      confidence=0.84 }
)

$insertSql = @'
INSERT INTO extracted_data
    (id, document_id, patient_id, data_type, field_name, value, confidence,
     source_page_number, priority_review, is_canonical, deduplication_status)
VALUES
    (@id, @did, @pid, @dtype, @fname, @val, @conf, 1, @pr, true, 'Unprocessed')
ON CONFLICT DO NOTHING
'@

foreach ($r in $rows) {
    $pr = if ([decimal]$r.confidence -lt 0.80) { $true } else { $false }
    Exec-Param $insertSql @{
        id    = [Guid]::NewGuid()
        did   = [Guid]$DocumentId
        pid   = [Guid]$PatientId
        dtype = $r.type
        fname = $r.field
        val   = $r.value
        conf  = [decimal]$r.confidence
        pr    = $pr
    } $conn
}

Write-Host "Inserted $($rows.Count) sample extracted data rows." -ForegroundColor Green

# ── 4. Verify insertion ────────────────────────────────────────────────────────
$verifyCmd = $conn.CreateCommand()
$verifyCmd.CommandText = 'SELECT data_type, COUNT(*) as cnt FROM extracted_data WHERE patient_id = @pid GROUP BY data_type ORDER BY data_type'
$verifyCmd.Parameters.AddWithValue('pid', [Guid]$PatientId) | Out-Null
$reader = $verifyCmd.ExecuteReader()
Write-Host "`nExtractedData summary for patient $PatientId" -ForegroundColor Cyan
while ($reader.Read()) {
    $dt  = $reader.GetString(0)
    $cnt = $reader.GetInt64(1)
    Write-Host "  ${dt}: ${cnt} rows"
}
$reader.Close()

$conn.Close()
Write-Host "`nDone! Refresh the 360-view page -- it should now show all clinical sections." -ForegroundColor Green
