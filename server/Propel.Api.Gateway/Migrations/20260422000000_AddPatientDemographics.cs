using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <summary>
    /// Migration: AddPatientDemographics (task_002/task_003, US_015).
    ///
    /// Changes applied in Up():
    ///   1. Adds <c>biological_sex</c> (VARCHAR 30 NULL) — locked field set at registration (AC-3).
    ///   2. Adds <c>address_encrypted</c> (TEXT NULL) — AES-256 encrypted JSON; address PHI (NFR-004).
    ///   3. Adds <c>emergency_contact_encrypted</c> (TEXT NULL) — AES-256 encrypted JSON; contact PHI (NFR-004).
    ///   4. Adds <c>communication_preferences_json</c> (JSONB NULL) — plain JSON; non-PHI opt-in flags.
    ///   5. Adds <c>insurer_name</c> (VARCHAR 200 NULL) — insurance carrier name; non-PHI.
    ///   6. Adds <c>member_id</c> (VARCHAR 200 NULL) — insurance member id; non-PHI.
    ///   7. Adds <c>group_number</c> (VARCHAR 200 NULL) — insurance group number; non-PHI.
    ///
    /// NOTE: The <c>xmin</c> system column (optimistic concurrency token) is NOT added via DDL.
    /// PostgreSQL automatically maintains <c>xmin</c> on every table row. EF Core's
    /// <c>IsRowVersion()</c> configuration instructs Npgsql to read <c>xmin</c> as a
    /// concurrency token at runtime — no schema change is needed.
    ///
    /// Down() rollback: drops all seven columns in reverse order.
    ///
    /// Zero-downtime compatible on PostgreSQL 16+: all changes are additive nullable columns.
    /// </summary>
    public partial class AddPatientDemographics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. BiologicalSex — locked field (AC-3)
            migrationBuilder.AddColumn<string>(
                name: "biological_sex",
                table: "patients",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            // 2. Address — AES-256 encrypted JSON (PHI, NFR-004)
            migrationBuilder.AddColumn<string>(
                name: "address_encrypted",
                table: "patients",
                type: "text",
                nullable: true);

            // 3. EmergencyContact — AES-256 encrypted JSON (PHI, NFR-004)
            migrationBuilder.AddColumn<string>(
                name: "emergency_contact_encrypted",
                table: "patients",
                type: "text",
                nullable: true);

            // 4. CommunicationPreferences — plain JSONB (non-PHI)
            migrationBuilder.AddColumn<string>(
                name: "communication_preferences_json",
                table: "patients",
                type: "jsonb",
                nullable: true);

            // 5. InsurerName
            migrationBuilder.AddColumn<string>(
                name: "insurer_name",
                table: "patients",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // 6. MemberId
            migrationBuilder.AddColumn<string>(
                name: "member_id",
                table: "patients",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // 7. GroupNumber
            migrationBuilder.AddColumn<string>(
                name: "group_number",
                table: "patients",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "group_number",    table: "patients");
            migrationBuilder.DropColumn(name: "member_id",      table: "patients");
            migrationBuilder.DropColumn(name: "insurer_name",   table: "patients");
            migrationBuilder.DropColumn(name: "communication_preferences_json", table: "patients");
            migrationBuilder.DropColumn(name: "emergency_contact_encrypted",    table: "patients");
            migrationBuilder.DropColumn(name: "address_encrypted",              table: "patients");
            migrationBuilder.DropColumn(name: "biological_sex", table: "patients");
        }
    }
}
