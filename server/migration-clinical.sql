START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE EXTENSION IF NOT EXISTS vector;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE TABLE clinical_documents (
        id uuid NOT NULL,
        patient_id uuid NOT NULL,
        file_name character varying(500) NOT NULL,
        file_size bigint NOT NULL,
        storage_path character varying(1000) NOT NULL,
        mime_type character varying(100) NOT NULL,
        processing_status character varying(30) NOT NULL,
        uploaded_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_clinical_documents PRIMARY KEY (id),
        CONSTRAINT fk_clinical_documents_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE TABLE intake_records (
        id uuid NOT NULL,
        patient_id uuid NOT NULL,
        appointment_id uuid NOT NULL,
        source character varying(20) NOT NULL,
        demographics jsonb NOT NULL,
        medical_history jsonb NOT NULL,
        symptoms jsonb NOT NULL,
        medications jsonb NOT NULL,
        completed_at timestamp with time zone,
        CONSTRAINT pk_intake_records PRIMARY KEY (id),
        CONSTRAINT fk_intake_records_appointments_appointment_id FOREIGN KEY (appointment_id) REFERENCES appointments (id) ON DELETE RESTRICT,
        CONSTRAINT fk_intake_records_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE TABLE no_show_risks (
        id uuid NOT NULL,
        appointment_id uuid NOT NULL,
        score numeric(4,3) NOT NULL,
        factors jsonb NOT NULL,
        calculated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_no_show_risks PRIMARY KEY (id),
        CONSTRAINT ck_no_show_risk_score CHECK (score >= 0 AND score <= 1),
        CONSTRAINT fk_no_show_risks_appointments_appointment_id FOREIGN KEY (appointment_id) REFERENCES appointments (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE TABLE queue_entries (
        id uuid NOT NULL,
        patient_id uuid NOT NULL,
        appointment_id uuid NOT NULL,
        position integer NOT NULL,
        arrival_time timestamp with time zone NOT NULL,
        status character varying(20) NOT NULL,
        CONSTRAINT pk_queue_entries PRIMARY KEY (id),
        CONSTRAINT fk_queue_entries_appointments_appointment_id FOREIGN KEY (appointment_id) REFERENCES appointments (id) ON DELETE RESTRICT,
        CONSTRAINT fk_queue_entries_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE TABLE data_conflicts (
        id uuid NOT NULL,
        patient_id uuid NOT NULL,
        field_name character varying(200) NOT NULL,
        value1 character varying(2000) NOT NULL,
        source_document_id1 uuid NOT NULL,
        value2 character varying(2000) NOT NULL,
        source_document_id2 uuid NOT NULL,
        resolution_status character varying(20) NOT NULL,
        resolved_value character varying(2000),
        resolved_by uuid,
        resolved_at timestamp with time zone,
        CONSTRAINT pk_data_conflicts PRIMARY KEY (id),
        CONSTRAINT fk_data_conflicts_clinical_documents_source_document_id1 FOREIGN KEY (source_document_id1) REFERENCES clinical_documents (id) ON DELETE RESTRICT,
        CONSTRAINT fk_data_conflicts_clinical_documents_source_document_id2 FOREIGN KEY (source_document_id2) REFERENCES clinical_documents (id) ON DELETE RESTRICT,
        CONSTRAINT fk_data_conflicts_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE TABLE extracted_data (
        id uuid NOT NULL,
        document_id uuid NOT NULL,
        patient_id uuid NOT NULL,
        data_type character varying(20) NOT NULL,
        field_name character varying(200) NOT NULL,
        value character varying(2000) NOT NULL,
        confidence numeric(4,3) NOT NULL,
        source_page_number integer NOT NULL,
        source_text_snippet character varying(1000),
        embedding vector(1536),
        CONSTRAINT pk_extracted_data PRIMARY KEY (id),
        CONSTRAINT ck_extracted_data_confidence CHECK (confidence >= 0 AND confidence <= 1),
        CONSTRAINT fk_extracted_data_clinical_documents_document_id FOREIGN KEY (document_id) REFERENCES clinical_documents (id) ON DELETE RESTRICT,
        CONSTRAINT fk_extracted_data_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE TABLE medical_codes (
        id uuid NOT NULL,
        patient_id uuid NOT NULL,
        code_type character varying(10) NOT NULL,
        code character varying(20) NOT NULL,
        description character varying(500) NOT NULL,
        confidence numeric(4,3) NOT NULL,
        source_document_id uuid NOT NULL,
        verification_status character varying(20) NOT NULL,
        verified_by uuid,
        verified_at timestamp with time zone,
        CONSTRAINT pk_medical_codes PRIMARY KEY (id),
        CONSTRAINT ck_medical_codes_confidence CHECK (confidence >= 0 AND confidence <= 1),
        CONSTRAINT fk_medical_codes_clinical_documents_source_document_id FOREIGN KEY (source_document_id) REFERENCES clinical_documents (id) ON DELETE RESTRICT,
        CONSTRAINT fk_medical_codes_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE INDEX ix_clinical_documents_patient_id ON clinical_documents (patient_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE INDEX ix_data_conflicts_patient_id ON data_conflicts (patient_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE INDEX ix_data_conflicts_source_document_id1 ON data_conflicts (source_document_id1);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE INDEX ix_data_conflicts_source_document_id2 ON data_conflicts (source_document_id2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE INDEX ix_extracted_data_document_type ON extracted_data (document_id, data_type);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE INDEX ix_extracted_data_embedding_hnsw ON extracted_data USING hnsw (embedding vector_cosine_ops);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE INDEX ix_extracted_data_patient_id ON extracted_data (patient_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE INDEX ix_intake_records_appointment_id ON intake_records (appointment_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE INDEX ix_intake_records_patient_id ON intake_records (patient_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE INDEX ix_medical_codes_patient_pending ON medical_codes (patient_id, verification_status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE INDEX ix_medical_codes_source_document_id ON medical_codes (source_document_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE UNIQUE INDEX ix_no_show_risks_appointment_id ON no_show_risks (appointment_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE UNIQUE INDEX ix_queue_entries_appointment_id ON queue_entries (appointment_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE INDEX ix_queue_entries_patient_id ON queue_entries (patient_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    CREATE INDEX ix_queue_entries_status_position ON queue_entries (status, position);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260420171127_AddClinicalEntities') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260420171127_AddClinicalEntities', '9.0.15');
    END IF;
END $EF$;
COMMIT;

