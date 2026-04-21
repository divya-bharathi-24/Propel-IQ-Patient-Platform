using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
// TODO: Uncomment when pgvector is installed and AI features are ready
// using Pgvector;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicalEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TODO: Uncomment when pgvector is installed and AI features are ready
            // COMMENTED OUT - AI features disabled temporarily
            // migrationBuilder.AlterDatabase()
            //     .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "clinical_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    processing_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    uploaded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clinical_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_clinical_documents_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "intake_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    demographics = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    medical_history = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    symptoms = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    medications = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_intake_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_intake_records_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_intake_records_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "no_show_risks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    score = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: false),
                    factors = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    calculated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_no_show_risks", x => x.id);
                    table.CheckConstraint("ck_no_show_risk_score", "score >= 0 AND score <= 1");
                    table.ForeignKey(
                        name: "fk_no_show_risks_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "queue_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    arrival_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_queue_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_queue_entries_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_queue_entries_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "data_conflicts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    value1 = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    source_document_id1 = table.Column<Guid>(type: "uuid", nullable: false),
                    value2 = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    source_document_id2 = table.Column<Guid>(type: "uuid", nullable: false),
                    resolution_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resolved_value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_conflicts", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_conflicts_clinical_documents_source_document_id1",
                        column: x => x.source_document_id1,
                        principalTable: "clinical_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_data_conflicts_clinical_documents_source_document_id2",
                        column: x => x.source_document_id2,
                        principalTable: "clinical_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_data_conflicts_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "medical_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    verification_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    verified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medical_codes", x => x.id);
                    table.CheckConstraint("ck_medical_codes_confidence", "confidence >= 0 AND confidence <= 1");
                    table.ForeignKey(
                        name: "fk_medical_codes_clinical_documents_source_document_id",
                        column: x => x.source_document_id,
                        principalTable: "clinical_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_medical_codes_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "extracted_data",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    field_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: false),
                    source_page_number = table.Column<int>(type: "integer", nullable: false),
                    source_text_snippet = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                    // TODO: Uncomment when pgvector is installed and AI features are ready
                    // COMMENTED OUT - AI features disabled temporarily
                    // embedding = table.Column<Vector>(type: "vector(1536)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extracted_data", x => x.id);
                    table.CheckConstraint("ck_extracted_data_confidence", "confidence >= 0 AND confidence <= 1");
                    table.ForeignKey(
                        name: "fk_extracted_data_clinical_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "clinical_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_extracted_data_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_clinical_documents_patient_id",
                table: "clinical_documents",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_conflicts_patient_id",
                table: "data_conflicts",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_conflicts_source_document_id1",
                table: "data_conflicts",
                column: "source_document_id1");

            migrationBuilder.CreateIndex(
                name: "ix_data_conflicts_source_document_id2",
                table: "data_conflicts",
                column: "source_document_id2");

            migrationBuilder.CreateIndex(
                name: "ix_extracted_data_document_type",
                table: "extracted_data",
                columns: new[] { "document_id", "data_type" });

            // TODO: Uncomment when pgvector is installed and AI features are ready
            // COMMENTED OUT - AI features disabled temporarily
            // migrationBuilder.CreateIndex(
            //     name: "ix_extracted_data_embedding_hnsw",
            //     table: "extracted_data",
            //     column: "embedding")
            //     .Annotation("Npgsql:IndexMethod", "hnsw")
            //     .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_extracted_data_patient_id",
                table: "extracted_data",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_intake_records_appointment_id",
                table: "intake_records",
                column: "appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_intake_records_patient_id",
                table: "intake_records",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_medical_codes_patient_pending",
                table: "medical_codes",
                columns: new[] { "patient_id", "verification_status" });

            migrationBuilder.CreateIndex(
                name: "ix_medical_codes_source_document_id",
                table: "medical_codes",
                column: "source_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_no_show_risks_appointment_id",
                table: "no_show_risks",
                column: "appointment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_queue_entries_appointment_id",
                table: "queue_entries",
                column: "appointment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_queue_entries_patient_id",
                table: "queue_entries",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_queue_entries_status_position",
                table: "queue_entries",
                columns: new[] { "status", "position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_conflicts");

            migrationBuilder.DropTable(
                name: "extracted_data");

            migrationBuilder.DropTable(
                name: "medical_codes");

            migrationBuilder.DropTable(
                name: "intake_records");

            migrationBuilder.DropTable(
                name: "no_show_risks");

            migrationBuilder.DropTable(
                name: "queue_entries");

            migrationBuilder.DropTable(
                name: "clinical_documents");

            // TODO: Uncomment when pgvector is installed and AI features are ready
            // COMMENTED OUT - AI features disabled temporarily
            // migrationBuilder.AlterDatabase()
            //     .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
