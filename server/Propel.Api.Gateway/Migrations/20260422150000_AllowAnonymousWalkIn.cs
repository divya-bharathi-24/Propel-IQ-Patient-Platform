using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <summary>
    /// Migration: AllowAnonymousWalkIn (US_026 / AC-3 — Staff Walk-In with Optional Patient Account).
    ///
    /// Changes applied in Up():
    ///   1. Makes <c>patient_id</c> nullable on <c>appointments</c> — supports anonymous walk-in
    ///      visits where no patient account exists. Existing Booked appointments are unaffected
    ///      (patient_id values remain unchanged; only the NOT NULL constraint is dropped).
    ///   2. Adds <c>anonymous_visit_id</c> (UUID NULL) to <c>appointments</c> — uniquely identifies
    ///      an anonymous walk-in. Populated only when patient_id IS NULL.
    ///   3. Makes <c>time_slot_start</c> and <c>time_slot_end</c> nullable on <c>appointments</c> —
    ///      queue-only walk-ins may have no assigned time slot.
    ///   4. Makes <c>patient_id</c> nullable on <c>queue_entries</c> — an anonymous QueueEntry can
    ///      be created when the linked Appointment has no patient_id (AC-3 QueueEntry).
    ///   5. Creates partial unique index <c>idx_appointments_anonymous_visit_id</c> on
    ///      <c>appointments(anonymous_visit_id) WHERE anonymous_visit_id IS NOT NULL</c> — prevents
    ///      two anonymous walk-ins from sharing the same visit ID.
    ///
    /// All ALTER COLUMN ... DROP NOT NULL operations are non-blocking on PostgreSQL 16.
    ///
    /// Down() rollback:
    ///   Reverses all changes. Only safe on a fresh schema or when NO rows have
    ///   patient_id = NULL in appointments / queue_entries.
    /// </summary>
    public partial class AllowAnonymousWalkIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Drop FK before altering appointments.patient_id nullability
            migrationBuilder.DropForeignKey(
                name: "fk_appointments_patients_patient_id",
                table: "appointments");

            // 2. Make appointments.patient_id nullable (supports anonymous walk-ins, US_026 AC-3)
            migrationBuilder.AlterColumn<Guid>(
                name: "patient_id",
                table: "appointments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // 3. Make appointments.time_slot_start nullable (queue-only walk-ins have no assigned slot)
            migrationBuilder.AlterColumn<TimeOnly>(
                name: "time_slot_start",
                table: "appointments",
                type: "time",
                nullable: true,
                oldClrType: typeof(TimeOnly),
                oldType: "time");

            // 4. Make appointments.time_slot_end nullable (queue-only walk-ins have no assigned slot)
            migrationBuilder.AlterColumn<TimeOnly>(
                name: "time_slot_end",
                table: "appointments",
                type: "time",
                nullable: true,
                oldClrType: typeof(TimeOnly),
                oldType: "time");

            // 5. Add anonymous_visit_id column to appointments (US_026, AC-3)
            migrationBuilder.AddColumn<Guid>(
                name: "anonymous_visit_id",
                table: "appointments",
                type: "uuid",
                nullable: true);

            // 6. Re-add FK as optional (nullable patient_id is valid in PostgreSQL FK)
            migrationBuilder.AddForeignKey(
                name: "fk_appointments_patients_patient_id",
                table: "appointments",
                column: "patient_id",
                principalTable: "patients",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // 7. Drop FK before altering queue_entries.patient_id nullability
            migrationBuilder.DropForeignKey(
                name: "fk_queue_entries_patients_patient_id",
                table: "queue_entries");

            // 8. Make queue_entries.patient_id nullable (anonymous queue entries, US_026 AC-3 QueueEntry)
            migrationBuilder.AlterColumn<Guid>(
                name: "patient_id",
                table: "queue_entries",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // 9. Re-add FK as optional
            migrationBuilder.AddForeignKey(
                name: "fk_queue_entries_patients_patient_id",
                table: "queue_entries",
                column: "patient_id",
                principalTable: "patients",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // 10. Partial unique index — ensures two anonymous walk-ins never share the same visit ID
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX idx_appointments_anonymous_visit_id
                    ON appointments (anonymous_visit_id)
                    WHERE anonymous_visit_id IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop partial unique index first
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_appointments_anonymous_visit_id;");

            // Drop FK before altering queue_entries.patient_id back to non-nullable
            migrationBuilder.DropForeignKey(
                name: "fk_queue_entries_patients_patient_id",
                table: "queue_entries");

            // Restore queue_entries.patient_id to NOT NULL (safe only if no NULL patient_id rows exist)
            migrationBuilder.AlterColumn<Guid>(
                name: "patient_id",
                table: "queue_entries",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_queue_entries_patients_patient_id",
                table: "queue_entries",
                column: "patient_id",
                principalTable: "patients",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Drop anonymous_visit_id column from appointments
            migrationBuilder.DropColumn(
                name: "anonymous_visit_id",
                table: "appointments");

            // Drop FK before altering appointments columns back to non-nullable
            migrationBuilder.DropForeignKey(
                name: "fk_appointments_patients_patient_id",
                table: "appointments");

            // Restore appointments.time_slot_end to NOT NULL
            migrationBuilder.AlterColumn<TimeOnly>(
                name: "time_slot_end",
                table: "appointments",
                type: "time",
                nullable: false,
                oldClrType: typeof(TimeOnly),
                oldType: "time",
                oldNullable: true);

            // Restore appointments.time_slot_start to NOT NULL
            migrationBuilder.AlterColumn<TimeOnly>(
                name: "time_slot_start",
                table: "appointments",
                type: "time",
                nullable: false,
                oldClrType: typeof(TimeOnly),
                oldType: "time",
                oldNullable: true);

            // Restore appointments.patient_id to NOT NULL (safe only if no NULL patient_id rows exist)
            migrationBuilder.AlterColumn<Guid>(
                name: "patient_id",
                table: "appointments",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_appointments_patients_patient_id",
                table: "appointments",
                column: "patient_id",
                principalTable: "patients",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
