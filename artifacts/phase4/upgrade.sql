START TRANSACTION;
CREATE TABLE sonda.file_objects (
    team_id text NOT NULL,
    id uuid NOT NULL,
    profile_id text NOT NULL,
    source_key text NOT NULL,
    identity_key text NOT NULL,
    identity text NOT NULL,
    CONSTRAINT "PK_file_objects" PRIMARY KEY (team_id, id),
    CONSTRAINT "FK_file_objects_log_sources_team_id_profile_id_source_key" FOREIGN KEY (team_id, profile_id, source_key) REFERENCES sonda.log_sources (team_id, profile_id, source_key) ON DELETE RESTRICT
);

CREATE TABLE sonda.monitoring_sessions (
    team_id text NOT NULL,
    application_id text NOT NULL,
    session_id uuid NOT NULL,
    host_authority text NOT NULL,
    CONSTRAINT "PK_monitoring_sessions" PRIMARY KEY (team_id, application_id),
    CONSTRAINT "FK_monitoring_sessions_application_runtime_team_id_session_id_~" FOREIGN KEY (team_id, session_id, application_id) REFERENCES sonda.application_runtime (team_id, session_id, application_id) ON DELETE RESTRICT
);

CREATE TABLE sonda.source_acquisition_revisions (
    team_id text NOT NULL,
    profile_id text NOT NULL,
    source_key text NOT NULL,
    revision bigint NOT NULL,
    configuration text NOT NULL,
    hash text NOT NULL,
    CONSTRAINT "PK_source_acquisition_revisions" PRIMARY KEY (team_id, profile_id, source_key, revision),
    CONSTRAINT "FK_source_acquisition_revisions_log_sources_team_id_profile_id~" FOREIGN KEY (team_id, profile_id, source_key) REFERENCES sonda.log_sources (team_id, profile_id, source_key) ON DELETE RESTRICT
);

CREATE TABLE sonda.source_membership_sets (
    team_id text NOT NULL,
    profile_id text NOT NULL,
    revision bigint NOT NULL,
    sources text NOT NULL,
    effective_sequence bigint NOT NULL,
    CONSTRAINT "PK_source_membership_sets" PRIMARY KEY (team_id, profile_id, revision),
    CONSTRAINT "FK_source_membership_sets_profiles_team_id_profile_id" FOREIGN KEY (team_id, profile_id) REFERENCES sonda.profiles (team_id, profile_id) ON DELETE RESTRICT
);

CREATE TABLE sonda.source_operational_events (
    team_id text NOT NULL,
    id uuid NOT NULL,
    profile_id text NOT NULL,
    source_key text NOT NULL,
    processed_at timestamp with time zone NOT NULL,
    payload text NOT NULL,
    receipt_id uuid,
    CONSTRAINT "PK_source_operational_events" PRIMARY KEY (team_id, id),
    CONSTRAINT "FK_source_operational_events_log_sources_team_id_profile_id_so~" FOREIGN KEY (team_id, profile_id, source_key) REFERENCES sonda.log_sources (team_id, profile_id, source_key) ON DELETE RESTRICT
);

CREATE TABLE sonda.source_scan_batches (
    team_id text NOT NULL,
    id uuid NOT NULL,
    profile_id text NOT NULL,
    source_key text NOT NULL,
    observed_at timestamp with time zone NOT NULL,
    payload text NOT NULL,
    CONSTRAINT "PK_source_scan_batches" PRIMARY KEY (team_id, id),
    CONSTRAINT "FK_source_scan_batches_log_sources_team_id_profile_id_source_k~" FOREIGN KEY (team_id, profile_id, source_key) REFERENCES sonda.log_sources (team_id, profile_id, source_key) ON DELETE RESTRICT
);

CREATE TABLE sonda.file_path_observations (
    team_id text NOT NULL,
    id uuid NOT NULL,
    object_id uuid NOT NULL,
    path text NOT NULL,
    observed_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_file_path_observations" PRIMARY KEY (team_id, id),
    CONSTRAINT "FK_file_path_observations_file_objects_team_id_object_id" FOREIGN KEY (team_id, object_id) REFERENCES sonda.file_objects (team_id, id) ON DELETE RESTRICT
);

