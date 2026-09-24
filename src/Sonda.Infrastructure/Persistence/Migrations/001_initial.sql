CREATE SCHEMA sonda;
CREATE TABLE sonda.teams(team_id text COLLATE "C" PRIMARY KEY, name text NOT NULL);
CREATE TABLE sonda.applications(team_id text NOT NULL REFERENCES sonda.teams, application_id text COLLATE "C" NOT NULL, name text NOT NULL, revision bigint NOT NULL, PRIMARY KEY(team_id,application_id));
CREATE TABLE sonda.profiles(team_id text NOT NULL, profile_id text COLLATE "C" NOT NULL, application_id text NOT NULL, draft text NOT NULL, revision bigint NOT NULL, PRIMARY KEY(team_id,profile_id), FOREIGN KEY(team_id,application_id) REFERENCES sonda.applications);
CREATE TABLE sonda.profile_versions(team_id text NOT NULL, profile_id text NOT NULL, version integer NOT NULL CHECK(version>0), snapshot text NOT NULL, hash text NOT NULL, report text NOT NULL, request text NOT NULL, sealed boolean NOT NULL DEFAULT false, PRIMARY KEY(team_id,profile_id,version), FOREIGN KEY(team_id,profile_id) REFERENCES sonda.profiles);
CREATE TABLE sonda.profile_rules(team_id text NOT NULL,profile_id text NOT NULL,version int NOT NULL,key text NOT NULL,ordinal int NOT NULL CHECK(ordinal>=0),payload text NOT NULL,PRIMARY KEY(team_id,profile_id,version,key),UNIQUE(team_id,profile_id,version,ordinal),FOREIGN KEY(team_id,profile_id,version) REFERENCES sonda.profile_versions);
CREATE TABLE sonda.profile_patterns(team_id text NOT NULL,profile_id text NOT NULL,version int NOT NULL,rule_key text NOT NULL,ordinal int NOT NULL CHECK(ordinal>=0),payload text NOT NULL,PRIMARY KEY(team_id,profile_id,version,rule_key,ordinal),FOREIGN KEY(team_id,profile_id,version,rule_key) REFERENCES sonda.profile_rules(team_id,profile_id,version,key));
CREATE TABLE sonda.profile_parsing_configurations(team_id text NOT NULL,profile_id text NOT NULL,version int NOT NULL,payload text NOT NULL,PRIMARY KEY(team_id,profile_id,version),FOREIGN KEY(team_id,profile_id,version) REFERENCES sonda.profile_versions);
CREATE TABLE sonda.profile_identifier_configurations(LIKE sonda.profile_parsing_configurations INCLUDING ALL, FOREIGN KEY(team_id,profile_id,version) REFERENCES sonda.profile_versions);
CREATE TABLE sonda.log_sources(team_id text NOT NULL,profile_id text NOT NULL,source_key text NOT NULL,configuration text NOT NULL,revision bigint NOT NULL,PRIMARY KEY(team_id,profile_id,source_key),FOREIGN KEY(team_id,profile_id) REFERENCES sonda.profiles);
CREATE TABLE sonda.processing_sessions(team_id text NOT NULL REFERENCES sonda.teams,session_id uuid NOT NULL,seed text NOT NULL,kind text NOT NULL CHECK(kind IN ('Simulation','DurabilityHarness')),zone text NOT NULL,zone_rules text NOT NULL,PRIMARY KEY(team_id,session_id));
CREATE TABLE sonda.application_runtime(team_id text NOT NULL,session_id uuid NOT NULL,application_id text NOT NULL,next_sequence bigint NOT NULL CHECK(next_sequence>0),last_processed_ticks bigint NOT NULL,revision bigint NOT NULL,PRIMARY KEY(team_id,session_id,application_id),FOREIGN KEY(team_id,session_id) REFERENCES sonda.processing_sessions,FOREIGN KEY(team_id,application_id) REFERENCES sonda.applications);
CREATE TABLE sonda.profile_runtime(team_id text NOT NULL,session_id uuid NOT NULL,profile_id text NOT NULL,application_id text NOT NULL,version int NOT NULL,cycle_sequence int NOT NULL CHECK(cycle_sequence>=0),id_counter bigint NOT NULL CHECK(id_counter>=0),blocked boolean NOT NULL,revision bigint NOT NULL,PRIMARY KEY(team_id,session_id,profile_id),FOREIGN KEY(team_id,session_id,application_id) REFERENCES sonda.application_runtime,FOREIGN KEY(team_id,profile_id,version) REFERENCES sonda.profile_versions);
CREATE TABLE sonda.profile_activations(team_id text NOT NULL,session_id uuid NOT NULL,profile_id text NOT NULL,revision bigint NOT NULL,version int NOT NULL,effective_sequence bigint NOT NULL,command_id uuid NOT NULL,PRIMARY KEY(team_id,session_id,profile_id,revision),UNIQUE(team_id,session_id,command_id),FOREIGN KEY(team_id,session_id,profile_id) REFERENCES sonda.profile_runtime,FOREIGN KEY(team_id,profile_id,version) REFERENCES sonda.profile_versions);
CREATE TABLE sonda.raw_evidence(team_id text NOT NULL,session_id uuid NOT NULL,id uuid NOT NULL,profile_id text NOT NULL,source_key text NOT NULL,generation text NOT NULL,source_ordinal bigint NOT NULL CHECK(source_ordinal>=0),raw text NOT NULL,PRIMARY KEY(team_id,session_id,id),UNIQUE(team_id,session_id,profile_id,source_key,generation,source_ordinal),FOREIGN KEY(team_id,session_id,profile_id) REFERENCES sonda.profile_runtime,FOREIGN KEY(team_id,profile_id,source_key) REFERENCES sonda.log_sources);
CREATE TABLE sonda.processing_receipts(team_id text NOT NULL,session_id uuid NOT NULL,id uuid NOT NULL,request_id uuid NOT NULL,evidence_id uuid NOT NULL,application_id text NOT NULL,profile_id text NOT NULL,version int NOT NULL,sequence bigint NOT NULL,line int NOT NULL CHECK(line>0),fingerprint text NOT NULL,trace text NOT NULL,processed_ticks bigint NOT NULL,processed_at timestamptz NOT NULL,recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),transaction_id text NOT NULL DEFAULT pg_current_xact_id()::text,PRIMARY KEY(team_id,session_id,id),UNIQUE(team_id,session_id,request_id),UNIQUE(team_id,session_id,evidence_id),UNIQUE(team_id,session_id,line),UNIQUE(team_id,session_id,application_id,sequence),FOREIGN KEY(team_id,session_id,evidence_id) REFERENCES sonda.raw_evidence,FOREIGN KEY(team_id,profile_id,version) REFERENCES sonda.profile_versions,FOREIGN KEY(team_id,session_id,application_id) REFERENCES sonda.application_runtime);
CREATE TABLE sonda.normalized_evidence(team_id text NOT NULL,session_id uuid NOT NULL,receipt_id uuid NOT NULL,source_timestamp text,quality text NOT NULL,event_ticks bigint NOT NULL,event_at timestamptz NOT NULL,payload text NOT NULL,PRIMARY KEY(team_id,session_id,receipt_id),FOREIGN KEY(team_id,session_id,receipt_id) REFERENCES sonda.processing_receipts);
CREATE TABLE sonda.runs(team_id text NOT NULL,session_id uuid NOT NULL,id text COLLATE "C" NOT NULL,profile_id text NOT NULL,version int NOT NULL,scope text NOT NULL CHECK(scope IN ('Application','Order')),cycle_sequence int NOT NULL CHECK(cycle_sequence>0),parent_id text,identifier text COLLATE "C",attempt int NOT NULL CHECK(attempt>0),result text CHECK(result IN ('Success','Failure','Undefined')),created_receipt uuid NOT NULL,completed_receipt uuid,payload text NOT NULL,PRIMARY KEY(team_id,session_id,id),FOREIGN KEY(team_id,profile_id,version) REFERENCES sonda.profile_versions,FOREIGN KEY(team_id,session_id,parent_id) REFERENCES sonda.runs DEFERRABLE INITIALLY DEFERRED,FOREIGN KEY(team_id,session_id,created_receipt) REFERENCES sonda.processing_receipts,FOREIGN KEY(team_id,session_id,completed_receipt) REFERENCES sonda.processing_receipts,CHECK((scope='Application' AND parent_id IS NULL AND identifier IS NULL AND attempt=1) OR (scope='Order' AND parent_id IS NOT NULL AND length(identifier)>0)),CHECK((result IS NULL)=(completed_receipt IS NULL)));
CREATE UNIQUE INDEX cycle_identity ON sonda.runs(team_id,session_id,profile_id,cycle_sequence) WHERE scope='Application';
CREATE INDEX order_identity_lookup ON sonda.runs(team_id,session_id,parent_id,attempt) WHERE scope='Order';
CREATE TABLE sonda.problem_identities(team_id text NOT NULL,session_id uuid NOT NULL,id uuid NOT NULL,key text COLLATE "C" NOT NULL,digest text NOT NULL,PRIMARY KEY(team_id,session_id,id),FOREIGN KEY(team_id,session_id) REFERENCES sonda.processing_sessions);
CREATE INDEX problem_lookup ON sonda.problem_identities(team_id,session_id,digest);
CREATE TABLE sonda.incidents(team_id text NOT NULL,session_id uuid NOT NULL,id text NOT NULL,profile_id text NOT NULL,problem_id uuid NOT NULL,problem text NOT NULL,recovery_policy text NOT NULL CHECK(recovery_policy IN ('ManualOnly','NextSuccessfulRun')),severity text NOT NULL CHECK(severity IN ('Error','Warning')),status text NOT NULL CHECK(status IN ('Active','Investigating','Resolved')),revision bigint NOT NULL,created_receipt uuid NOT NULL,PRIMARY KEY(team_id,session_id,id),FOREIGN KEY(team_id,session_id,problem_id) REFERENCES sonda.problem_identities,FOREIGN KEY(team_id,session_id,profile_id) REFERENCES sonda.profile_runtime,FOREIGN KEY(team_id,session_id,created_receipt) REFERENCES sonda.processing_receipts);
CREATE UNIQUE INDEX unresolved_problem ON sonda.incidents(team_id,session_id,problem_id) WHERE status IN ('Active','Investigating');
CREATE TABLE sonda.incident_occurrences(team_id text NOT NULL,session_id uuid NOT NULL,id text NOT NULL,incident_id text NOT NULL,run_id text NOT NULL,created_receipt uuid NOT NULL,updated_receipt uuid NOT NULL,payload text NOT NULL,PRIMARY KEY(team_id,session_id,id),UNIQUE(team_id,session_id,incident_id,run_id),FOREIGN KEY(team_id,session_id,incident_id) REFERENCES sonda.incidents,FOREIGN KEY(team_id,session_id,run_id) REFERENCES sonda.runs,FOREIGN KEY(team_id,session_id,created_receipt) REFERENCES sonda.processing_receipts,FOREIGN KEY(team_id,session_id,updated_receipt) REFERENCES sonda.processing_receipts);
CREATE TABLE sonda.recoveries(team_id text NOT NULL,session_id uuid NOT NULL,incident_id text NOT NULL,run_id text NOT NULL,receipt_id uuid NOT NULL,payload text NOT NULL,PRIMARY KEY(team_id,session_id,incident_id,run_id),FOREIGN KEY(team_id,session_id,incident_id) REFERENCES sonda.incidents,FOREIGN KEY(team_id,session_id,run_id) REFERENCES sonda.runs,FOREIGN KEY(team_id,session_id,receipt_id) REFERENCES sonda.processing_receipts);
CREATE TABLE sonda.incident_status_history(team_id text NOT NULL,session_id uuid NOT NULL,incident_id text NOT NULL,ordinal int NOT NULL,origin uuid NOT NULL,payload text NOT NULL,PRIMARY KEY(team_id,session_id,incident_id,ordinal),UNIQUE(team_id,session_id,incident_id,origin,ordinal),FOREIGN KEY(team_id,session_id,incident_id) REFERENCES sonda.incidents);
CREATE TABLE sonda.run_metric_facts(team_id text NOT NULL,session_id uuid NOT NULL,run_id text NOT NULL,receipt_id uuid NOT NULL,scope text NOT NULL,result text NOT NULL,event_ticks bigint NOT NULL,processed_ticks bigint NOT NULL,event_at timestamptz NOT NULL,processed_at timestamptz NOT NULL,event_date date NOT NULL,processed_date date NOT NULL,PRIMARY KEY(team_id,session_id,run_id),FOREIGN KEY(team_id,session_id,run_id) REFERENCES sonda.runs,FOREIGN KEY(team_id,session_id,receipt_id) REFERENCES sonda.processing_receipts);
CREATE INDEX event_metric_date ON sonda.run_metric_facts(team_id,session_id,event_date);
CREATE INDEX volume_metric_date ON sonda.run_metric_facts(team_id,session_id,processed_date,scope);
CREATE TABLE sonda.command_receipts(team_id text NOT NULL REFERENCES sonda.teams,id uuid NOT NULL,fingerprint text NOT NULL,result text NOT NULL,PRIMARY KEY(team_id,id));
CREATE TABLE sonda.commit_observations(team_id text NOT NULL,session_id uuid NOT NULL,receipt_id uuid NOT NULL,database_commit_at timestamptz,acknowledged_at timestamptz NOT NULL,method text NOT NULL,PRIMARY KEY(team_id,session_id,receipt_id),FOREIGN KEY(team_id,session_id,receipt_id) REFERENCES sonda.processing_receipts);

