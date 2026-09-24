# Phase 2 proposed transaction and adapter contract

Status: **implemented Phase 2 design, stopped for user review**, part of [Phase 2 scope](phase-2-scope.md). Open-run activation rejection is temporary for Phase 2, not a permanent SONDA business rule. See the [schema](phase-2-schema.md) for keys and constraints.

## One input, one atomic interpretation

The input unit is one supplied Phase 1-style entry, not a whole file or simulation batch. It can complete multiple runs: a parent End may finalize several unfinished orders Undefined and create several incidents/facts. All effects belong to the same transaction. The harness supplies an ordered source identity, request ID, Profile, raw payload, parser date context, explicit processing clock, and expected input sequence. Its fingerprint covers all semantic inputs, including clock/context and payload, but excludes the transport request ID. Version selection is recorded separately on first interpretation and reused on replay rather than recomputed from the current active pointer. Identical text at a new source position remains new evidence.

Use an explicit READ COMMITTED PostgreSQL transaction and `SELECT ... FOR UPDATE` on `(team,session,application)` runtime. All writers of engine state, activation and incident status use that lock first; then Profile/Incident locks in a consistent ID order. This coarse application serialization is deliberate for correctness and can be measured later. No external queue or distributed lock service.

1. Open fresh scoped DbContext/transaction; obtain application runtime lock. Scope every query by team and session.
2. Look up request receipt and evidence identity. A committed same fingerprint returns the stored result immediately. A conflicting fingerprint fails visibly. Another request ID for the same evidence returns the existing interpretation only if its semantic input matches; it cannot select a newer version or a new processing date.
3. For a genuinely new input, verify expected sequence, monotonic supplied processing time, source/Profile ownership and unblocked lane. Resolve the active immutable version under this lock. Validate that its engine format is supported. Reserve stable line number and deterministic ID counter values in transaction-local state.
4. Load/restore interpreter state from durable rows and counters. Create an isolated working instance; do not mutate a singleton interpreter or a cache that survives rollback. Run the existing parser/matcher/interpreter through shared Application orchestration, preserving preflight diagnostics.
5. Compute a typed change set by comparing exported before/after state: new/enriched runs, evidence links, new Incidents/Occurrences, same-run occurrence enrichment, recoveries/history, finalized metric facts, counters and blocked state. Never infer changes from human-readable action messages.
6. Insert evidence, receipt and interpretation provenance; apply changes, matching facts and state checkpoint in this transaction. Deferred constraints validate complete aggregate relationships at commit. EF `SaveChanges` is not the durability boundary; explicit transaction COMMIT is.
7. Commit. Only then report Applied/Rejected as durable and publish any in-memory cached snapshot. Return stored receipt/result and revision. Record acknowledgment/actual commit observation separately as described below; failure of that optional audit enrichment cannot invalidate or replay a committed business transaction.

Parsing/interpretation rejection is a **committed diagnostic outcome**, distinct from infrastructure failure: persist raw evidence, diagnostic and receipt, set the Profile lane blocked where Phase 1 does, advance the supplied input receipt sequence, and leave business state unchanged. Later input to the blocked lane is rejected consistently; other Profile lanes can proceed according to their ordered inputs. No new live skip/unblock policy is added. Infrastructure/constraint failures roll back everything including the checkpoint and are not persisted as successful domain rejection.

EF supports explicit transactions and savepoints, but retries here must rerun the complete locked unit on a fresh state/context; never retry just its last `SaveChanges`. [EF Core transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions).

## Idempotency and crash boundaries

The durable fact is identified by `(team,session,evidence identity)` and bound to its first successful interpretation version. The request key provides retry correlation; it is not the only deduplication key. A different request ID must not allow the same evidence to acquire another completion fact. Reinterpretation under changed rules requires a separate explicitly isolated simulation session, never an overwrite.

