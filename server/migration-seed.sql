START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420191333_AddExtensionsSeedData') THEN
    CREATE EXTENSION IF NOT EXISTS vector;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420191333_AddExtensionsSeedData') THEN
    CREATE EXTENSION IF NOT EXISTS pgcrypto;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420191333_AddExtensionsSeedData') THEN

                    CREATE TABLE IF NOT EXISTS insurance_providers (
                        id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
                        name         TEXT        NOT NULL,
                        insurer_code TEXT        NOT NULL UNIQUE,
                        is_active    BOOLEAN     NOT NULL DEFAULT TRUE,
                        created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
                    );
                
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420191333_AddExtensionsSeedData') THEN

                    INSERT INTO specialties (id, name, description) VALUES
                        ('11111111-0001-0000-0000-000000000000', 'General Practice',  'Primary care for adults and children'),
                        ('11111111-0002-0000-0000-000000000000', 'Cardiology',         'Heart and cardiovascular system'),
                        ('11111111-0003-0000-0000-000000000000', 'Dermatology',        'Skin, hair, and nails'),
                        ('11111111-0004-0000-0000-000000000000', 'Orthopedics',        'Musculoskeletal system and bones'),
                        ('11111111-0005-0000-0000-000000000000', 'Pediatrics',         'Medical care for infants, children, and adolescents')
                    ON CONFLICT (id) DO NOTHING;
                
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420191333_AddExtensionsSeedData') THEN

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
                
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420191333_AddExtensionsSeedData') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260420191333_AddExtensionsSeedData', '9.0.15');
    END IF;
END $EF$;
COMMIT;

