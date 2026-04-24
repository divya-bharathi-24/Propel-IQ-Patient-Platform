using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class AddDataConflictsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_data_conflicts_patients_patient_id",
                table: "data_conflicts");

            migrationBuilder.AlterColumn<string>(
                name: "severity",
                table: "data_conflicts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Warning",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Medium");

            migrationBuilder.AlterColumn<string>(
                name: "resolution_status",
                table: "data_conflicts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValueSql: "'Unresolved'",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "field_name",
                table: "data_conflicts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "detected_at",
                table: "data_conflicts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "resolution_note",
                table: "data_conflicts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_conflicts_idempotency",
                table: "data_conflicts",
                columns: new[] { "patient_id", "field_name", "source_document_id1", "source_document_id2" },
                unique: true,
                filter: "\"resolution_status\" = 'Unresolved'");

            migrationBuilder.CreateIndex(
                name: "ix_data_conflicts_patient_status_severity",
                table: "data_conflicts",
                columns: new[] { "patient_id", "resolution_status", "severity" });

            // Plain PatientId index for all-conflicts retrieval; re-created here because
            // the us_041 migration dropped it (ix_data_conflicts_patient_id) and EF Core
            // collapses two HasIndex(PatientId) registrations in the model.
            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ix_data_conflicts_patient_id ON data_conflicts (patient_id);");

            migrationBuilder.CreateIndex(
                name: "ix_data_conflicts_resolved_by",
                table: "data_conflicts",
                column: "resolved_by");

            migrationBuilder.AddForeignKey(
                name: "fk_data_conflicts_patients_patient_id",
                table: "data_conflicts",
                column: "patient_id",
                principalTable: "patients",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_data_conflicts_users_resolved_by",
                table: "data_conflicts",
                column: "resolved_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_data_conflicts_patients_patient_id",
                table: "data_conflicts");

            migrationBuilder.DropForeignKey(
                name: "fk_data_conflicts_users_resolved_by",
                table: "data_conflicts");

            migrationBuilder.DropIndex(
                name: "ix_data_conflicts_idempotency",
                table: "data_conflicts");

            migrationBuilder.DropIndex(
                name: "ix_data_conflicts_patient_status_severity",
                table: "data_conflicts");

            migrationBuilder.Sql(
                @"DROP INDEX IF EXISTS ix_data_conflicts_patient_id;");

            migrationBuilder.DropIndex(
                name: "ix_data_conflicts_resolved_by",
                table: "data_conflicts");

            migrationBuilder.DropColumn(
                name: "detected_at",
                table: "data_conflicts");

            migrationBuilder.DropColumn(
                name: "resolution_note",
                table: "data_conflicts");

            migrationBuilder.AlterColumn<string>(
                name: "severity",
                table: "data_conflicts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Medium",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Warning");

            migrationBuilder.AlterColumn<string>(
                name: "resolution_status",
                table: "data_conflicts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValueSql: "'Unresolved'");

            migrationBuilder.AlterColumn<string>(
                name: "field_name",
                table: "data_conflicts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AddForeignKey(
                name: "fk_data_conflicts_patients_patient_id",
                table: "data_conflicts",
                column: "patient_id",
                principalTable: "patients",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