CREATE FUNCTION sonda.immutable() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'immutable %',TG_TABLE_NAME USING ERRCODE='23514'; END $$;
DO $$ DECLARE n text; BEGIN FOREACH n IN ARRAY ARRAY['raw_evidence','processing_receipts','normalized_evidence','problem_identities','recoveries','incident_status_history','run_metric_facts','command_receipts','profile_activations'] LOOP EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON sonda.%I FOR EACH ROW EXECUTE FUNCTION sonda.immutable()',n); END LOOP; END $$;
CREATE FUNCTION sonda.version_guard() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_TABLE_NAME='profile_versions' THEN
  IF TG_OP='DELETE' OR OLD.sealed THEN RAISE EXCEPTION 'published version immutable' USING ERRCODE='23514'; END IF;
  IF NEW.sealed THEN
   IF NOT EXISTS(SELECT 1 FROM sonda.profile_parsing_configurations c WHERE (c.team_id,c.profile_id,c.version)=(NEW.team_id,NEW.profile_id,NEW.version) AND c.payload::jsonb=NEW.snapshot::jsonb->'parsing') THEN RAISE EXCEPTION 'parsing snapshot mismatch'; END IF;
   IF (SELECT count(*) FROM sonda.profile_rules r WHERE (r.team_id,r.profile_id,r.version)=(NEW.team_id,NEW.profile_id,NEW.version)) <> jsonb_array_length(NEW.snapshot::jsonb->'rules') THEN RAISE EXCEPTION 'rules snapshot mismatch'; END IF;
  END IF; RETURN NEW;
 END IF;
 IF EXISTS(SELECT 1 FROM sonda.profile_versions v WHERE (v.team_id,v.profile_id,v.version)=(COALESCE(NEW.team_id,OLD.team_id),COALESCE(NEW.profile_id,OLD.profile_id),COALESCE(NEW.version,OLD.version)) AND v.sealed) THEN RAISE EXCEPTION 'published child immutable' USING ERRCODE='23514'; END IF;
 RETURN COALESCE(NEW,OLD);