CREATE TABLE sonda.ingestion_owners (
    team_id text NOT NULL,
    application_id text NOT NULL,
    instance_id uuid NOT NULL,
    epoch bigint NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ingestion_owners" PRIMARY KEY (team_id, application_id),
    CONSTRAINT "FK_ingestion_owners_monitoring_sessions_team_id_application_id" FOREIGN KEY (team_id, application_id) REFERENCES sonda.monitoring_sessions (team_id, application_id) ON DELETE RESTRICT
);

CREATE TABLE sonda.file_generations (
    team_id text NOT NULL,
    id uuid NOT NULL,
    object_id uuid NOT NULL,
    epoch bigint NOT NULL,
    profile_id text NOT NULL,
    source_key text NOT NULL,
    source_revision bigint NOT NULL,
    predecessor uuid,
    state text NOT NULL,
    observed_length bigint NOT NULL,
    prefix_hash text NOT NULL,
    prefix_length integer NOT NULL,
    gap text,
    configuration text NOT NULL,
    CONSTRAINT "PK_file_generations" PRIMARY KEY (team_id, id),
    CONSTRAINT "FK_file_generations_file_generations_team_id_predecessor" FOREIGN KEY (team_id, predecessor) REFERENCES sonda.file_generations (team_id, id) ON DELETE RESTRICT,
    CONSTRAINT "FK_file_generations_file_objects_team_id_object_id" FOREIGN KEY (team_id, object_id) REFERENCES sonda.file_objects (team_id, id) ON DELETE RESTRICT,
    CONSTRAINT "FK_file_generations_source_acquisition_revisions_team_id_profi~" FOREIGN KEY (team_id, profile_id, source_key, source_revision) REFERENCES sonda.source_acquisition_revisions (team_id, profile_id, source_key, revision) ON DELETE RESTRICT
);

CREATE TABLE sonda.source_acquisition_runtime (
    team_id text NOT NULL,
    profile_id text NOT NULL,
    source_key text NOT NULL,
    revision bigint NOT NULL,
    enabled boolean NOT NULL,
    state text NOT NULL,
    error text,
    last_observation uuid,
    CONSTRAINT "PK_source_acquisition_runtime" PRIMARY KEY (team_id, profile_id, source_key),
    CONSTRAINT "FK_source_acquisition_runtime_source_acquisition_revisions_tea~" FOREIGN KEY (team_id, profile_id, source_key, revision) REFERENCES sonda.source_acquisition_revisions (team_id, profile_id, source_key, revision) ON DELETE RESTRICT
);

CREATE TABLE sonda.frontier_certificates (
    team_id text NOT NULL,
    id uuid NOT NULL,
    profile_id text NOT NULL,
    membership_revision bigint NOT NULL,
    payload text NOT NULL,
    status text NOT NULL,
    receipt_id uuid,
    CONSTRAINT "PK_frontier_certificates" PRIMARY KEY (team_id, id),
    CONSTRAINT "FK_frontier_certificates_source_membership_sets_team_id_profil~" FOREIGN KEY (team_id, profile_id, membership_revision) REFERENCES sonda.source_membership_sets (team_id, profile_id, revision) ON DELETE RESTRICT
);

CREATE TABLE sonda.file_checkpoints (
    team_id text NOT NULL,
    generation_id uuid NOT NULL,
    "offset" bigint NOT NULL,
    revision bigint NOT NULL,
    receipt_id uuid,
    fence_epoch bigint NOT NULL,
    CONSTRAINT "PK_file_checkpoints" PRIMARY KEY (team_id, generation_id),
    CONSTRAINT "FK_file_checkpoints_file_generations_team_id_generation_id" FOREIGN KEY (team_id, generation_id) REFERENCES sonda.file_generations (team_id, id) ON DELETE RESTRICT
);

