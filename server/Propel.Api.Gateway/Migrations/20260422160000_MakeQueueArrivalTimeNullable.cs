using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <summary>
    /// Migration: MakeQueueArrivalTimeNullable (US_027 — Same-Day Queue View &amp; Arrived Status Marking).
    ///
    /// Changes applied in Up():
    ///   1. Makes <c>arrival_time</c> nullable on <c>queue_entries</c> — the column must accept
    ///      NULL until <c>MarkArrived</c> is called and be settable back to NULL by
    ///      <c>RevertArrived</c> (AC-2, edge case). Existing rows in <c>queue_entries</c>
    ///      retain their current <c>arrival_time</c> values; the NOT NULL constraint is dropped.
    ///
    /// Down() rollback:
    ///   Sets any NULL <c>arrival_time</c> values to <c>now()</c> (to avoid constraint violation),
    ///   then restores the NOT NULL constraint. Only safe when no production reverts have been issued.
    /// </summary>
    public partial class MakeQueueArrivalTimeNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop FK before altering queue_entries.appointment_id relationship
            migrationBuilder.DropForeignKey(
                name: "fk_queue_entries_appointments_appointment_id",
                table: "queue_entries");

            // Make arrival_time nullable (US_027, AC-2 — RevertArrived clears this field)
            migrationBuilder.AlterColumn<DateTime>(
                name: "arrival_time",
                table: "queue_entries",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            // Restore FK with same semantics — only principal/dependent navigation has changed
            migrationBuilder.AddForeignKey(
                name: "fk_queue_entries_appointments_appointment_id",
                table: "queue_entries",
                column: "appointment_id",
                principalTable: "appointments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_queue_entries_appointments_appointment_id",
                table: "queue_entries");

            // Backfill NULLs with now() so the NOT NULL constraint can be restored without error
            migrationBuilder.Sql(
                "UPDATE queue_entries SET arrival_time = now() WHERE arrival_time IS NULL;");

            migrationBuilder.AlterColumn<DateTime>(
                name: "arrival_time",
                table: "queue_entries",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldNullable: true,
                oldType: "timestamp with time zone");

            migrationBuilder.AddForeignKey(
                name: "fk_queue_entries_appointments_appointment_id",
                table: "queue_entries",
                column: "appointment_id",
                principalTable: "appointments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
