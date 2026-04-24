using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <summary>
    /// Migration: Add_PatientProfileVerification_And_ExtractedData_DeduplicationFields
    /// (EP-008-I/us_041, task_004).
    ///
    /// Changes applied in Up():
    ///   1. Drops obsolete <c>ix_extracted_data_patient_id</c> shadow index (superseded by the
    ///      new composite de-duplication index on (patient_id, deduplication_status)).
    ///   2. Drops obsolete <c>ix_data_conflicts_patient_id</c> simple index (superseded by
    ///      the targeted partial index covering only Critical+Unresolved rows).
    ///   3. Adds three de-duplication columns to <c>extracted_data</c>:
    ///      - <c>is_canonical</c> BOOLEAN NOT NULL DEFAULT false — canonical cluster flag (AC-1).
    ///      - <c>canonical_group_id</c> UUID NULL — links duplicates to canonical record (AIR-002).
    ///      - <c>deduplication_status</c> VARCHAR(30) NOT NULL DEFAULT 'Unprocessed' (AC-1, AC-2).
    ///   4. Adds <c>severity</c> VARCHAR(20) NOT NULL DEFAULT 'Medium' to <c>data_conflicts</c>
    ///      (AC-4 — required for conflict-gate filter on Critical conflicts).
    ///   5. Creates <c>patient_profile_verifications</c> table (AC-3):
    ///      - One row per patient (unique index on <c>patient_id</c>).
    ///      - FK → <c>patients</c> (RESTRICT — no cascade delete, DR-009).
    ///      - FK → <c>users</c> on <c>verified_by</c> (RESTRICT — staff user audit trail).
    ///   6. Creates <c>ix_extracted_data_patient_dedup_status</c> composite index (pipeline
    ///      re-query of Unprocessed/FallbackManual records per patient, task_003).
    ///   7. Creates <c>ix_extracted_data_patient_id_is_canonical</c> partial index
    ///      (filter: is_canonical = true) — fast canonical-only lookup for 360-degree
    ///      aggregation query (AC-1, task_002 GetAggregated360ViewAsync).
    ///   8. Creates <c>ix_data_conflicts_patient_critical_unresolved</c> partial index
    ///      (filter: severity = 'Critical' AND resolution_status = 'Unresolved') — conflict-gate
    ///      query optimization (AC-4, task_002 GetUnresolvedCriticalConflictsAsync).
    ///
    /// Down() rollback (DR-013):
    ///   Drops the <c>patient_profile_verifications</c> table, drops all new indexes and columns,
    ///   then recreates the original <c>ix_extracted_data_patient_id</c> and
    ///   <c>ix_data_conflicts_patient_id</c> shadow indexes.
    ///
    /// Additive migration — existing <c>extracted_data</c> rows receive default values; no data loss.
    /// Zero-downtime compatible on PostgreSQL 16+ (ADD COLUMN with DEFAULT is instantaneous for
    /// non-volatile defaults — no table rewrite required).
    /// </summary>
    public partial class Add_PatientProfileVerification_And_ExtractedData_DeduplicationFields : Migration
    {
        /// <summary>Applies all schema changes for us_041/task_004 — see class-level summary.</summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Drop obsolete shadow index — replaced by composite de-dup index below.
            migrationBuilder.DropIndex(
                name: "ix_extracted_data_patient_id",
                table: "extracted_data");

            // 2. Drop obsolete patient-id index on data_conflicts — replaced by partial index below.
            migrationBuilder.DropIndex(
                name: "ix_data_conflicts_patient_id",
                table: "data_conflicts");

            // 3. De-duplication columns on extracted_data (AC-1, AC-2, AIR-002).
            migrationBuilder.AddColumn<Guid>(
                name: "canonical_group_id",
                table: "extracted_data",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deduplication_status",
                table: "extracted_data",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Unprocessed");

            migrationBuilder.AddColumn<bool>(
                name: "is_canonical",
                table: "extracted_data",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // 4. Severity column on data_conflicts — required for conflict-gate partial index filter (AC-4).
            migrationBuilder.AddColumn<string>(
                name: "severity",
                table: "data_conflicts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Medium");

            // 5. patient_profile_verifications table — one record per patient, upserted on re-verify (AC-3).
            migrationBuilder.CreateTable(
                name: "patient_profile_verifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    verified_by = table.Column<Guid>(type: "uuid", nullable: false),
                    verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_profile_verifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_patient_profile_verifications_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_patient_profile_verifications_users_verified_by",
                        column: x => x.verified_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // 6. Composite index — pipeline re-query of Unprocessed/FallbackManual records (task_003).
            migrationBuilder.CreateIndex(
                name: "ix_extracted_data_patient_dedup_status",
                table: "extracted_data",
                columns: new[] { "patient_id", "deduplication_status" });

            // 7. Partial index — canonical-only lookup for 360-degree aggregation query (AC-1, task_002).
            migrationBuilder.CreateIndex(
                name: "ix_extracted_data_patient_id_is_canonical",
                table: "extracted_data",
                columns: new[] { "patient_id", "is_canonical" },
                filter: "is_canonical = true");

            // 8. Partial index — conflict-gate: fast lookup of Critical+Unresolved rows (AC-4, task_002).
            migrationBuilder.CreateIndex(
                name: "ix_data_conflicts_patient_critical_unresolved",
                table: "data_conflicts",
                column: "patient_id",
                filter: "severity = 'Critical' AND resolution_status = 'Unresolved'");

            migrationBuilder.CreateIndex(
                name: "ix_patient_profile_verifications_patient_id",
                table: "patient_profile_verifications",
                column: "patient_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_patient_profile_verifications_verified_by",
                table: "patient_profile_verifications",
                column: "verified_by");
        }

        /// <summary>Rolls back all us_041/task_004 schema changes. Restores original shadow indexes.</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "patient_profile_verifications");

            migrationBuilder.DropIndex(
                name: "ix_extracted_data_patient_dedup_status",
                table: "extracted_data");

            migrationBuilder.DropIndex(
                name: "ix_extracted_data_patient_id_is_canonical",
                table: "extracted_data");

            migrationBuilder.DropIndex(
                name: "ix_data_conflicts_patient_critical_unresolved",
                table: "data_conflicts");

            migrationBuilder.DropColumn(
                name: "canonical_group_id",
                table: "extracted_data");

            migrationBuilder.DropColumn(
                name: "deduplication_status",
                table: "extracted_data");

            migrationBuilder.DropColumn(
                name: "is_canonical",
                table: "extracted_data");

            migrationBuilder.DropColumn(
                name: "severity",
                table: "data_conflicts");

            migrationBuilder.CreateIndex(
                name: "ix_extracted_data_patient_id",
                table: "extracted_data",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_conflicts_patient_id",
                table: "data_conflicts",
                column: "patient_id");
        }
    }
}