CREATE TABLE sonda.file_record_evidence (
    team_id text NOT NULL,
    generation_id uuid NOT NULL,
    start_offset bigint NOT NULL,
    end_offset bigint NOT NULL,
    bytes bytea NOT NULL,
    hash text NOT NULL,
    session_id uuid NOT NULL,
    evidence_id uuid NOT NULL,
    receipt_id uuid NOT NULL,
    fence_epoch bigint NOT NULL,
    record text NOT NULL,
    CONSTRAINT "PK_file_record_evidence" PRIMARY KEY (team_id, generation_id, start_offset),
    CONSTRAINT "FK_file_record_evidence_file_generations_team_id_generation_id" FOREIGN KEY (team_id, generation_id) REFERENCES sonda.file_generations (team_id, id) ON DELETE RESTRICT,
    CONSTRAINT "FK_file_record_evidence_processing_receipts_team_id_session_id~" FOREIGN KEY (team_id, session_id, receipt_id) REFERENCES sonda.processing_receipts (team_id, session_id, id) ON DELETE RESTRICT,
    CONSTRAINT "FK_file_record_evidence_raw_evidence_team_id_session_id_eviden~" FOREIGN KEY (team_id, session_id, evidence_id) REFERENCES sonda.raw_evidence (team_id, session_id, id) ON DELETE RESTRICT
);