END $$;
CREATE TRIGGER version_guard BEFORE UPDATE OR DELETE ON sonda.profile_versions FOR EACH ROW EXECUTE FUNCTION sonda.version_guard();
DO $$ DECLARE n text; BEGIN FOREACH n IN ARRAY ARRAY['profile_rules','profile_patterns','profile_parsing_configurations','profile_identifier_configurations'] LOOP EXECUTE format('CREATE TRIGGER version_guard BEFORE INSERT OR UPDATE OR DELETE ON sonda.%I FOR EACH ROW EXECUTE FUNCTION sonda.version_guard()',n); END LOOP; END $$;
CREATE FUNCTION sonda.problem_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 -- Scope lock makes correctness independent of caller-provided digest, including forced collisions.
 PERFORM pg_advisory_xact_lock(hashtextextended(NEW.team_id||NEW.session_id::text,0));
 IF EXISTS(SELECT 1 FROM sonda.problem_identities p WHERE (p.team_id,p.session_id)=(NEW.team_id,NEW.session_id) AND p.key=NEW.key COLLATE "C") THEN RAISE EXCEPTION 'duplicate exact problem' USING ERRCODE='23505'; END IF; RETURN NEW;
END $$;
CREATE TRIGGER problem_guard BEFORE INSERT ON sonda.problem_identities FOR EACH ROW EXECUTE FUNCTION sonda.problem_guard();
CREATE FUNCTION sonda.revision_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.revision<>OLD.revision+1 THEN RAISE EXCEPTION 'revision must advance once' USING ERRCODE='23514'; END IF; RETURN NEW; END $$;
DO $$ DECLARE n text; BEGIN FOREACH n IN ARRAY ARRAY['profiles','applications','log_sources','incidents','profile_runtime','application_runtime'] LOOP EXECUTE format('CREATE TRIGGER revision_guard BEFORE UPDATE ON sonda.%I FOR EACH ROW EXECUTE FUNCTION sonda.revision_guard()',n); END LOOP; END $$;
CREATE FUNCTION sonda.run_guard() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE p sonda.runs; BEGIN
 IF TG_OP='UPDATE' AND OLD.result IS NOT NULL THEN RAISE EXCEPTION 'finalized run immutable' USING ERRCODE='23514'; END IF;
 IF NEW.payload::jsonb->>'id'<>NEW.id OR NEW.payload::jsonb->>'scope'<>NEW.scope OR (NEW.payload::jsonb->>'result') IS DISTINCT FROM NEW.result THEN RAISE EXCEPTION 'run payload mismatch'; END IF;
 IF NEW.scope='Order' THEN
  SELECT * INTO p FROM sonda.runs WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,NEW.parent_id) FOR UPDATE;
  IF p.scope IS DISTINCT FROM 'Application' OR (p.profile_id,p.version,p.cycle_sequence) IS DISTINCT FROM (NEW.profile_id,NEW.version,NEW.cycle_sequence) THEN RAISE EXCEPTION 'invalid parent'; END IF;
  IF EXISTS(SELECT 1 FROM sonda.runs r WHERE (r.team_id,r.session_id,r.parent_id,r.attempt)=(NEW.team_id,NEW.session_id,NEW.parent_id,NEW.attempt) AND r.identifier=NEW.identifier COLLATE "C" AND r.id<>NEW.id) THEN RAISE EXCEPTION 'duplicate order attempt' USING ERRCODE='23505'; END IF;
 END IF; RETURN NEW;
