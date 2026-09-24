-- Preserve the original evidence contract while adding non-evidence commands.
ALTER TABLE sonda.processing_receipts ADD CONSTRAINT receipt_kind_evidence CHECK (
 (kind IN ('Evidence','PolicyEvidence') AND evidence_id IS NOT NULL) OR
 (kind IN ('PolicyAdvanceTime','PolicyChangeStatus','PolicyActivateVersion','PolicySourceObservation') AND evidence_id IS NULL));
CREATE UNIQUE INDEX policy_observation_identity ON sonda.incident_occurrences(team_id,session_id,incident_id,created_receipt) WHERE run_id IS NULL;
CREATE UNIQUE INDEX policy_episode_identity ON sonda.incidents(team_id,session_id,problem_id,((policy_context::jsonb->>'episode')::int)) WHERE policy_context IS NOT NULL;
CREATE UNIQUE INDEX policy_cycle_correlation ON sonda.runs(team_id,session_id,profile_id,((payload::jsonb->'policyContext'->>'correlationKey'))) WHERE scope='Application' AND payload::jsonb->'policyContext'->>'correlationKey' IS NOT NULL;

-- Amend narrow legacy assumptions without removing its integrity checks.
DO $$ DECLARE definition text; BEGIN
 definition=pg_get_functiondef('sonda.scope_guard()'::regprocedure);
 definition=replace(definition,'IF TG_TABLE_NAME=''processing_receipts'' THEN','IF TG_TABLE_NAME=''processing_receipts'' THEN IF NEW.evidence_id IS NULL THEN RETURN NEW; END IF;');
 EXECUTE definition;
 definition=pg_get_functiondef('sonda.run_provenance()'::regprocedure);
 definition=replace(definition,'(t.profile_id,t.version) IS DISTINCT FROM (NEW.profile_id,NEW.version)',
 '(t.profile_id IS DISTINCT FROM NEW.profile_id OR (t.version IS DISTINCT FROM NEW.version AND t.kind NOT IN (''PolicyAdvanceTime'',''PolicyEvidence'')))');
 EXECUTE definition;
 definition=pg_get_functiondef('sonda.occurrence_guard()'::regprocedure);
 definition=replace(definition,'SELECT * INTO r FROM sonda.runs',
 'IF NEW.run_id IS NULL THEN
 IF i.policy_context IS NULL OR NEW.payload::jsonb->>''id'' IS DISTINCT FROM NEW.id OR NOT EXISTS(SELECT 1 FROM sonda.processing_receipts c WHERE (c.team_id,c.session_id,c.id)=(NEW.team_id,NEW.session_id,NEW.created_receipt) AND c.profile_id=i.profile_id AND c.kind=''PolicyEvidence'' AND c.line=(NEW.payload::jsonb->>''evidenceLine'')::int) THEN RAISE EXCEPTION ''invalid observation occurrence''; END IF;
 IF TG_OP=''UPDATE'' THEN RAISE EXCEPTION ''observation occurrence immutable''; END IF;
 RETURN NEW; END IF;
 SELECT * INTO r FROM sonda.runs');
 EXECUTE definition;
END $$;

CREATE OR REPLACE FUNCTION sonda.recovery_guard() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE r sonda.runs; i sonda.incidents; BEGIN
 SELECT * INTO r FROM sonda.runs WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,NEW.run_id);
 SELECT * INTO i FROM sonda.incidents WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,NEW.incident_id);
 IF r.result IS DISTINCT FROM 'Success' OR r.profile_id<>i.profile_id OR r.scope<>i.problem::jsonb->>'scope' OR r.identifier IS DISTINCT FROM i.problem::jsonb->>'identifier' OR i.recovery_policy<>'NextSuccessfulRun' THEN RAISE EXCEPTION 'invalid recovery' USING ERRCODE='23514'; END IF;
 IF i.policy_context IS NULL THEN
  IF EXISTS(SELECT 1 FROM sonda.incident_occurrences o JOIN sonda.runs failed ON (failed.team_id,failed.session_id,failed.id)=(o.team_id,o.session_id,o.run_id) WHERE (o.team_id,o.session_id,o.incident_id)=(NEW.team_id,NEW.session_id,NEW.incident_id) AND failed.cycle_sequence>=r.cycle_sequence) THEN RAISE EXCEPTION 'invalid recovery' USING ERRCODE='23514'; END IF;
 ELSE
  IF EXISTS(SELECT 1 FROM sonda.recoveries WHERE (team_id,session_id,incident_id)=(NEW.team_id,NEW.session_id,NEW.incident_id)) THEN RAISE EXCEPTION 'one successful confirmation per episode' USING ERRCODE='23505'; END IF;
  IF r.payload::jsonb->'policyContext'->>'recoveryCompatibility' IS DISTINCT FROM i.policy_context::jsonb->>'recoveryCompatibility' THEN RAISE EXCEPTION 'incompatible recovery contract'; END IF;
  IF EXISTS(SELECT 1 FROM sonda.incident_occurrences o LEFT JOIN sonda.runs failed ON (failed.team_id,failed.session_id,failed.id)=(o.team_id,o.session_id,o.run_id) WHERE (o.team_id,o.session_id,o.incident_id)=(NEW.team_id,NEW.session_id,NEW.incident_id) AND
   ((o.run_id IS NOT NULL AND (failed.result IS NULL OR (failed.payload::jsonb->'policyContext'->>'completedSequence')::bigint >= (r.payload::jsonb->'policyContext'->>'startedSequence')::bigint OR (failed.payload::jsonb->>'completedEventAt')::timestamptz > (r.payload::jsonb->>'startedEventAt')::timestamptz)) OR
    (o.run_id IS NULL AND ((o.payload::jsonb->>'evidenceLine')::bigint >= (r.payload::jsonb->'policyContext'->>'startedSequence')::bigint OR (o.payload::jsonb->>'eventAt')::timestamptz > (r.payload::jsonb->>'startedEventAt')::timestamptz)))) THEN RAISE EXCEPTION 'recovery must be causally later'; END IF;
 END IF;
 RETURN NEW;
