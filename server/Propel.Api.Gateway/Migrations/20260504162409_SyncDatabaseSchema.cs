using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class SyncDatabaseSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Check if constraint exists before dropping
            migrationBuilder.Sql(@"
                DO $$ 
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.table_constraints 
                        WHERE constraint_name = 'fk_notifications_appointments_appointment_id1' 
                        AND table_name = 'notifications'
                    ) THEN
                        ALTER TABLE notifications DROP CONSTRAINT fk_notifications_appointments_appointment_id1;
                    END IF;
                END $$;
            ");

            // Check if index exists before dropping
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ix_notifications_appointment_id1;
            ");

            // Check if column exists before dropping
            migrationBuilder.Sql(@"
                DO $$ 
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'notifications' 
                        AND column_name = 'appointment_id1'
                    ) THEN
                        ALTER TABLE notifications DROP COLUMN appointment_id1;
                    END IF;
                END $$;
            ");

            // Same for calendar_syncs table
            migrationBuilder.Sql(@"
                DO $$ 
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.table_constraints 
                        WHERE constraint_name = 'fk_calendar_syncs_appointments_appointment_id1' 
                        AND table_name = 'calendar_syncs'
                    ) THEN
                        ALTER TABLE calendar_syncs DROP CONSTRAINT fk_calendar_syncs_appointments_appointment_id1;
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ix_calendar_syncs_appointment_id1;
            ");

            migrationBuilder.Sql(@"
                DO $$ 
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'calendar_syncs' 
                        AND column_name = 'appointment_id1'
                    ) THEN
                        ALTER TABLE calendar_syncs DROP COLUMN appointment_id1;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "appointment_id1",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_appointment_id1",
                table: "notifications",
                column: "appointment_id1");

            migrationBuilder.AddForeignKey(
                name: "fk_notifications_appointments_appointment_id1",
                table: "notifications",
                column: "appointment_id1",
                principalTable: "appointments",
                principalColumn: "id");
        }
    }
}