END $$;
CREATE TRIGGER run_guard BEFORE INSERT OR UPDATE ON sonda.runs FOR EACH ROW EXECUTE FUNCTION sonda.run_guard();
CREATE FUNCTION sonda.fact_guard() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE r sonda.runs; BEGIN
 SELECT * INTO r FROM sonda.runs WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,NEW.run_id);
 IF r.result IS NULL OR (r.scope,r.result,r.completed_receipt) IS DISTINCT FROM (NEW.scope,NEW.result,NEW.receipt_id) THEN RAISE EXCEPTION 'metric does not match finalized run' USING ERRCODE='23514'; END IF; RETURN NEW;
END $$;
CREATE TRIGGER fact_guard BEFORE INSERT ON sonda.run_metric_facts FOR EACH ROW EXECUTE FUNCTION sonda.fact_guard();
CREATE FUNCTION sonda.complete_fact() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF EXISTS(SELECT 1 FROM sonda.runs r WHERE (r.team_id,r.session_id,r.id)=(NEW.team_id,NEW.session_id,NEW.id) AND r.result IS NOT NULL AND NOT EXISTS(SELECT 1 FROM sonda.run_metric_facts f WHERE (f.team_id,f.session_id,f.run_id)=(r.team_id,r.session_id,r.id))) THEN RAISE EXCEPTION 'finalized run requires metric fact' USING ERRCODE='23514'; END IF; RETURN NEW;
END $$;
CREATE CONSTRAINT TRIGGER complete_fact AFTER INSERT OR UPDATE ON sonda.runs DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sonda.complete_fact();
CREATE FUNCTION sonda.recovery_guard() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE r sonda.runs; i sonda.incidents; BEGIN
 SELECT * INTO r FROM sonda.runs WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,NEW.run_id);
 SELECT * INTO i FROM sonda.incidents WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,NEW.incident_id);
 IF r.result IS DISTINCT FROM 'Success' OR r.profile_id<>i.profile_id OR r.scope<>i.problem::jsonb->>'scope' OR r.identifier IS DISTINCT FROM i.problem::jsonb->>'identifier' OR i.recovery_policy<>'NextSuccessfulRun' OR EXISTS(SELECT 1 FROM sonda.incident_occurrences o JOIN sonda.runs failed ON (failed.team_id,failed.session_id,failed.id)=(o.team_id,o.session_id,o.run_id) WHERE (o.team_id,o.session_id,o.incident_id)=(NEW.team_id,NEW.session_id,NEW.incident_id) AND failed.cycle_sequence>=r.cycle_sequence) THEN RAISE EXCEPTION 'invalid recovery' USING ERRCODE='23514'; END IF; RETURN NEW;
