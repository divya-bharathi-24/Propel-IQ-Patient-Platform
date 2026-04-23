using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <summary>
    /// Migration: AddRiskInterventions (US_032, task_003 — High-Risk Appointment Flag).
    ///
    /// Changes applied in Up():
    ///   1. Creates <c>risk_interventions</c> table with all 9 columns (id, no_show_risk_id,
    ///      appointment_id, type, status, staff_id, acknowledged_at, dismissal_reason, created_at).
    ///   2. Adds CHECK constraint <c>CK_risk_interventions_type</c>: restricts type to
    ///      ('AdditionalReminder', 'CallbackRequest') (AC-2).
    ///   3. Adds CHECK constraint <c>CK_risk_interventions_status</c>: restricts status to
    ///      ('Pending', 'Accepted', 'Dismissed', 'AutoCleared') (AC-2, AC-3, edge case).
    ///   4. Adds FK <c>risk_interventions → no_show_risks</c> (CASCADE) — interventions are
    ///      derived from the risk record and share its lifecycle.
    ///   5. Adds FK <c>risk_interventions → appointments</c> (CASCADE) — denormalised FK for
    ///      query efficiency; interventions are cleaned up when the appointment is deleted.
    ///   6. Adds FK <c>risk_interventions → users</c> (SET NULL) — deleting a staff user
    ///      retains the intervention row for audit history; staff_id is nulled.
    ///   7. Adds partial index <c>IX_risk_interventions_pending</c> on (appointment_id)
    ///      WHERE status = 'Pending' — optimises the "Requires Attention" dashboard query (AC-4).
    ///
    /// Down() rollback:
    ///   Drops the partial index first, then drops the table (dependency order).
    ///
    /// Additive migration — no existing table is modified. Zero-downtime compatible on PostgreSQL 16+.
    /// </summary>
    public partial class AddRiskInterventions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "risk_interventions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    no_show_risk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acknowledged_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    dismissal_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_interventions", x => x.id);

                    table.CheckConstraint(
                        "CK_risk_interventions_type",
                        "type IN ('AdditionalReminder', 'CallbackRequest')");

                    table.CheckConstraint(
                        "CK_risk_interventions_status",
                        "status IN ('Pending', 'Accepted', 'Dismissed', 'AutoCleared')");

                    table.ForeignKey(
                        name: "FK_risk_interventions_no_show_risks_no_show_risk_id",
                        column: x => x.no_show_risk_id,
                        principalTable: "no_show_risks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);

                    table.ForeignKey(
                        name: "FK_risk_interventions_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);

                    table.ForeignKey(
                        name: "FK_risk_interventions_users_staff_id",
                        column: x => x.staff_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            // Partial index: optimises the "Requires Attention" dashboard query (AC-4).
            // Only indexes rows where status = 'Pending'; AutoCleared/Accepted/Dismissed rows
            // are excluded, keeping the index small as the table grows.
            migrationBuilder.CreateIndex(
                name: "IX_risk_interventions_pending",
                table: "risk_interventions",
                column: "appointment_id",
                filter: "status = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop partial index before the table to avoid orphaned index references
            migrationBuilder.DropIndex(
                name: "IX_risk_interventions_pending",
                table: "risk_interventions");

            migrationBuilder.DropTable(name: "risk_interventions");
        }
    }
}
