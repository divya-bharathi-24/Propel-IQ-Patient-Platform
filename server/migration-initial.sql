CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420161639_Initial') THEN
    CREATE TABLE patients (
        id uuid NOT NULL,
        name character varying(200) NOT NULL,
        email character varying(320) NOT NULL,
        phone character varying(30) NOT NULL,
        date_of_birth date NOT NULL,
        password_hash character varying(500) NOT NULL,
        email_verified boolean NOT NULL DEFAULT FALSE,
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_patients PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420161639_Initial') THEN
    CREATE TABLE specialties (
        id uuid NOT NULL,
        name character varying(100) NOT NULL,
        description character varying(500),
        CONSTRAINT pk_specialties PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420161639_Initial') THEN
    CREATE TABLE users (
        id uuid NOT NULL,
        email character varying(320) NOT NULL,
        password_hash character varying(500) NOT NULL,
        role character varying(20) NOT NULL,
        status character varying(20) NOT NULL,
        last_login_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_users PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420161639_Initial') THEN
    CREATE TABLE appointments (
        id uuid NOT NULL,
        patient_id uuid NOT NULL,
        specialty_id uuid NOT NULL,
        date date NOT NULL,
        time_slot_start time NOT NULL,
        time_slot_end time NOT NULL,
        status character varying(20) NOT NULL,
        cancellation_reason character varying(500),
        created_by uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_appointments PRIMARY KEY (id),
        CONSTRAINT fk_appointments_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id) ON DELETE RESTRICT,
        CONSTRAINT fk_appointments_specialties_specialty_id FOREIGN KEY (specialty_id) REFERENCES specialties (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420161639_Initial') THEN
    CREATE TABLE waitlist_entries (
        id uuid NOT NULL,
        patient_id uuid NOT NULL,
        current_appointment_id uuid NOT NULL,
        preferred_date date NOT NULL,
        preferred_time_slot time NOT NULL,
        enrolled_at timestamp with time zone NOT NULL,
        status character varying(20) NOT NULL,
        CONSTRAINT pk_waitlist_entries PRIMARY KEY (id),
        CONSTRAINT fk_waitlist_entries_appointments_current_appointment_id FOREIGN KEY (current_appointment_id) REFERENCES appointments (id) ON DELETE RESTRICT,
        CONSTRAINT fk_waitlist_entries_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420161639_Initial') THEN
    INSERT INTO specialties (id, description, name)
    VALUES ('00000000-0000-0000-0000-000000000001', 'Primary care and general health services', 'General Practice');
    INSERT INTO specialties (id, description, name)
    VALUES ('00000000-0000-0000-0000-000000000002', 'Heart and cardiovascular system care', 'Cardiology');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420161639_Initial') THEN
    CREATE INDEX ix_appointments_patient_id ON appointments (patient_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420161639_Initial') THEN
    CREATE INDEX ix_appointments_slot_lookup ON appointments (date, time_slot_start, specialty_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420161639_Initial') THEN
    CREATE INDEX ix_appointments_specialty_id ON appointments (specialty_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420161639_Initial') THEN
    CREATE UNIQUE INDEX ix_patients_email ON patients (email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420161639_Initial') THEN
    CREATE UNIQUE INDEX ix_users_email ON users (email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420161639_Initial') THEN
    CREATE INDEX ix_waitlist_enrolled_at ON waitlist_entries (enrolled_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420161639_Initial') THEN
    CREATE UNIQUE INDEX ix_waitlist_entries_current_appointment_id ON waitlist_entries (current_appointment_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420161639_Initial') THEN
    CREATE INDEX ix_waitlist_entries_patient_id ON waitlist_entries (patient_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420161639_Initial') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260420161639_Initial', '9.0.15');
    END IF;
END $EF$;
COMMIT;