END $$;
CREATE TRIGGER recovery_guard BEFORE INSERT ON sonda.recoveries FOR EACH ROW EXECUTE FUNCTION sonda.recovery_guard();

CREATE TABLE sonda.run_evidence(team_id text NOT NULL,session_id uuid NOT NULL,run_id text NOT NULL,receipt_id uuid NOT NULL,PRIMARY KEY(team_id,session_id,run_id,receipt_id),FOREIGN KEY(team_id,session_id,run_id) REFERENCES sonda.runs,FOREIGN KEY(team_id,session_id,receipt_id) REFERENCES sonda.normalized_evidence);
CREATE TABLE sonda.occurrence_evidence(team_id text NOT NULL,session_id uuid NOT NULL,occurrence_id text NOT NULL,receipt_id uuid NOT NULL,PRIMARY KEY(team_id,session_id,occurrence_id,receipt_id),FOREIGN KEY(team_id,session_id,occurrence_id) REFERENCES sonda.incident_occurrences,FOREIGN KEY(team_id,session_id,receipt_id) REFERENCES sonda.normalized_evidence);
CREATE TABLE sonda.recovery_evidence(team_id text NOT NULL,session_id uuid NOT NULL,incident_id text NOT NULL,run_id text NOT NULL,receipt_id uuid NOT NULL,PRIMARY KEY(team_id,session_id,incident_id,run_id,receipt_id),FOREIGN KEY(team_id,session_id,incident_id,run_id) REFERENCES sonda.recoveries,FOREIGN KEY(team_id,session_id,receipt_id) REFERENCES sonda.normalized_evidence);
CREATE TABLE sonda.simulation_reports(team_id text NOT NULL,id uuid NOT NULL,profile_id text NOT NULL,version int NOT NULL,request_hash text NOT NULL,profile_hash text NOT NULL,engine_version text NOT NULL,request text NOT NULL,report text NOT NULL,PRIMARY KEY(team_id,id),UNIQUE(team_id,profile_id,version),FOREIGN KEY(team_id,profile_id,version) REFERENCES sonda.profile_versions);
CREATE TABLE sonda.profile_validations(team_id text NOT NULL,profile_id text NOT NULL,version int NOT NULL,draft_revision bigint NOT NULL,profile_hash text NOT NULL,report_id uuid NOT NULL,PRIMARY KEY(team_id,profile_id,version),FOREIGN KEY(team_id,report_id) REFERENCES sonda.simulation_reports);
CREATE TABLE sonda.profile_version_sources(team_id text NOT NULL,profile_id text NOT NULL,version int NOT NULL,source_key text NOT NULL,configuration text NOT NULL,PRIMARY KEY(team_id,profile_id,version,source_key),FOREIGN KEY(team_id,profile_id,version) REFERENCES sonda.profile_versions,FOREIGN KEY(team_id,profile_id,source_key) REFERENCES sonda.log_sources);
CREATE TRIGGER version_guard BEFORE INSERT OR UPDATE OR DELETE ON sonda.profile_version_sources FOR EACH ROW EXECUTE FUNCTION sonda.version_guard();
DO $$ DECLARE n text; BEGIN FOREACH n IN ARRAY ARRAY['run_evidence','occurrence_evidence','recovery_evidence','simulation_reports','profile_validations'] LOOP EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON sonda.%I FOR EACH ROW EXECUTE FUNCTION sonda.immutable()',n); END LOOP; END $$;