CREATE TABLE sonda.ingestion_pending_commands (
    team_id text NOT NULL,
    application_id text NOT NULL,
    command_id uuid NOT NULL,
    profile_id text NOT NULL,
    generation_id uuid,
    start_offset bigint,
    end_offset bigint,
    bytes bytea,
    record text,
    command text NOT NULL,
    fingerprint text NOT NULL,
    certificate text,
    CONSTRAINT "PK_ingestion_pending_commands" PRIMARY KEY (team_id, application_id),
    CONSTRAINT "FK_ingestion_pending_commands_file_generations_team_id_generat~" FOREIGN KEY (team_id, generation_id) REFERENCES sonda.file_generations (team_id, id) ON DELETE RESTRICT,
    CONSTRAINT "FK_ingestion_pending_commands_monitoring_sessions_team_id_appl~" FOREIGN KEY (team_id, application_id) REFERENCES sonda.monitoring_sessions (team_id, application_id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_file_generations_team_id_object_id_epoch" ON sonda.file_generations (team_id, object_id, epoch);

CREATE INDEX "IX_file_generations_team_id_predecessor" ON sonda.file_generations (team_id, predecessor);

CREATE INDEX "IX_file_generations_team_id_profile_id_source_key_source_revis~" ON sonda.file_generations (team_id, profile_id, source_key, source_revision);

CREATE UNIQUE INDEX "IX_file_objects_team_id_identity_key" ON sonda.file_objects (team_id, identity_key);

CREATE INDEX "IX_file_objects_team_id_profile_id_source_key" ON sonda.file_objects (team_id, profile_id, source_key);

CREATE INDEX "IX_file_path_observations_team_id_object_id" ON sonda.file_path_observations (team_id, object_id);

CREATE UNIQUE INDEX "IX_file_record_evidence_team_id_session_id_evidence_id" ON sonda.file_record_evidence (team_id, session_id, evidence_id);

CREATE INDEX "IX_file_record_evidence_team_id_session_id_receipt_id" ON sonda.file_record_evidence (team_id, session_id, receipt_id);

CREATE INDEX "IX_frontier_certificates_team_id_profile_id_membership_revision" ON sonda.frontier_certificates (team_id, profile_id, membership_revision);

CREATE UNIQUE INDEX "IX_ingestion_pending_commands_team_id_command_id" ON sonda.ingestion_pending_commands (team_id, command_id);

CREATE INDEX "IX_ingestion_pending_commands_team_id_generation_id" ON sonda.ingestion_pending_commands (team_id, generation_id);

CREATE UNIQUE INDEX "IX_monitoring_sessions_team_id_session_id" ON sonda.monitoring_sessions (team_id, session_id);

CREATE INDEX "IX_monitoring_sessions_team_id_session_id_application_id" ON sonda.monitoring_sessions (team_id, session_id, application_id);

CREATE INDEX "IX_source_acquisition_runtime_team_id_profile_id_source_key_re~" ON sonda.source_acquisition_runtime (team_id, profile_id, source_key, revision);

CREATE INDEX "IX_source_operational_events_team_id_profile_id_source_key" ON sonda.source_operational_events (team_id, profile_id, source_key);

CREATE INDEX "IX_source_scan_batches_team_id_profile_id_source_key" ON sonda.source_scan_batches (team_id, profile_id, source_key);

ALTER TABLE sonda.file_checkpoints ADD CONSTRAINT checkpoint_nonnegative CHECK ("offset">=0 AND revision>=0);
ALTER TABLE sonda.file_generations ADD CONSTRAINT generation_epoch CHECK (epoch>0 AND observed_length>=0 AND prefix_length>=0);
ALTER TABLE sonda.file_record_evidence ADD CONSTRAINT physical_range CHECK(start_offset>=0 AND end_offset>start_offset AND octet_length(bytes)=end_offset-start_offset);
CREATE FUNCTION sonda.assert_ingestion_fence(t text,a text) RETURNS void LANGUAGE plpgsql AS $$ DECLARE o sonda.ingestion_owners; BEGIN
 SELECT * INTO o FROM sonda.ingestion_owners WHERE team_id=t AND application_id=a FOR UPDATE;
 IF NOT FOUND OR o.expires_at<=clock_timestamp() OR o.epoch::text IS DISTINCT FROM current_setting('sonda.fence_epoch',true) OR o.instance_id::text IS DISTINCT FROM current_setting('sonda.fence_instance',true) THEN RAISE EXCEPTION 'lost ingestion fence' USING ERRCODE='40001'; END IF;
END $$;
CREATE FUNCTION sonda.ingestion_receipt_guard() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE p sonda.ingestion_pending_commands; BEGIN
 IF EXISTS(SELECT 1 FROM sonda.monitoring_sessions WHERE team_id=NEW.team_id AND session_id=NEW.session_id AND application_id=NEW.application_id) THEN
  PERFORM sonda.assert_ingestion_fence(NEW.team_id,NEW.application_id);
  SELECT * INTO p FROM sonda.ingestion_pending_commands WHERE team_id=NEW.team_id AND application_id=NEW.application_id;
  IF NOT FOUND OR p.command='' OR p.command_id<>NEW.request_id OR p.profile_id<>NEW.profile_id THEN RAISE EXCEPTION 'monitored command requires reservation'; END IF;
 END IF; RETURN NEW;
END $$;
CREATE TRIGGER ingestion_receipt_guard BEFORE INSERT ON sonda.processing_receipts FOR EACH ROW EXECUTE FUNCTION sonda.ingestion_receipt_guard();
CREATE FUNCTION sonda.ingestion_runtime_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF EXISTS(SELECT 1 FROM sonda.monitoring_sessions WHERE team_id=NEW.team_id AND session_id=NEW.session_id AND application_id=NEW.application_id) THEN PERFORM sonda.assert_ingestion_fence(NEW.team_id,NEW.application_id); END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER ingestion_runtime_guard BEFORE UPDATE ON sonda.application_runtime FOR EACH ROW EXECUTE FUNCTION sonda.ingestion_runtime_guard();
CREATE TRIGGER ingestion_lane_guard BEFORE UPDATE ON sonda.profile_runtime FOR EACH ROW EXECUTE FUNCTION sonda.ingestion_runtime_guard();
CREATE FUNCTION sonda.physical_record_guard() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE g sonda.file_generations; c sonda.file_checkpoints; r sonda.processing_receipts; e sonda.raw_evidence; a text; BEGIN
 SELECT * INTO g FROM sonda.file_generations WHERE (team_id,id)=(NEW.team_id,NEW.generation_id);
 SELECT application_id INTO a FROM sonda.profiles WHERE (team_id,profile_id)=(g.team_id,g.profile_id);
 PERFORM sonda.assert_ingestion_fence(NEW.team_id,a);
 SELECT * INTO c FROM sonda.file_checkpoints WHERE (team_id,generation_id)=(NEW.team_id,NEW.generation_id) FOR UPDATE;
 SELECT * INTO r FROM sonda.processing_receipts WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,NEW.receipt_id);
 SELECT * INTO e FROM sonda.raw_evidence WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,NEW.evidence_id);
 IF c.offset IS DISTINCT FROM NEW.start_offset OR NEW.fence_epoch::text IS DISTINCT FROM current_setting('sonda.fence_epoch',true) OR r.evidence_id IS DISTINCT FROM e.id OR e.profile_id IS DISTINCT FROM g.profile_id OR e.source_key IS DISTINCT FROM g.source_key OR e.generation IS DISTINCT FROM g.id::text OR e.source_ordinal IS DISTINCT FROM NEW.start_offset THEN RAISE EXCEPTION 'invalid physical record provenance or contiguous offset'; END IF;
 IF NOT EXISTS(SELECT 1 FROM sonda.ingestion_pending_commands p WHERE p.team_id=NEW.team_id AND p.application_id=a AND p.command_id=r.request_id AND p.generation_id=g.id AND p.start_offset=NEW.start_offset AND p.end_offset=NEW.end_offset AND p.bytes=NEW.bytes) THEN RAISE EXCEPTION 'record differs from reserved bytes'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER physical_record_guard BEFORE INSERT ON sonda.file_record_evidence FOR EACH ROW EXECUTE FUNCTION sonda.physical_record_guard();
