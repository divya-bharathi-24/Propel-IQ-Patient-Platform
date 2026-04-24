using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicalCodesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_medical_codes_clinical_documents_source_document_id",
                table: "medical_codes");

            migrationBuilder.DropForeignKey(
                name: "fk_medical_codes_patients_patient_id",
                table: "medical_codes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_medical_codes_confidence",
                table: "medical_codes");

            migrationBuilder.RenameIndex(
                name: "ix_medical_codes_patient_pending",
                table: "medical_codes",
                newName: "ix_medical_codes_patient_status");

            migrationBuilder.AlterColumn<Guid>(
                name: "source_document_id",
                table: "medical_codes",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "medical_codes",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<decimal>(
                name: "confidence",
                table: "medical_codes",
                type: "numeric(4,3)",
                precision: 4,
                scale: 3,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,3)",
                oldPrecision: 4,
                oldScale: 3);

            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "medical_codes",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                table: "medical_codes",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<bool>(
                name: "is_manual_entry",
                table: "medical_codes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                table: "medical_codes",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_medical_codes_patient_codetype_status",
                table: "medical_codes",
                columns: new[] { "patient_id", "code_type", "verification_status" });

            migrationBuilder.CreateIndex(
                name: "ix_medical_codes_verified_by",
                table: "medical_codes",
                column: "verified_by");

            migrationBuilder.AddCheckConstraint(
                name: "ck_medical_codes_confidence",
                table: "medical_codes",
                sql: "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");

            migrationBuilder.AddForeignKey(
                name: "fk_medical_codes_clinical_documents_source_document_id",
                table: "medical_codes",
                column: "source_document_id",
                principalTable: "clinical_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_medical_codes_patients_patient_id",
                table: "medical_codes",
                column: "patient_id",
                principalTable: "patients",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_medical_codes_users_verified_by",
                table: "medical_codes",
                column: "verified_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_medical_codes_clinical_documents_source_document_id",
                table: "medical_codes");

            migrationBuilder.DropForeignKey(
                name: "fk_medical_codes_patients_patient_id",
                table: "medical_codes");

            migrationBuilder.DropForeignKey(
                name: "fk_medical_codes_users_verified_by",
                table: "medical_codes");

            migrationBuilder.DropIndex(
                name: "ix_medical_codes_patient_codetype_status",
                table: "medical_codes");

            migrationBuilder.DropIndex(
                name: "ix_medical_codes_verified_by",
                table: "medical_codes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_medical_codes_confidence",
                table: "medical_codes");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "medical_codes");

            migrationBuilder.DropColumn(
                name: "is_manual_entry",
                table: "medical_codes");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                table: "medical_codes");

            migrationBuilder.RenameIndex(
                name: "ix_medical_codes_patient_status",
                table: "medical_codes",
                newName: "ix_medical_codes_patient_pending");

            migrationBuilder.AlterColumn<Guid>(
                name: "source_document_id",
                table: "medical_codes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "medical_codes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.AlterColumn<decimal>(
                name: "confidence",
                table: "medical_codes",
                type: "numeric(4,3)",
                precision: 4,
                scale: 3,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,3)",
                oldPrecision: 4,
                oldScale: 3,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "medical_codes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AddCheckConstraint(
                name: "ck_medical_codes_confidence",
                table: "medical_codes",
                sql: "confidence >= 0 AND confidence <= 1");

            migrationBuilder.AddForeignKey(
                name: "fk_medical_codes_clinical_documents_source_document_id",
                table: "medical_codes",
                column: "source_document_id",
                principalTable: "clinical_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_medical_codes_patients_patient_id",
                table: "medical_codes",
                column: "patient_id",
                principalTable: "patients",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