CREATE FUNCTION sonda.scope_guard() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE app text; owner_profile text; receipt_profile text; BEGIN
 SELECT application_id INTO app FROM sonda.profiles WHERE (team_id,profile_id)=(NEW.team_id,NEW.profile_id);
 IF app IS DISTINCT FROM NEW.application_id THEN RAISE EXCEPTION 'cross application reference' USING ERRCODE='23514'; END IF;
 IF TG_TABLE_NAME='processing_receipts' THEN
  SELECT profile_id INTO owner_profile FROM sonda.raw_evidence WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,NEW.evidence_id);
  IF owner_profile IS DISTINCT FROM NEW.profile_id THEN RAISE EXCEPTION 'cross profile evidence'; END IF;
 END IF; RETURN NEW;
END $$;
CREATE TRIGGER scope_guard BEFORE INSERT OR UPDATE ON sonda.profile_runtime FOR EACH ROW EXECUTE FUNCTION sonda.scope_guard();
CREATE TRIGGER scope_guard BEFORE INSERT ON sonda.processing_receipts FOR EACH ROW EXECUTE FUNCTION sonda.scope_guard();
CREATE FUNCTION sonda.occurrence_guard() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE i sonda.incidents; r sonda.runs; BEGIN
 SELECT * INTO i FROM sonda.incidents WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,NEW.incident_id);
 SELECT * INTO r FROM sonda.runs WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,NEW.run_id);
 IF i.profile_id IS DISTINCT FROM r.profile_id OR r.scope IS DISTINCT FROM i.problem::jsonb->>'scope' OR r.identifier IS DISTINCT FROM i.problem::jsonb->>'identifier' OR NEW.payload::jsonb->>'runId'<>NEW.run_id OR NEW.payload::jsonb->>'id'<>NEW.id THEN RAISE EXCEPTION 'occurrence identity mismatch' USING ERRCODE='23514'; END IF;
 IF TG_OP='UPDATE' AND (OLD.run_id<>NEW.run_id OR OLD.incident_id<>NEW.incident_id OR OLD.created_receipt<>NEW.created_receipt OR OLD.payload::jsonb->>'detectedAt'<>NEW.payload::jsonb->>'detectedAt') THEN RAISE EXCEPTION 'occurrence origin immutable'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER occurrence_guard BEFORE INSERT OR UPDATE ON sonda.incident_occurrences FOR EACH ROW EXECUTE FUNCTION sonda.occurrence_guard();
CREATE FUNCTION sonda.incident_consistency() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE i sonda.incidents; last_change jsonb; BEGIN
 SELECT * INTO i FROM sonda.incidents WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,NEW.id);
 SELECT payload::jsonb INTO last_change FROM sonda.incident_status_history WHERE (team_id,session_id,incident_id)=(i.team_id,i.session_id,i.id) ORDER BY ordinal DESC LIMIT 1;
 IF last_change->>'to' IS DISTINCT FROM i.status THEN RAISE EXCEPTION 'status requires matching history' USING ERRCODE='23514'; END IF;
 IF NOT EXISTS(SELECT 1 FROM sonda.incident_occurrences WHERE (team_id,session_id,incident_id)=(i.team_id,i.session_id,i.id)) THEN RAISE EXCEPTION 'incident requires occurrence'; END IF;
 IF i.status='Resolved' AND last_change->>'reason'='Later configured successful run' AND NOT EXISTS(SELECT 1 FROM sonda.recoveries WHERE (team_id,session_id,incident_id)=(i.team_id,i.session_id,i.id)) THEN RAISE EXCEPTION 'automatic resolution requires recovery'; END IF;
 RETURN NEW;