CREATE FUNCTION sonda.checkpoint_guard() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE a text; BEGIN
 SELECT p.application_id INTO a FROM sonda.file_generations g JOIN sonda.profiles p ON (p.team_id,p.profile_id)=(g.team_id,g.profile_id) WHERE (g.team_id,g.id)=(NEW.team_id,NEW.generation_id);
 PERFORM sonda.assert_ingestion_fence(NEW.team_id,a);
 IF TG_OP='INSERT' THEN IF NEW.offset<>0 OR NEW.revision<>0 OR NEW.receipt_id IS NOT NULL THEN RAISE EXCEPTION 'checkpoint must begin at zero'; END IF;
 ELSE
  IF (NEW.team_id,NEW.generation_id) IS DISTINCT FROM (OLD.team_id,OLD.generation_id) OR NEW.offset<=OLD.offset OR NEW.revision<>OLD.revision+1 OR NOT EXISTS(SELECT 1 FROM sonda.file_record_evidence f WHERE (f.team_id,f.generation_id,f.start_offset,f.end_offset,f.receipt_id,f.fence_epoch)=(NEW.team_id,NEW.generation_id,OLD.offset,NEW.offset,NEW.receipt_id,NEW.fence_epoch)) THEN RAISE EXCEPTION 'checkpoint requires contiguous committed record'; END IF;
 END IF; RETURN NEW;
END $$;
CREATE TRIGGER checkpoint_guard BEFORE INSERT OR UPDATE ON sonda.file_checkpoints FOR EACH ROW EXECUTE FUNCTION sonda.checkpoint_guard();
CREATE FUNCTION sonda.physical_record_committed() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM sonda.file_checkpoints c WHERE (c.team_id,c.generation_id)=(NEW.team_id,NEW.generation_id) AND c.offset>=NEW.end_offset) THEN RAISE EXCEPTION 'physical record and checkpoint must commit together'; END IF;
 RETURN NEW;
