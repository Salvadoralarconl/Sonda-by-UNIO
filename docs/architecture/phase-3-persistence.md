# Phase 3 proposed domain and persistence changes

Status: **approved design; implementation record below takes precedence for concrete storage names**. Parent: [scope and policy decisions](phase-3-scope.md). The original design alternatives are preserved below; the following record specifies their implemented representation. Preserve the same modular monolith and accepted PostgreSQL adapter.

## Domain/Application changes

### Implementation record

The companion port is `IPolicyProcessingStore` (`ExecuteAsync` / `ReadAsync`). `PolicySession` dispatches evidence, explicit clock/frontier, workflow, activation and source-observation commands into the existing `ProfileInterpreter`. `PostgresPolicyStore` implements that port using the original persistence helpers and application lock. LegacyV1 continues through the original adapter. There is no second interpretation engine or new service.

The additive migration is `20260924032911_InterpretationPolicies`, following unchanged `20260924023552_DurablePersistence`. Its concrete storage representation deliberately reuses existing constrained facts instead of duplicating each into another extension table:

- Policy configuration lives in the sealed version snapshot and retains its snapshot hash. Missing `policy` is LegacyV1; revision 2 is explicit. Publication reports identify `phase3.1`; old reports retain `phase1.1`.
- `runs.payload.policyContext` holds the typed pinned context, predecessor, correlation/epoch, recovery contract, deadline anchor/due/overdue and completion kind/sequence. Dedicated SQL guards enforce pinned-version consistency and immutable start context. Existing relational parent/attempt keys remain authoritative.
- `processing_receipts.kind` distinguishes legacy Evidence and five Policy command kinds; `evidence_id` is nullable only for non-evidence commands. Immutable command/trace JSON preserves exact offsets/ticks and selected version. `processing_request_keys` retains request aliases. No fake raw evidence is inserted for timers or workflow.
- `policy_runtime` is a per-lane frontier header. Exact frontier values, rejected dispositions and blocked partition identities reconstruct from committed receipts. Pending deadlines derive from the pinned context of open runs; finalized runs are no longer pending. Thus there is no independent mutable deadline queue/table to become inconsistent. One AdvanceTime transaction finalizes the deterministic due set and its contributions atomically.
- `incidents.policy_context` records episode, pinned recovery identity, logical workflow revision, resolution origin/actor/reason and observation summary. SQL protects identity and episode uniqueness. Database row revision remains a separate storage-concurrency value.
- `incident_occurrences.run_id` may be null only for an evidence observation with a valid PolicyEvidence receipt. Subject and creation origin are immutable. Reconstruction reads normalized occurrence rows; a deferred constraint checks the summary against them. Run occurrences retain the original unique `(incident,run)` key; observations have unique `(incident,created_receipt)`.
- Source observations are immutable typed command facts in receipts; availability is derived at explicit as-of time. Status audit and recovery use the existing relational tables plus typed payload metadata. One successful confirmation per revision-2 episode is enforced.
- Metric facts stay one per completed run. For synthetic closure, the existing `event_at`/event ticks carry **effective deadline completion**, identified by `completionTimeKind` on the referenced run. They are not a raw source timestamp: the origin receipt has no evidence/normalized-source row. Completion-processing instant, reporting dates, optional actual COMMIT observation and acknowledgment remain distinct.

The original table of proposed storage alternatives below is retained for design provenance; it does not claim separate extension, deadline, availability or command tables were all necessary. The implemented constraints, receipt origin and exact typed context provide those identities without a duplicated authority.

`PostgresConfigurationStore.ActivateAsync` remains the LegacyV1/drained upgrade adapter. Revision-2 activations go through `PolicySession`, including compatible open-run routing and expected activation revision. Downgrading a revision-2 session to LegacyV1 is rejected. No historical reinterpretation occurs. Profile publication still accepts the existing line-simulation contract; the command simulator provides the richer policy/workflow test and report workflow. Production profile-authoring UI and enforcement of a team-specific sample-coverage policy are deferred.

| Area | Bounded change |
| --- | --- |
| Profile contracts | Optional versioned policy block; timing clock/thresholds, Undefined Application severity, serial/correlated mode, stable routing contract, late/out-of-cycle diagnostic eligibility, availability thresholds and recovery compatibility |
| Run state | Pinned policy/Profile/routing revisions, exact cycle correlation/epoch, attempt ordinal/previous attempt, observed structural outcomes, deadline anchor/effective completion kind |
| Interpretation | Per-parent state partitions in the existing interpreter; select partition/version before applying the same parser/matcher/state machine; preserve legacy path/defaults |
| Problem/Incident | Episode ordinal, immutable original recovery contract, occurrence subject discriminant, manual resolution metadata, first successful recovery confirmation |
| Input/event contracts | Discriminated Evidence, AdvanceTime, ChangeIncidentStatus, ActivateVersion and SourceObservation commands with stable IDs, immutable payload fingerprint, effective and processing clocks |
| Diagnostic disposition | Separate record for late/unmatched/conflicting evidence; link a target only when routing proves it; no fabricated run |
| Projections | Reconstruct unresolved business health, independent availability, per-run completion metrics and audit history from durable facts |
| Simulator | Ordered scripted commands/frontiers in addition to legacy line-only inputs, explicit fake clock and routing/policy provenance, no wall-clock scheduler |