END $$;
CREATE CONSTRAINT TRIGGER incident_consistency AFTER INSERT OR UPDATE ON sonda.incidents DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sonda.incident_consistency();
CREATE FUNCTION sonda.sealing_consistency() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE v sonda.profile_versions; BEGIN
 SELECT * INTO v FROM sonda.profile_versions WHERE (team_id,profile_id,version)=(NEW.team_id,NEW.profile_id,NEW.version);
 IF NOT v.sealed THEN RAISE EXCEPTION 'publication must seal version before commit'; END IF;
 IF upper(encode(sha256(convert_to(v.snapshot,'UTF8')),'hex'))<>v.hash OR v.snapshot::jsonb->>'teamId'<>v.team_id OR v.snapshot::jsonb->>'id'<>v.profile_id OR (v.snapshot::jsonb->>'version')::int<>v.version THEN RAISE EXCEPTION 'snapshot identity/hash mismatch'; END IF;
 IF EXISTS(SELECT 1 FROM sonda.profile_rules r WHERE (r.team_id,r.profile_id,r.version)=(v.team_id,v.profile_id,v.version) AND r.payload::jsonb IS DISTINCT FROM v.snapshot::jsonb->'rules'->r.ordinal) THEN RAISE EXCEPTION 'rule payload mismatch'; END IF;
 IF EXISTS(SELECT 1 FROM sonda.profile_patterns p JOIN sonda.profile_rules r ON (r.team_id,r.profile_id,r.version,r.key)=(p.team_id,p.profile_id,p.version,p.rule_key) WHERE (p.team_id,p.profile_id,p.version)=(v.team_id,v.profile_id,v.version) AND p.payload::jsonb IS DISTINCT FROM r.payload::jsonb->'alternatives'->p.ordinal) THEN RAISE EXCEPTION 'pattern mismatch'; END IF;
 IF EXISTS(SELECT 1 FROM sonda.profile_rules r WHERE (r.team_id,r.profile_id,r.version)=(v.team_id,v.profile_id,v.version) AND (SELECT count(*) FROM sonda.profile_patterns p WHERE (p.team_id,p.profile_id,p.version,p.rule_key)=(r.team_id,r.profile_id,r.version,r.key))<>jsonb_array_length(r.payload::jsonb->'alternatives')) THEN RAISE EXCEPTION 'missing pattern'; END IF;
 IF (v.snapshot::jsonb->'identifier') IS DISTINCT FROM COALESCE((SELECT payload::jsonb FROM sonda.profile_identifier_configurations WHERE (team_id,profile_id,version)=(v.team_id,v.profile_id,v.version)),'null'::jsonb) THEN RAISE EXCEPTION 'identifier mismatch'; END IF;
 IF NOT EXISTS(SELECT 1 FROM sonda.profile_validations WHERE (team_id,profile_id,version)=(v.team_id,v.profile_id,v.version)) THEN RAISE EXCEPTION 'validation provenance required'; END IF;
 RETURN NEW;
END $$;
CREATE CONSTRAINT TRIGGER sealing_consistency AFTER INSERT OR UPDATE ON sonda.profile_versions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sonda.sealing_consistency();

CREATE FUNCTION sonda.run_provenance() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE c sonda.processing_receipts; t sonda.processing_receipts; BEGIN
 SELECT * INTO c FROM sonda.processing_receipts WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,NEW.created_receipt);
 IF (c.profile_id,c.version) IS DISTINCT FROM (NEW.profile_id,NEW.version) THEN RAISE EXCEPTION 'run creation version mismatch'; END IF;
 IF (NEW.payload::jsonb->>'profileId', (NEW.payload::jsonb->>'cycleSequence')::int, NEW.payload::jsonb->>'applicationRunId',NEW.payload::jsonb->>'identifier',(NEW.payload::jsonb->>'attemptNumber')::int) IS DISTINCT FROM (NEW.profile_id,NEW.cycle_sequence,NEW.parent_id,NEW.identifier,NEW.attempt) THEN RAISE EXCEPTION 'run identity payload mismatch'; END IF;
 IF NEW.result IS NOT NULL THEN
  SELECT * INTO t FROM sonda.processing_receipts WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,NEW.completed_receipt);
  IF (t.profile_id,t.version) IS DISTINCT FROM (NEW.profile_id,NEW.version) OR NEW.payload::jsonb->>'lifecycle'<>'Finalized' OR NEW.payload::jsonb->>'completedEventAt' IS NULL OR NEW.payload::jsonb->>'completedProcessedAt' IS NULL THEN RAISE EXCEPTION 'run completion mismatch'; END IF;
 ELSE IF NEW.payload::jsonb->>'lifecycle'<>'Running' OR NEW.payload::jsonb->>'completedEventAt' IS NOT NULL OR NEW.payload::jsonb->>'completedProcessedAt' IS NOT NULL THEN RAISE EXCEPTION 'running run cannot have completion'; END IF; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER run_provenance BEFORE INSERT OR UPDATE ON sonda.runs FOR EACH ROW EXECUTE FUNCTION sonda.run_provenance();