END $$;
CREATE CONSTRAINT TRIGGER physical_record_committed AFTER INSERT ON sonda.file_record_evidence DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sonda.physical_record_committed();
CREATE FUNCTION sonda.pending_ingestion_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 PERFORM sonda.assert_ingestion_fence(NEW.team_id,NEW.application_id);
 IF TG_OP='UPDATE' AND OLD.command<>'' THEN
  IF NEW.command<>'' OR NOT EXISTS(SELECT 1 FROM sonda.processing_receipts r JOIN sonda.monitoring_sessions s ON (s.team_id,s.session_id)=(r.team_id,r.session_id) WHERE s.team_id=OLD.team_id AND s.application_id=OLD.application_id AND r.request_id=OLD.command_id) THEN RAISE EXCEPTION 'reserved command immutable until its receipt commits'; END IF;
 END IF;
 IF NEW.command<>'' AND (NEW.command::jsonb->>'id')::uuid IS DISTINCT FROM NEW.command_id THEN RAISE EXCEPTION 'reservation command identity mismatch'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER pending_ingestion_guard BEFORE INSERT OR UPDATE ON sonda.ingestion_pending_commands FOR EACH ROW EXECUTE FUNCTION sonda.pending_ingestion_guard();
CREATE FUNCTION sonda.generation_identity_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM sonda.file_objects o WHERE (o.team_id,o.id,o.profile_id,o.source_key)=(NEW.team_id,NEW.object_id,NEW.profile_id,NEW.source_key)) THEN RAISE EXCEPTION 'generation physical owner mismatch'; END IF;
 IF NEW.predecessor IS NOT NULL AND NOT EXISTS(SELECT 1 FROM sonda.file_generations g WHERE (g.team_id,g.id,g.profile_id,g.source_key)=(NEW.team_id,NEW.predecessor,NEW.profile_id,NEW.source_key) AND g.id<>NEW.id) THEN RAISE EXCEPTION 'foreign predecessor'; END IF;
 IF NOT EXISTS(SELECT 1 FROM sonda.source_acquisition_revisions r WHERE (r.team_id,r.profile_id,r.source_key,r.revision)=(NEW.team_id,NEW.profile_id,NEW.source_key,NEW.source_revision) AND r.configuration=NEW.configuration) THEN RAISE EXCEPTION 'generation framing revision mismatch'; END IF;
 IF TG_OP='UPDATE' AND (NEW.team_id,NEW.id,NEW.object_id,NEW.epoch,NEW.profile_id,NEW.source_key,NEW.source_revision,NEW.predecessor,NEW.configuration) IS DISTINCT FROM (OLD.team_id,OLD.id,OLD.object_id,OLD.epoch,OLD.profile_id,OLD.source_key,OLD.source_revision,OLD.predecessor,OLD.configuration) THEN RAISE EXCEPTION 'generation identity immutable'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER generation_identity_guard BEFORE INSERT OR UPDATE ON sonda.file_generations FOR EACH ROW EXECUTE FUNCTION sonda.generation_identity_guard();
CREATE FUNCTION sonda.ingestion_commit_fence() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE a text; BEGIN
 IF TG_TABLE_NAME='ingestion_pending_commands' THEN PERFORM sonda.assert_ingestion_fence(NEW.team_id,NEW.application_id); RETURN NEW; END IF;
 IF TG_TABLE_NAME IN ('processing_receipts','application_runtime','profile_runtime') THEN
  IF EXISTS(SELECT 1 FROM sonda.monitoring_sessions WHERE team_id=NEW.team_id AND session_id=NEW.session_id AND application_id=NEW.application_id) THEN PERFORM sonda.assert_ingestion_fence(NEW.team_id,NEW.application_id); END IF;
 ELSE
  IF TG_TABLE_NAME='file_checkpoints' THEN
   SELECT p.application_id INTO a FROM sonda.file_generations g JOIN sonda.profiles p ON (p.team_id,p.profile_id)=(g.team_id,g.profile_id) WHERE (g.team_id,g.id)=(NEW.team_id,NEW.generation_id);
  ELSIF TG_TABLE_NAME='file_path_observations' THEN
   SELECT p.application_id INTO a FROM sonda.file_objects o JOIN sonda.profiles p ON (p.team_id,p.profile_id)=(o.team_id,o.profile_id) WHERE (o.team_id,o.id)=(NEW.team_id,NEW.object_id);
  ELSE SELECT application_id INTO a FROM sonda.profiles WHERE team_id=NEW.team_id AND profile_id=NEW.profile_id; END IF;
  IF EXISTS(SELECT 1 FROM sonda.ingestion_owners WHERE team_id=NEW.team_id AND application_id=a AND epoch>0) THEN PERFORM sonda.assert_ingestion_fence(NEW.team_id,a); END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE CONSTRAINT TRIGGER ingestion_commit_fence AFTER INSERT ON sonda.processing_receipts DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sonda.ingestion_commit_fence();