| Boundary | Restart/retry behavior |
| --- | --- |
| Before transaction or during interpretation | No committed receipt; reload old state, use same input/fingerprint; no caller-visible success |
| After inserts or SaveChanges, before COMMIT | PostgreSQL rollback removes every effect and counter advance; discard working engine |
| COMMIT definitely fails | Fresh transaction/context; same request, evidence and proposed processing timestamp; retry only bounded transient infrastructure failures |
| Connection fails during COMMIT / acknowledgment lost | Outcome is unknown, not definitely failed. Reconnect and query receipt under the same lock. Existing receipt returns its result; absent receipt after the old transaction resolves permits whole-unit retry |
| COMMIT succeeds, process dies before responding | Receipt is authoritative; repeated request returns exact result, no second recovery/history/fact |
| Projection/audit enrichment fails after COMMIT | Canonical effects remain committed; repair projection/observation separately, never rerun business interpretation to repair a cache |

Request processing time is part of the supplied immutable command, as in Phase 1; a retry never substitutes the current clock. Thus an input proposed before midnight and committed after midnight retains its explicitly recorded processing date, while its commit/acknowledgment date can differ. A production acquisition dispatcher that durably assigns clocks is later work; this phase does not claim durable delivery for an input never successfully committed and then lost by its caller.

Use exact uniqueness independently for receipts, run cycles, order attempts, unresolved problem episodes, incident/run occurrences, incident/successful-run recoveries, history origins and one metric fact per run. An `ON CONFLICT DO NOTHING` is not a universal success path: compare existing semantic content and reject mismatches. Repeated recovery processing returns the receipt, or encounters an already resolved incident; it does not add another history row.

## Distinct timestamp concepts

| Field | Meaning and use |
| --- | --- |
| `source_timestamp_text`, source offset/zone, `source_event_at?` | Original timestamp representation and resolvable source instant; preserve date-less text/date context. Missing source timestamps remain explicitly missing here |
| `normalized_event_at` / UTC ticks / quality | Parser-resolved instant passed as Phase 1 `ParsedEntry.EventAt`; explicit date/timezone resolution or existing flagged fallback. Event normalization is not the wall clock when normalization executed. May equal source instant, but provenance is separate |
| `processing_at` / UTC ticks | Supplied server/harness time for the interpreted input; determines first completion-processing time and Logs Today date; unchanged on retry |
| `completed_processed_at` / UTC ticks | Run-specific first-finalization processing time, copied from terminal input only when the run completes; earlier input processing, replay and later recovery cannot change it |
| `database_commit_at` | Actual PostgreSQL transaction commit instant, **nullable when unavailable**; audit only, never a replacement processing time |
| `event_reporting_date`, `processed_reporting_date`, context ID | Server-local dates calculated from the respective instants using explicit stored reporting timezone rules; neither is the database session's implicit date |

Also store `db_recorded_at = clock_timestamp()` for insertion observation and `commit_acknowledged_at` for the client's receipt of successful COMMIT. These are separately named values. PostgreSQL `CURRENT_TIMESTAMP`/`now()` denotes transaction start and `clock_timestamp()` denotes call time; neither is actual commit time. [PostgreSQL time functions](https://www.postgresql.org/docs/18/functions-datetime.html).

