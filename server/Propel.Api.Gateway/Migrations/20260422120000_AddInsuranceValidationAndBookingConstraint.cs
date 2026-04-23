using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations;

/// <summary>
/// Additive migration for US_019 task_003 — zero-downtime per DR-013.
/// Changes:
///   1. Creates <c>dummy_insurers</c> table seeded with 5 records for
///      <c>InsuranceSoftCheckService</c> prefix matching (FR-038, FR-040).
///   2. Adds FK <c>insurance_validations → appointments</c> (CASCADE) so orphaned
///      validation records are cleaned up when an appointment is deleted.
///   3. Adds composite index <c>ix_insurance_validations_patient_id_validated_at</c>
///      (patient_id ASC, validated_at DESC) to support the patient dashboard query (AC-2).
///   4. Adds unique partial index <c>ix_appointments_slot_uniqueness</c> on
///      <c>appointments(specialty_id, date, time_slot_start)</c> WHERE status NOT IN
///      ('Cancelled') — the primary DB-level concurrency constraint for FR-013. A second
///      concurrent INSERT to the same active slot raises a PostgreSQL unique violation,
///      surfaced as <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/> in EF Core
///      and mapped to HTTP 409 Conflict by the booking handler (task_002).
/// </summary>
public partial class AddInsuranceValidationAndBookingConstraint : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── 1. Create dummy_insurers seed table ───────────────────────────────
        migrationBuilder.CreateTable(
            name: "dummy_insurers",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                insurer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                member_id_prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_dummy_insurers", x => x.id);
            });

        // Seed 5 dummy insurer records used by InsuranceSoftCheckService (FR-038, FR-040)
        migrationBuilder.InsertData(
            table: "dummy_insurers",
            columns: new[] { "id", "insurer_name", "member_id_prefix", "is_active" },
            values: new object[,]
            {
                { new Guid("a1b2c3d4-0001-0000-0000-000000000000"), "BlueCross Shield",  "BCS", true },
                { new Guid("a1b2c3d4-0002-0000-0000-000000000000"), "Aetna Health",       "AET", true },
                { new Guid("a1b2c3d4-0003-0000-0000-000000000000"), "United HealthGroup", "UHG", true },
                { new Guid("a1b2c3d4-0004-0000-0000-000000000000"), "Cigna Medical",      "CGN", true },
                { new Guid("a1b2c3d4-0005-0000-0000-000000000000"), "Humana Plus",        "HMN", true }
            });

        // ── 2. Add FK: insurance_validations → appointments (CASCADE) ─────────
        // appointment_id is already a nullable column (created in 20260420190747).
        // This FK is additive — no column change needed, only the constraint is new.
        migrationBuilder.AddForeignKey(
            name: "fk_insurance_validations_appointments_appointment_id",
            table: "insurance_validations",
            column: "appointment_id",
            principalTable: "appointments",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        // ── 3. Composite index for patient dashboard query (AC-2) ─────────────
        // validated_at DESC so the latest validation appears first without ORDER BY.
        migrationBuilder.CreateIndex(
            name: "ix_insurance_validations_patient_id_validated_at",
            table: "insurance_validations",
            columns: new[] { "patient_id", "validated_at" },
            descending: new[] { false, true });

        // ── 4. Unique partial index for slot concurrency constraint (FR-013) ───
        // WHERE status NOT IN ('Cancelled') mirrors the soft-delete query filter so
        // cancelled appointments do not block re-booking of the same slot.
        // EF Core CreateIndex does not support arbitrary WHERE predicates with NOT IN,
        // so raw SQL is used here per established project pattern (task_002, AC-3).
        migrationBuilder.Sql(
            """
            CREATE UNIQUE INDEX ix_appointments_slot_uniqueness
                ON appointments (specialty_id, date, time_slot_start)
                WHERE status NOT IN ('Cancelled');
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Reverse in dependency order (index → FK → table)
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_appointments_slot_uniqueness;");

        migrationBuilder.DropIndex(
            name: "ix_insurance_validations_patient_id_validated_at",
            table: "insurance_validations");

        migrationBuilder.DropForeignKey(
            name: "fk_insurance_validations_appointments_appointment_id",
            table: "insurance_validations");

        migrationBuilder.DropTable(name: "dummy_insurers");
    }
}