CREATE CONSTRAINT TRIGGER ingestion_commit_fence AFTER UPDATE ON sonda.application_runtime DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sonda.ingestion_commit_fence();
CREATE CONSTRAINT TRIGGER ingestion_commit_fence AFTER INSERT OR UPDATE ON sonda.file_checkpoints DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sonda.ingestion_commit_fence();
CREATE CONSTRAINT TRIGGER ingestion_commit_fence AFTER INSERT OR UPDATE ON sonda.file_generations DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sonda.ingestion_commit_fence();
CREATE CONSTRAINT TRIGGER ingestion_commit_fence AFTER INSERT OR UPDATE ON sonda.source_acquisition_runtime DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sonda.ingestion_commit_fence();
CREATE CONSTRAINT TRIGGER ingestion_commit_fence AFTER INSERT OR UPDATE ON sonda.frontier_certificates DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sonda.ingestion_commit_fence();
CREATE CONSTRAINT TRIGGER ingestion_commit_fence AFTER INSERT ON sonda.source_operational_events DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sonda.ingestion_commit_fence();
CREATE CONSTRAINT TRIGGER ingestion_commit_fence AFTER INSERT OR UPDATE ON sonda.ingestion_pending_commands DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sonda.ingestion_commit_fence();
DO $$ DECLARE n text; BEGIN FOREACH n IN ARRAY ARRAY['source_acquisition_revisions','source_membership_sets','file_objects','file_path_observations','source_scan_batches'] LOOP EXECUTE format('CREATE CONSTRAINT TRIGGER ingestion_commit_fence AFTER INSERT ON sonda.%I DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sonda.ingestion_commit_fence()',n); END LOOP; END $$;
CREATE FUNCTION sonda.frontier_certificate_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF TG_OP='UPDATE' THEN
  IF (NEW.team_id,NEW.id,NEW.profile_id,NEW.membership_revision,NEW.payload) IS DISTINCT FROM (OLD.team_id,OLD.id,OLD.profile_id,OLD.membership_revision,OLD.payload) OR OLD.status<>'Validated' THEN RAISE EXCEPTION 'certificate identity/state immutable'; END IF;
 END IF;
 IF NEW.status NOT IN ('Validated','Consumed','Invalidated') THEN RAISE EXCEPTION 'invalid certificate state'; END IF;
 IF NEW.status='Consumed' AND NOT EXISTS(SELECT 1 FROM sonda.processing_receipts r WHERE r.team_id=NEW.team_id AND r.id=NEW.receipt_id AND r.profile_id=NEW.profile_id AND r.kind='PolicyAdvanceTime' AND r.request_id=NEW.id) THEN RAISE EXCEPTION 'certificate requires its deadline receipt'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER frontier_certificate_guard BEFORE INSERT OR UPDATE ON sonda.frontier_certificates FOR EACH ROW EXECUTE FUNCTION sonda.frontier_certificate_guard();
DO $$ DECLARE n text; BEGIN FOREACH n IN ARRAY ARRAY['source_acquisition_revisions','source_membership_sets','file_objects','file_path_observations','file_record_evidence','source_scan_batches','source_operational_events'] LOOP EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON sonda.%I FOR EACH ROW EXECUTE FUNCTION sonda.immutable()',n); END LOOP; END $$;


INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260924044645_FileAcquisition', '10.0.4');

COMMIT;