An exact commit instant cannot be inserted by the same transaction after it has committed. Proposed Phase 2 uses nullable `database_commit_at` with explicit availability. If `track_commit_timestamp` is enabled in the disposable database, a post-commit observer queries `pg_xact_commit_timestamp` for the saved transaction identity and stores the result in a separate transaction. Test both enabled and disabled configurations. Transaction IDs are contextual metadata, not permanent business identities; retain server/database identity and guard unavailable/aged transaction metadata. Do not require this optional database feature for correctness or fabricate a timestamp when an observation was lost. PostgreSQL documents that commit information is available only when tracking was enabled for those transactions. [PostgreSQL commit information](https://www.postgresql.org/docs/18/functions-info.html).

If guaranteed actual commit-time audit for every transaction becomes a requirement, that needs a separately approved durable capture/retention design. Phase 2 guarantees atomic facts and honest time semantics, not guaranteed retrospective recovery of every database commit instant.

## Proposed persistence interfaces

Illustrative contracts below are documentation, not source implementation. All operations carry a `ProcessingScope` containing team/session/application. No raw DbContext, EF entity or database connection crosses into Domain.

```csharp
public interface IProcessingStore
{
    Task<IProcessingTransaction> BeginLockedAsync(
        ProcessingScope scope, CancellationToken cancellationToken);
}

public interface IProcessingTransaction : IAsyncDisposable
{
    Task<CommittedInput?> FindCommittedAsync(InputIdentity identity,
        CancellationToken cancellationToken);
    Task<ProcessingState> LoadStateAsync(ProfileIdentity profile,
        CancellationToken cancellationToken);
    Task StageAsync(ProcessingRequest request, ProcessingChangeSet changes,
        CancellationToken cancellationToken);
    Task<CommitReceipt> CommitAsync(CancellationToken cancellationToken);
    // Dispose without successful commit rolls back. Commit can report an unknown outcome.
}

public interface IConfigurationStore
{
    Task<PublishedVersion> PublishAsync(PublishVersionCommand command,
        CancellationToken cancellationToken);
    Task<ActivationReceipt> ActivateAsync(ActivateVersionCommand command,
        CancellationToken cancellationToken);
}

public interface IReadModelStore
{
    Task<ApplicationState> ReadApplicationAsync(ProcessingScope scope,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<RunMetricFact>> ReadMetricFactsAsync(
        MetricQuery query, CancellationToken cancellationToken);
}
```

`ProcessingState` includes immutable Profile copy, engine state schema/version, open cycle/order associations, observed flags, runs and incident state/evidence required by the engine, allocator/cycle counters, sequence watermark, reporting context and diagnostics/block state. Initially load full relevant lane history for faithful restore, with a documented finite fixture scope; optimization/pruning requires parity evidence later. The engine's successful recovery compares occurrence cycle sequences, so restoring only open orders is insufficient.

The shared Application processor owns parse/match/apply/change-set behavior. The persistence store owns atomic storage/locking, not business decisions. In-memory simulation supplies equivalent state lifecycle through memory adapters, preserving deterministic seeded IDs and outputs. A Domain restore method validates invariants and rejects an incompatible checkpoint; it does not invoke a constructor then overwrite private fields through reflection.

Minimal typed changes include `RunCreated`, `RunChanged`, `RunFinalized`, `OccurrenceCreated`, `OccurrenceEnriched`, `IncidentChanged`, `RecoveryRecorded`, `StatusHistoryAppended`, `EvidenceLinked`, `MetricFactAdded`, and `RuntimeAdvanced`, with stable IDs and origin receipt. They can be transient change-set records; do not introduce event-sourcing infrastructure or an external event bus. Durable normalized facts plus interpretation provenance are sufficient.

## Immutable versions and safe activation

### Profile lifecycle and administrative concurrency

| Stage | Durable meaning and transition guard |
| --- | --- |
| Draft | Editable content with draft revision/hash; editing increments revision and invalidates prior validation applicability |
| Validated | Immutable validation record for exact draft revision/hash, engine/validator version and complete simulator provenance; not a boolean reusable after editing |
| Published | Frozen version number/UUID and exact snapshot from matching validated content; draft may evolve independently |
| Activated | Runtime lane points to published version through activation history at an effective future input sequence and safe boundary |
| Superseded | Later activation replaces the pointer; old activation/version remains immutable and queryable. Derived from activation history, not mutation of the old snapshot |

Administrative edit commands carry command ID and expected entity/draft revision. In one transaction, check duplicate receipt first, compare revision, validate, update content/revision and record receipt. Conflict returns current revision without overwriting another edit. Publication compares validated draft revision/hash under lock; competing publications cannot allocate the same version number. Log Source edits compare source revision; interpretation-relevant changes require publication and cannot mutate version settings.

Activation requires expected activation revision and expected current version ID. Under the application lock, recheck both and boundary conditions, append history, increment activation revision and record the command receipt atomically. Competing admins targeting the same prior activation cannot both silently succeed; stale commands conflict. Retry of a successful command returns its original receipt. No administrative website or account permissions are implemented here.

Example: CAM v7 runs, normalized evidence, Occurrence origins and metric facts retain v7 provenance. Publishing v8 does not activate it. Activating v8 at the next safe sequence affects new inputs only; v7 becomes superseded for that lane. Replaying a v7 receipt returns its original result while v8 is active. Explicit historical reprocessing remains a separate deferred feature.

### Publication and activation boundaries

Publication and activation are different commands. Publication freezes the exact tested snapshot; activation adds history and changes the lane's active pointer under the same application lock used by processing. Publication requires a complete applicable simulator report with matching draft/sample/request hashes and supported engine version; editing a draft invalidates that eligibility. Store supported timing requests/diagnostics as-is; do not allow an incomplete/unsupported simulation to masquerade as validated production configuration.

Temporary Phase 2 safe-boundary restriction (not permanent business policy): activate only with **no open Application/Order Run** for that Profile lane. If a run remains open, reject activation with a precise reason; no forced closure or guessed migration. Effective sequence is the next unprocessed input sequence. Inputs already committed retain their version on replay, and historical runs keep the version FK forever. There is no mid-cycle switch and no retroactive retagging of queued/committed interpretation.

Cycle and ID counters continue across compatible activation so later-cycle recovery remains meaningful. Require unchanged team/application/Profile, stream and identifier namespace. While unresolved incidents exist, reject changes to identity extraction, condition identities or recovery semantics unless proved compatible with the old snapshot; Phase 2 conservatively requires these fields unchanged. Unresolved incidents retain their original policy/evidence, and new diagnostics never merge by similar text. More permissive cross-version recovery/stream migration is a Phase 3 decision, not an adapter shortcut.

## Incident revisions and later manual commands

Automatic engine changes update incident revision/history in the interpretation transaction. The future manual command contract supplies command UUID and expected incident revision. Lock application, check existing command receipt/payload, compare expected revision, apply the approved transition, append history, increment revision and insert command receipt in one transaction. Stale revision returns conflict/current revision with no changes; retry of the same already-applied command returns its receipt before checking the now-stale revision. Conflicting reuse of command UUID is rejected.

Phase 2 tests the atomic compare-and-swap and idempotency primitives using controlled test commands. It does not expose manual status endpoints, grant permissions, implement authentication, reopen incidents, or decide the pending manual-transition policy. Source actors are System/Harness with nullable future actor reference; never create fictional users to satisfy a foreign key.

## Reconstruction and reproducibility

Restore runtime from normalized runs, occurrences, recoveries, history and durable counters. A serialized engine checkpoint is an optional acceleration with schema version and receipt watermark; if missing, rebuild. If its hash/watermark disagrees, fail visibly or rebuild from canonical rows, never prefer stale cache. Replaying all original evidence through a changed engine is not a silent substitute for restoring historical facts.

Read current application state from a coherent database snapshot of all its lanes: highest unresolved severity, or Stable after at least one finalized run, otherwise null. Historical health and order volume read immutable `run_metric_facts` and call the same pure metric logic. Original timestamps, clock quality, reporting contexts and Profile snapshots make the result explainable. A read across multiple tables uses a short REPEATABLE READ transaction; locks for writes remain READ COMMITTED as specified above.

Reprojection is intentionally different from reinterpretation: it recomputes counts/dates from immutable facts, not log rules. A simulation/reinterpretation session is isolated and cannot write facts into another session's totals. Neither recovery nor manual workflow changes historical success/failure contributions.