CREATE FUNCTION sonda.fact_times() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE r sonda.runs; c sonda.processing_receipts; BEGIN
 SELECT * INTO r FROM sonda.runs WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,NEW.run_id);
 SELECT * INTO c FROM sonda.processing_receipts WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,NEW.receipt_id);
 IF NEW.processed_ticks<>c.processed_ticks OR abs(extract(epoch FROM (NEW.processed_at-c.processed_at)))>0.000001 OR abs(extract(epoch FROM (NEW.event_at-(r.payload::jsonb->>'completedEventAt')::timestamptz)))>0.000001 THEN RAISE EXCEPTION 'completion time mismatch' USING ERRCODE='23514'; END IF;
 IF abs((NEW.event_ticks-621355968000000000)::numeric/10000000-extract(epoch FROM NEW.event_at))>0.000001 OR abs((NEW.processed_ticks-621355968000000000)::numeric/10000000-extract(epoch FROM NEW.processed_at))>0.000001 THEN RAISE EXCEPTION 'tick/time mismatch'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER fact_times BEFORE INSERT ON sonda.run_metric_facts FOR EACH ROW EXECUTE FUNCTION sonda.fact_times();

CREATE TABLE sonda.processing_request_keys(team_id text NOT NULL,session_id uuid NOT NULL,request_id uuid NOT NULL,receipt_id uuid NOT NULL,PRIMARY KEY(team_id,session_id,request_id),FOREIGN KEY(team_id,session_id,receipt_id) REFERENCES sonda.processing_receipts);
CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON sonda.processing_request_keys FOR EACH ROW EXECUTE FUNCTION sonda.immutable();
CREATE FUNCTION sonda.history_chain() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE previous jsonb; BEGIN
 IF NEW.ordinal=0 THEN
  IF NEW.payload::jsonb->>'from' IS NOT NULL OR NEW.payload::jsonb->>'to'<>'Active' THEN RAISE EXCEPTION 'invalid initial status'; END IF;
 ELSE
  SELECT payload::jsonb INTO previous FROM sonda.incident_status_history WHERE (team_id,session_id,incident_id,ordinal)=(NEW.team_id,NEW.session_id,NEW.incident_id,NEW.ordinal-1);
  IF previous IS NULL OR previous->>'to' IS DISTINCT FROM NEW.payload::jsonb->>'from' OR NEW.payload::jsonb->>'from'=NEW.payload::jsonb->>'to' THEN RAISE EXCEPTION 'invalid status chain'; END IF;
 END IF; RETURN NEW;
END $$;
CREATE TRIGGER history_chain BEFORE INSERT ON sonda.incident_status_history FOR EACH ROW EXECUTE FUNCTION sonda.history_chain();
ALTER TABLE sonda.processing_receipts ADD COLUMN input_context text NOT NULL;

CREATE FUNCTION sonda.incident_identity_guard() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE exact_key text; p jsonb; app text; BEGIN
 SELECT key INTO exact_key FROM sonda.problem_identities WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,NEW.problem_id);
 p=NEW.problem::jsonb;
 SELECT application_id INTO app FROM sonda.profiles WHERE (team_id,profile_id)=(NEW.team_id,NEW.profile_id);
 IF left(exact_key,11)<>'problem:v1:' OR substring(exact_key from 12)::jsonb IS DISTINCT FROM jsonb_build_array(NEW.team_id,app,NEW.profile_id,p->>'scope',p->>'streamKey',p->>'identifierNamespace',p->>'identifier',p->>'conditionKey') OR p->>'teamId'<>NEW.team_id OR p->>'profileId'<>NEW.profile_id OR p->>'applicationId'<>app THEN RAISE EXCEPTION 'incident problem identity mismatch'; END IF;
 IF TG_OP='UPDATE' AND ((OLD.problem_id,OLD.problem,OLD.profile_id,OLD.recovery_policy,OLD.created_receipt) IS DISTINCT FROM (NEW.problem_id,NEW.problem,NEW.profile_id,NEW.recovery_policy,NEW.created_receipt) OR (OLD.severity='Error' AND NEW.severity<>'Error')) THEN RAISE EXCEPTION 'incident identity/severity invariant'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER incident_identity_guard BEFORE INSERT OR UPDATE ON sonda.incidents FOR EACH ROW EXECUTE FUNCTION sonda.incident_identity_guard();
CREATE FUNCTION sonda.evidence_link_guard() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE run_key text; run_profile text; evidence_profile text; BEGIN
 IF TG_TABLE_NAME='occurrence_evidence' THEN SELECT run_id INTO run_key FROM sonda.incident_occurrences WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,NEW.occurrence_id); ELSE run_key=NEW.run_id; END IF;
 SELECT profile_id INTO run_profile FROM sonda.runs WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,run_key);
 SELECT profile_id INTO evidence_profile FROM sonda.processing_receipts WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,NEW.receipt_id);
 IF run_profile IS DISTINCT FROM evidence_profile THEN RAISE EXCEPTION 'cross profile evidence link'; END IF; RETURN NEW;
END $$;
DO $$ DECLARE n text; BEGIN FOREACH n IN ARRAY ARRAY['run_evidence','occurrence_evidence','recovery_evidence'] LOOP EXECUTE format('CREATE TRIGGER evidence_link_guard BEFORE INSERT ON sonda.%I FOR EACH ROW EXECUTE FUNCTION sonda.evidence_link_guard()',n); END LOOP; END $$;