END $$;

CREATE FUNCTION sonda.policy_run_guard() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE v jsonb; BEGIN
 IF TG_OP='UPDATE' AND OLD.payload::jsonb->'policyContext' IS NOT NULL AND OLD.payload::jsonb->'policyContext'<>'null'::jsonb AND (NEW.payload::jsonb->'policyContext' IS NULL OR NEW.payload::jsonb->'policyContext'='null'::jsonb) THEN RAISE EXCEPTION 'pinned policy context cannot be removed'; END IF;
 IF NEW.payload::jsonb->'policyContext' IS NOT NULL AND NEW.payload::jsonb->'policyContext'<>'null'::jsonb THEN
  SELECT snapshot::jsonb INTO v FROM sonda.profile_versions WHERE (team_id,profile_id,version)=(NEW.team_id,NEW.profile_id,NEW.version);
  IF v->'policy'->>'revision' IS DISTINCT FROM '2' OR (NEW.payload::jsonb->'policyContext'->>'profileVersion')::int IS DISTINCT FROM NEW.version THEN RAISE EXCEPTION 'invalid pinned policy context'; END IF;
  IF TG_OP='UPDATE' AND ((OLD.payload::jsonb->'policyContext')-'completedSequence'-'completionTimeKind') IS DISTINCT FROM ((NEW.payload::jsonb->'policyContext')-'completedSequence'-'completionTimeKind') THEN RAISE EXCEPTION 'pinned run context immutable'; END IF;
 END IF; RETURN NEW;
END $$;
CREATE TRIGGER policy_run_guard BEFORE INSERT OR UPDATE ON sonda.runs FOR EACH ROW EXECUTE FUNCTION sonda.policy_run_guard();

CREATE FUNCTION sonda.policy_incident_guard() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE v jsonb; BEGIN
 IF TG_OP='UPDATE' AND OLD.policy_context IS NOT NULL AND NEW.policy_context IS NULL THEN RAISE EXCEPTION 'incident policy context cannot be removed'; END IF;
 IF NEW.policy_context IS NULL THEN RETURN NEW; END IF;
 SELECT snapshot::jsonb INTO v FROM sonda.profile_versions WHERE (team_id,profile_id,version)=(NEW.team_id,NEW.profile_id,(NEW.policy_context::jsonb->>'profileVersion')::int);
 IF v->'policy'->>'revision' IS DISTINCT FROM '2' OR (NEW.policy_context::jsonb->>'episode')::int<1 OR (NEW.policy_context::jsonb->>'revision')::bigint<0 OR NEW.policy_context::jsonb->>'recoveryCompatibility' IS DISTINCT FROM v->'policy'->>'recoveryCompatibility' THEN RAISE EXCEPTION 'invalid incident policy context'; END IF;
 IF TG_OP='UPDATE' AND OLD.policy_context IS NOT NULL THEN
  IF (OLD.policy_context::jsonb->>'episode',OLD.policy_context::jsonb->>'profileVersion',OLD.policy_context::jsonb->>'recoveryCompatibility') IS DISTINCT FROM (NEW.policy_context::jsonb->>'episode',NEW.policy_context::jsonb->>'profileVersion',NEW.policy_context::jsonb->>'recoveryCompatibility') OR (NEW.policy_context::jsonb->>'revision')::bigint<(OLD.policy_context::jsonb->>'revision')::bigint THEN RAISE EXCEPTION 'incident recovery identity immutable'; END IF;
 END IF; RETURN NEW;
END $$;
CREATE TRIGGER policy_incident_guard BEFORE INSERT OR UPDATE ON sonda.incidents FOR EACH ROW EXECUTE FUNCTION sonda.policy_incident_guard();
CREATE FUNCTION sonda.policy_occurrence_subject_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF TG_OP='UPDATE' AND (NEW.run_id,NEW.incident_id,NEW.created_receipt) IS DISTINCT FROM (OLD.run_id,OLD.incident_id,OLD.created_receipt) THEN RAISE EXCEPTION 'occurrence subject immutable'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER policy_occurrence_subject_guard BEFORE UPDATE ON sonda.incident_occurrences FOR EACH ROW EXECUTE FUNCTION sonda.policy_occurrence_subject_guard();
CREATE FUNCTION sonda.policy_observation_consistency() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE context jsonb; actual jsonb; BEGIN
 SELECT policy_context::jsonb INTO context FROM sonda.incidents WHERE (team_id,session_id,id)=(NEW.team_id,NEW.session_id,NEW.id);
 IF context IS NULL THEN RETURN NEW; END IF;
 SELECT COALESCE(jsonb_agg(payload::jsonb ORDER BY (payload::jsonb->>'evidenceLine')::int),'[]'::jsonb) INTO actual FROM sonda.incident_occurrences WHERE (team_id,session_id,incident_id)=(NEW.team_id,NEW.session_id,NEW.id) AND run_id IS NULL;
 IF actual IS DISTINCT FROM context->'observations' THEN RAISE EXCEPTION 'observation summary must match evidence occurrences'; END IF; RETURN NEW;
END $$;
CREATE CONSTRAINT TRIGGER policy_observation_consistency AFTER INSERT OR UPDATE ON sonda.incidents DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sonda.policy_observation_consistency();