Proposed Application interfaces: extend `IProcessingStore` with a versioned command envelope or companion `IInterpretationCommandStore.ExecuteAsync`; use `IIncidentWorkflow.ChangeStatusAsync` for validated workflow commands; extend configuration activation to return drain-required/activated with routing provenance; add `IAvailabilityReader.ReadAsync(asOf)` and a simulation-only source-observation command. Avoid exposing EF entities through these ports. Legacy ProcessAsync remains an adapter to legacy evidence commands, not a separate business engine.

The internal Phase 2 `IncidentRevisionStore` is not enough by itself: the new Application workflow must validate transitions and call the shared domain policy before transactional compare-and-swap. Phase 3 still supplies no authenticated service endpoint.

## Proposed forward migration 002

Do not edit the delivered initial migration or immutable Profile snapshot bytes. Introduce a new migration, with an upgrade fixture seeded by the actual Phase 2 migration and engine. Exact SQL/EF implementation will be reviewed during the approved implementation, not supplied as already-tested migration here.

| Storage area | Proposed additive data/constraints |
| --- | --- |
| Version policy/routing | Version-keyed extension rows for policy revision and routing/recovery contract fingerprints; missing extension means LegacyV1. Keep original snapshot hash independently verified. Seal extension together with a new version; prohibit post-publication edits |
| Run context | One-to-one immutable-at-start run context: pinned version, correlation value/epoch, attempt ordinal, predecessor, recovery compatibility. Preserve existing parent/version FKs. Unique correlated parent `(team, session, Profile, stream, correlation epoch/key)`; unique order `(parent, exact identifier, attempt)` |
| Routing dispositions | One immutable disposition per evidence command; Profile/routing/version/target and decision recorded. New interpretation receipt can reference the existing raw evidence identity without recapture. Support pinned old/new versions in one lane; do not retain the assumption that lane active version interprets every input |
| Command receipts | General processing-command table for evidence, clock, workflow and source observations; unique `(team, session, commandId)`, fingerprint and result. Link existing receipt IDs instead of rewriting old receipts. Evidence source identity uniqueness remains authoritative across request aliases |
| Deadlines | Unique `(run, deadlineKind, policyRevision)` pending deadline records plus idempotent evaluation/disposition; anchor/effectiveDueAt, explicit clock kind and frontier. Completion/cancellation atomic with run changes. Never persist a clock command as a fake raw log line |
| Completion provenance | Typed relation to exactly one evidence receipt or clock command; effective timestamp/kind and processing time. Preserve historical completion fields/metrics. Synthetic deadline closures have no source event timestamp |
| Observation occurrences | Generalize occurrence subject to `Run` or `EvidenceObservation`: exactly one run FK or observation-command FK, scoped to same team/Profile. New uniqueness for evidence occurrences `(incident, observationId)`; existing `(incident, run)` uniqueness retained for run occurrences |
| Episodes and recovery | Episode ordinal unique per exact Problem Identity; at most one unresolved episode. Recovery contract/version pinned at creation. Add resolution-kind/audit extensions and one first successful recovery confirmation per episode, linked to successful run and eligible occurrence coverage |
| Status audit | Actor, reason, command and resolution kind in typed audit fields/extension rows. Existing history payloads remain readable. One history effect per accepted command; status/revision/history update atomic |
| Evidence dispositions | Immutable late/unmatched/conflict observations with optional validated target and selected rule/version. No modification to finalized run evidence links just to append late input |
| Availability facts | Immutable source-observation events with source, sequence, effective/processing time and success/failure; no file reader. Derived freshness evaluated at explicit asOf, no mutable authoritative green flag |
| Metric provenance | New-policy completion provenance includes effective clock kind; retain exactly one immutable contribution per completed run. Existing EventAt semantics remain unchanged for legacy facts; new effective completion distinguished in typed extension, not mislabeled as raw event time |

Some existing triggers encode assumptions that Phase 3 would intentionally extend: a recovery must be from a later cycle; an occurrence must have a run; completion origin must be an evidence receipt; one active version handles a lane. Replace those checks in migration 002 with policy-aware stronger constraints, preserving old-profile checks. Retain immutable finalized results, sealed versions, exact identity checks, tenant/session ownership and no-duplicate metric facts. Do not simply disable triggers to make new tests pass.

