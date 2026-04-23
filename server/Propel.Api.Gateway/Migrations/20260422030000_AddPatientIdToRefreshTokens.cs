using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations;

/// <summary>
/// Migration to add PatientId support to RefreshToken table.
/// This enables refresh tokens to be used for both Patient and User (Staff/Admin) authentication.
/// Key changes:
///   - Make user_id nullable
///   - Add patient_id nullable column with FK to patients table
///   - Add CHECK constraint ensuring exactly one of patient_id or user_id is non-null
///   - Add composite index on (patient_id, family_id) for patient token family revocation
///   - Update existing composite index on (user_id, family_id) to use partial index filtering
/// </summary>
public partial class AddPatientIdToRefreshTokens : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Drop existing composite index before altering columns
        migrationBuilder.DropIndex(
            name: "ix_refresh_tokens_user_id_family_id",
            table: "refresh_tokens");

        // Drop existing FK constraint before altering columns
        migrationBuilder.DropForeignKey(
            name: "fk_refresh_tokens_users_user_id",
            table: "refresh_tokens");

        // Make user_id nullable
        migrationBuilder.AlterColumn<Guid>(
            name: "user_id",
            table: "refresh_tokens",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        // Add patient_id column
        migrationBuilder.AddColumn<Guid>(
            name: "patient_id",
            table: "refresh_tokens",
            type: "uuid",
            nullable: true);

        // Re-create FK constraint to users table
        migrationBuilder.AddForeignKey(
            name: "fk_refresh_tokens_users_user_id",
            table: "refresh_tokens",
            column: "user_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        // Create partial composite index on (user_id, family_id) for staff/admin tokens
        migrationBuilder.CreateIndex(
            name: "ix_refresh_tokens_user_id_family_id",
            table: "refresh_tokens",
            columns: new[] { "user_id", "family_id" },
            filter: "user_id IS NOT NULL");

        // Create partial composite index on (patient_id, family_id) for patient tokens
        migrationBuilder.CreateIndex(
            name: "ix_refresh_tokens_patient_id_family_id",
            table: "refresh_tokens",
            columns: new[] { "patient_id", "family_id" },
            filter: "patient_id IS NOT NULL");

        // Create index on patient_id for FK performance
        migrationBuilder.CreateIndex(
            name: "ix_refresh_tokens_patient_id",
            table: "refresh_tokens",
            column: "patient_id");

        // Create FK constraint to patients table
        migrationBuilder.AddForeignKey(
            name: "fk_refresh_tokens_patients_patient_id",
            table: "refresh_tokens",
            column: "patient_id",
            principalTable: "patients",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        // Add CHECK constraint: exactly one of patient_id or user_id must be non-null
        migrationBuilder.Sql(@"
            ALTER TABLE refresh_tokens
            ADD CONSTRAINT ck_refresh_tokens_patient_or_user
            CHECK (
                (patient_id IS NOT NULL AND user_id IS NULL) OR
                (patient_id IS NULL AND user_id IS NOT NULL)
            );
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Drop CHECK constraint
        migrationBuilder.Sql(@"
            ALTER TABLE refresh_tokens
            DROP CONSTRAINT IF EXISTS ck_refresh_tokens_patient_or_user;
        ");

        // Drop FK and indexes
        migrationBuilder.DropForeignKey(
            name: "fk_refresh_tokens_patients_patient_id",
            table: "refresh_tokens");

        migrationBuilder.DropIndex(
            name: "ix_refresh_tokens_patient_id",
            table: "refresh_tokens");

        migrationBuilder.DropIndex(
            name: "ix_refresh_tokens_patient_id_family_id",
            table: "refresh_tokens");

        migrationBuilder.DropIndex(
            name: "ix_refresh_tokens_user_id_family_id",
            table: "refresh_tokens");

        migrationBuilder.DropForeignKey(
            name: "fk_refresh_tokens_users_user_id",
            table: "refresh_tokens");

        // Remove patient_id column
        migrationBuilder.DropColumn(
            name: "patient_id",
            table: "refresh_tokens");

        // Make user_id required again
        migrationBuilder.AlterColumn<Guid>(
            name: "user_id",
            table: "refresh_tokens",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        // Re-create original FK constraint
        migrationBuilder.AddForeignKey(
            name: "fk_refresh_tokens_users_user_id",
            table: "refresh_tokens",
            column: "user_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        // Recreate original composite index
        migrationBuilder.CreateIndex(
            name: "ix_refresh_tokens_user_id_family_id",
            table: "refresh_tokens",
            columns: new[] { "user_id", "family_id" });
    }
}
