using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class AddExtensionsSeedData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TODO: Uncomment when pgvector is installed and AI features are ready
            // AC-1: Activate pgvector extension (idempotent — IF NOT EXISTS)
            // COMMENTED OUT - AI features disabled temporarily
            // migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            // AC-1 / NFR-004: Activate pgcrypto extension (idempotent — IF NOT EXISTS)
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");

            // AC-3: Create insurance_providers reference table (not tracked by EF Core)
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS insurance_providers (
                    id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
                    name         TEXT        NOT NULL,
                    insurer_code TEXT        NOT NULL UNIQUE,
                    is_active    BOOLEAN     NOT NULL DEFAULT TRUE,
                    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );
            ");

            // AC-2: Seed 5 predefined medical specialties with deterministic UUIDs
            migrationBuilder.Sql(@"
                INSERT INTO specialties (id, name, description) VALUES
                    ('11111111-0001-0000-0000-000000000000', 'General Practice',  'Primary care for adults and children'),
                    ('11111111-0002-0000-0000-000000000000', 'Cardiology',         'Heart and cardiovascular system'),
                    ('11111111-0003-0000-0000-000000000000', 'Dermatology',        'Skin, hair, and nails'),
                    ('11111111-0004-0000-0000-000000000000', 'Orthopedics',        'Musculoskeletal system and bones'),
                    ('11111111-0005-0000-0000-000000000000', 'Pediatrics',         'Medical care for infants, children, and adolescents')
                ON CONFLICT (id) DO NOTHING;
            ");

            // AC-3: Seed 10 dummy insurance provider records with deterministic UUIDs
            migrationBuilder.Sql(@"
                INSERT INTO insurance_providers (id, name, insurer_code) VALUES
                    ('22222222-0001-0000-0000-000000000000', 'BlueCross BlueShield',   'BCBS'),
                    ('22222222-0002-0000-0000-000000000000', 'Aetna Health',           'AETNA'),
                    ('22222222-0003-0000-0000-000000000000', 'UnitedHealthcare',        'UHC'),
                    ('22222222-0004-0000-0000-000000000000', 'Cigna',                  'CIGNA'),
                    ('22222222-0005-0000-0000-000000000000', 'Humana',                 'HUMANA'),
                    ('22222222-0006-0000-0000-000000000000', 'Kaiser Permanente',      'KAISER'),
                    ('22222222-0007-0000-0000-000000000000', 'Anthem',                 'ANTHEM'),
                    ('22222222-0008-0000-0000-000000000000', 'Centene',                'CENTENE'),
                    ('22222222-0009-0000-0000-000000000000', 'Molina Healthcare',      'MOLINA'),
                    ('22222222-0010-0000-0000-000000000000', 'WellCare Health Plans',  'WELLCARE')
                ON CONFLICT (id) DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop reference table created in this migration only.
            // Extensions (vector, pgcrypto) are intentionally NOT dropped — other
            // schema objects depend on the vector column type and pgcrypto functions.
            migrationBuilder.Sql("DROP TABLE IF EXISTS insurance_providers;");
        }
    }
}