Episode ordinal backfill is a storage ordering assignment based on existing creation receipt sequence/ID, not reinterpretation. Audit legacy rows as legacy/unknown where actor or resolution metadata was never stored; never invent an actor or actual commit instant. If preconditions cannot be established for an old row, stop migration with a diagnostic rather than silently correcting business history.

Maintain natural Profile version keys and the existing constrained run table. New tables/columns are selected to preserve legacy immutable rows and receipts. New snapshot/state schema versions must be understood by restore; unknown versions reject clearly. Older executable binaries must not be run against migration 002; reverse migration is not claimed. Restore a pre-upgrade backup for development rollback.

## Transaction model

Retain the per-team/application runtime lock and one logical command transaction. Under lock: resolve command receipt; verify fingerprint and expected revision/frontier; route by pinned context; reconstruct the affected state; evaluate deterministic domain policy; persist facts, provenance, status changes and metrics; commit receipt atomically. Retry always begins from a fresh context and committed state.

A clock command evaluates the due run set at its explicit frontier in deterministic order. For bounded simulation, its entire due set commits atomically; do not silently partially advance the clock. Later production batching/frontier scheduling is a separate ingestion decision. Evidence at a deadline is interpreted first if included in the declared frontier. A clock command cannot outrun declared pending evidence. Evidence received after an advanced frontier is handled by the late-evidence rules.

Incident commands and automatic recovery share the application lock and incident revision. A stale manual command conflicts; after a manual resolution wins, later eligible recovery may add confirmation without changing the resolution history. When recovery wins first, a competing stale manual resolution conflicts rather than replacing automatic provenance. Request and command receipt deduplication apply across restart and COMMIT acknowledgment loss.

For compatible activation, the lock defines the cutover sequence. Open runs continue under immutable pinned contexts; new Begin routes under the newly active version. A routing conflict is durably diagnosed without advancing business state. Incompatible changes return a drain-required result and make no activation mutation. Keep blocked partition state durable across restart. Never treat a raw log body as an instruction to change configuration.

## Rebuild/parity and time semantics

Rebuild uses the same typed domain rules in memory and PostgreSQL. No persisted color, outcome cache or clock shortcut becomes authoritative. Historical reports keep their original engine/policy metadata. New reports include clock commands, suppressed diagnostic matches, outcome/incident independence, exact episode/problem keys, recovery eligibility reasons and pinned-version routing decisions.

For deadline-generated contributions, effective completion time determines health date and first committed completion-processing time determines order volume date, both converted through the stored server reporting timezone/rules. Raw event time is null because no raw event exists. Exact ticks remain available for deterministic comparison. Database commit/acknowledgment remains separate optional audit. Neither a manual status change nor source freshness changes the metric denominator.

## Proposed files/projects

No new host, website or infrastructure service is proposed. Extend existing projects:

- `src/Sonda.Domain/Profiles`: versioned policy, correlation/routing and recovery contract types; validation.
- `src/Sonda.Domain/Processing`: extend existing interpreter/state restore for partitions, attempts, deadline and pinned-version semantics; small focused policy functions, not a replacement engine.
- `src/Sonda.Domain/Incidents`: episode/recovery coverage and workflow policies.
- `src/Sonda.Domain/Availability`: source-observation facts and pure projection.
- `src/Sonda.Application/Processing` and `Persistence`: versioned command orchestration/ports and deterministic scheduling inputs.
- `src/Sonda.Application/Simulation`: backward-compatible scripted command input and enriched reports.
- `src/Sonda.Infrastructure/Persistence`: additive mappings, policy-aware reconstruction/constraints and migration 002.
- `tools/Sonda.Simulator` and `tools/Sonda.PersistenceHarness`: scripted fixtures and crash hooks for new commands, no background monitor.
- `tests/Sonda.Phase3.Tests`: proposed new pure-domain/Application fixture project; existing test projects unchanged.
- `tests/Sonda.Persistence.Tests`: new migration/parity/concurrency/crash tests alongside all existing 39 tests.
- `tests/fixtures/phase3`, `artifacts/phase3`, `docs/phase-3-review.md`: only created/populated as implementation deliverables after approval.

## Implementation sequence after approval

1. Freeze all 81 regression gates; add policy-revision serialization and legacy report compatibility tests.
2. Add structural/diagnostic outcome policies, attempts, exact episodes and validated manual workflow with pure simulation fixtures.
3. Add deterministic deadlines/frontiers and availability observations; verify time attribution and recovery causality.
4. Add correlated partitioning and pinned-version routing with rejection of ambiguous transitions.
5. Apply forward migration, extend PostgreSQL adapter, prove legacy upgrade, parity, restart, crash/retry and race behavior.
6. Deliver test/source/report/migration review and stop. No Phase 4 work begins without approval.

If a policy cannot be implemented within this boundary while keeping accepted contracts, report the conflict and seek a specific decision; do not expand infrastructure or weaken regression tests.
