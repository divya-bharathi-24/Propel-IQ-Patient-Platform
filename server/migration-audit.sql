START TRANSACTION;
CREATE TABLE audit_logs (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    patient_id uuid,
    action character varying(100) NOT NULL,
    entity_type character varying(100) NOT NULL,
    entity_id uuid NOT NULL,
    details jsonb,
    ip_address character varying(45),
    correlation_id character varying(64),
    timestamp timestamp with time zone NOT NULL,
    CONSTRAINT pk_audit_logs PRIMARY KEY (id)
);


                CREATE OR REPLACE FUNCTION audit_logs_immutable()
                RETURNS TRIGGER LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'audit_logs is INSERT-only; UPDATE and DELETE are not permitted'
                    USING ERRCODE = '55000';
                END;
                $$;
            


                CREATE TRIGGER trg_audit_logs_immutable
                BEFORE UPDATE OR DELETE ON audit_logs
                FOR EACH ROW EXECUTE FUNCTION audit_logs_immutable();
            

CREATE TABLE calendar_syncs (
    id uuid NOT NULL,
    patient_id uuid NOT NULL,
    appointment_id uuid NOT NULL,
    provider character varying(20) NOT NULL,
    external_event_id character varying(255) NOT NULL,
    sync_status character varying(20) NOT NULL,
    synced_at timestamp with time zone,
    error_message character varying(500),
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_calendar_syncs PRIMARY KEY (id),
    CONSTRAINT fk_calendar_syncs_appointments_appointment_id FOREIGN KEY (appointment_id) REFERENCES appointments (id) ON DELETE RESTRICT,
    CONSTRAINT fk_calendar_syncs_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id) ON DELETE RESTRICT
);

CREATE TABLE insurance_validations (
    id uuid NOT NULL,
    patient_id uuid NOT NULL,
    appointment_id uuid,
    provider_name character varying(200) NOT NULL,
    insurance_id character varying(100) NOT NULL,
    validation_result character varying(20) NOT NULL,
    validation_message character varying(500),
    validated_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_insurance_validations PRIMARY KEY (id),
    CONSTRAINT fk_insurance_validations_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id) ON DELETE RESTRICT
);

CREATE TABLE notifications (
    id uuid NOT NULL,
    patient_id uuid NOT NULL,
    appointment_id uuid,
    channel character varying(20) NOT NULL,
    template_type character varying(150) NOT NULL,
    status character varying(30) NOT NULL,
    sent_at timestamp with time zone,
    retry_count integer NOT NULL,
    error_message character varying(500),
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_notifications PRIMARY KEY (id),
    CONSTRAINT fk_notifications_appointments_appointment_id FOREIGN KEY (appointment_id) REFERENCES appointments (id) ON DELETE RESTRICT,
    CONSTRAINT fk_notifications_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id) ON DELETE RESTRICT
);

CREATE INDEX ix_audit_logs_patient_id ON audit_logs (patient_id);

CREATE INDEX ix_audit_logs_timestamp ON audit_logs (timestamp DESC);

CREATE INDEX ix_audit_logs_user_id ON audit_logs (user_id);

CREATE INDEX ix_calendar_sync_appointment_id ON calendar_syncs (appointment_id);

CREATE UNIQUE INDEX ix_calendar_sync_provider_external_id ON calendar_syncs (provider, external_event_id);

CREATE INDEX ix_calendar_syncs_patient_id ON calendar_syncs (patient_id);

CREATE INDEX ix_insurance_validations_patient_id ON insurance_validations (patient_id);

CREATE INDEX ix_insurance_validations_result ON insurance_validations (validation_result);

CREATE INDEX ix_notifications_appointment_id ON notifications (appointment_id);

CREATE INDEX ix_notifications_patient_id ON notifications (patient_id);

CREATE INDEX ix_notifications_status ON notifications (status);

INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20260420190747_AddAuditNotificationEntities', '9.0.15');

COMMIT;

