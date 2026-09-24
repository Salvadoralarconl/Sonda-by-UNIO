# Phase 2 proposal: durable persistence

Status: **implemented; stopped for user review**. Phase 1 and its 42 tests remain the reference contract. Amendments: open-run activation rejection is a temporary Phase 2 safeguard, subject to later review; successful COMMIT followed by process termination before response and restart/retry is an explicit mandatory acceptance scenario. Stop for review before Phase 3.

## Outcome and boundary

Connect the existing Domain/Application engine to PostgreSQL through an EF Core adapter. Demonstrate that one supplied input, all of its interpretation effects, and the processing checkpoint either commit together or leave no effects. Restarting and replaying committed inputs must reproduce the accepted simulator behavior without duplicate runs, Occurrences, recoveries, or completed-order contributions.

This is one modular monolith, with a developer harness and real disposable PostgreSQL integration tests. No production monitor, web host, website, authentication UI, Home Page work, notifications, deployment, Redis, broker, Elasticsearch, or microservices. The permanent visual guide and approved Home image remain unchanged.

Read the [schema and mappings](phase-2-schema.md) and [transaction/adapter design](phase-2-transactions.md) as part of this proposal. These bounded documents take precedence over aspirational Phase 2 details in the older full-system architecture. None approves outstanding business policies in the decision register.

Visual authority: [@Canva → Sonda Home Page Design](../reference/canva-approved-design.md), both pages reviewed (Home and Search), governs later appearance. Phase 1 governs domain behavior. The expanded proposal includes explicit parsing/identifier configuration mappings, normalized evidence references, Draft → Validated → Published → Activated → Superseded lifecycle, administrative/activation revision conflicts, and separate source/normalized/processing/completion-processing/commit/reporting-time concepts. No frontend work is included.

## Reference contract and implementation approach

- Keep all 42 existing Phase 1 tests passing without weakening assertions. Keep deterministic fixture reports semantically identical, including exact problem keys, selected rules, outcomes, occurrence grouping, recovery, and metric arithmetic.
- `ProfileInterpreter`, `SampleParser`, `PatternMatcher`, `ProfileValidator`, and `MetricCalculator` remain the source of interpretation behavior. EF entities do not introduce their own matching, grouping, outcome, or recovery algorithms.
- Add only an explicit engine state export/restore seam and a typed before/after change set needed for durability. The current interpreter has private cycle/open-order/counter state and internal setters; EF cannot safely reconstruct it by treating its public lists as the whole state. Prove snapshot round trips at every input boundary against uninterrupted execution.
- Keep the simulator's default in-memory path and CLI usable without PostgreSQL. Share the per-input Application orchestration between the durable harness and simulation, retaining simulation's per-Profile diagnostics and stop behavior. Do not parse human-readable `Actions` strings as database commands.
- No new outcome policies: same-cycle retry, overlapping cycles, contradictory structural outcomes, unsupported timing, or other existing `PolicyDecisionRequired` cases remain diagnostics. EOF still does not close a run.

## Proposed work packages

| Package | Deliverable |
| --- | --- |
| Persistence foundation | PostgreSQL 18, EF Core 10/Npgsql 10, pinned compatible patch versions and lockfiles; explicit mappings, migrations, database constraints, scoped context |
| Configuration storage | Teams, Applications, Profiles, exact immutable versions, normalized rules/alternatives, source metadata, simulated report provenance, publication and safe activation commands exposed only to the harness/tests |
| Interpretation adapter | Stable supplied-input identities, durable receipts, per-application serialization, state restore/save, atomic runs/incidents/evidence/metrics, failed/uncertain commit recovery |
| Reconstruction | Restart from durable facts; current health and metric queries; rebuildable optional daily projections; no dashboard/API |
| Verification and review | Disposable-PostgreSQL integration suite, crash and race tests, migrations/restore exercises, parity reports, documented run commands and limitations |

## Proposed projects and files

Paths below are **planned files**, not files created by this proposal. Retain the existing solution and project dependency direction.

```text
src/Sonda.Domain/
  Processing/InterpreterState.cs           # versioned export/restore contract
  Processing/ProfileInterpreter.cs        # minimal state boundary only
src/Sonda.Application/
  Processing/InterpretInput.cs             # shared parser/matcher/engine orchestration
  Processing/ProcessingRequest.cs          # stable input key, supplied times/context
  Processing/ProcessingChangeSet.cs        # typed changes, never Action-string parsing
  Persistence/IProcessingStore.cs          # scoped atomic unit-of-work boundary
  Persistence/IProcessingTransaction.cs
  Persistence/IConfigurationStore.cs
  Persistence/IReadModelStore.cs
  Configuration/PublishProfileVersion.cs
  Configuration/ActivateProfileVersion.cs
  Simulation/SimulationRunner.cs           # use shared orchestration; preserve CLI contract
src/Sonda.Infrastructure/
  Sonda.Infrastructure.csproj
  Persistence/SondaDbContext.cs
  Persistence/Entities/                    # storage types separate from domain objects
  Persistence/Configuration/               # one EF mapping per table/entity family
  Persistence/Migrations/                  # reviewed SQL constraints/triggers included
  Persistence/PostgresProcessingStore.cs
  Persistence/PostgresConfigurationStore.cs
  Persistence/StateRehydrator.cs
  Persistence/Queries/ApplicationStateQuery.cs
  Persistence/Queries/MetricFactsQuery.cs
  Persistence/Queries/MetricProjectionRebuilder.cs
  Persistence/CommitObservation.cs
tools/Sonda.PersistenceHarness/
  Sonda.PersistenceHarness.csproj
  Program.cs                              # migrate, seed synthetic config, feed/replay, inspect
tests/Sonda.Persistence.Tests/
  Sonda.Persistence.Tests.csproj
  PostgresFixture.cs                      # real isolated database/container, disposal
  MigrationTests.cs
  ConstraintTests.cs
  Phase1ParityTests.cs
  RestartAndReplayTests.cs
  TransactionFailureTests.cs
  ConcurrencyTests.cs
  ProfileVersionTests.cs
  ProjectionTests.cs
tests/fixtures/persistence/                # supplements existing fixtures, not replacements
deploy/development/compose.postgres.yml    # development/test only, no production deployment
docs/development/postgresql.md
docs/phase-2-review.md
artifacts/phase2/                          # results/parity/SQL review artifacts, no credentials
```

No new host or frontend project. Domain stays free of EF/Npgsql. Application depends on Domain and its own ports; Infrastructure implements those ports. Harness and tests compose the adapters. Use explicit repositories/transactions for these use cases, not a general repository framework.

## Development database and migrations

After approval, supply an opt-in disposable PostgreSQL 18 container with a loopback-only port, pinned image version, health check, ephemeral test credentials, and isolated database names. A provided development PostgreSQL connection is an alternative when container tooling is unavailable; tests must verify it is PostgreSQL and use a uniquely named disposable test database. Never silently substitute SQLite or EF InMemory or report skipped integration tests as passing. Lack of a usable runtime is a reported blocker.

Keep credentials outside tracked files. Separate migration/owner credentials from restricted application credentials; test immutability and mutation permissions using the latter. The harness does not automatically migrate on every start. An explicit development migration command applies reviewed migrations under a database migration lock before processing begins.

Initial migrations create configuration, interpretation, audit, and fact tables with constraints and SQL trigger/function definitions. Record EF migration history; test empty-database creation, migration reapplication, and upgrading a seeded preceding migration when one exists. Review generated SQL. Future schema changes use additive/backfilled migrations; never rewrite immutable historical Profile snapshots or silently reinterpret finalized runs. An incompatible checkpoint format requires explicit migration or reconstruction, not silent reset.

A development-only `pg_dump`/restore exercise into a second disposable database verifies versions, receipts, unfinished engine state, facts, and post-restore replay. Production backup schedules, credentials, hosting, recovery objectives, and deployment runbooks remain later work.

## Acceptance scenarios

Every integration scenario below runs against actual PostgreSQL with migrations and actual constraints. Existing 42 in-memory tests remain an additional mandatory gate. Use independent connections for races and fresh process/context instances for restart tests.

| ID | Scenario and required evidence |
| --- | --- |
| P2-01 | Persist/reload all three accepted fixtures and compare semantic reports to the same in-memory engine; storage IDs/audit timestamps may differ, domain keys/results may not |
| P2-02 | Mixed-order fixture: successful parent, two successful orders and one failed order; appropriate Warning Incident; 3 completed-order contributions; 3/4 health = 75%, independently persisted parent/child outcomes |
| P2-03 | Two failed attempts in later cycles then successful retry: one Incident, two Occurrences, one recovery, one automatic-resolution history transition; three order completions; no rewritten failures |
| P2-04 | Stop/restart after every input in each fixture, including open parent/open order and an existing diagnostic Occurrence; IDs, counters, evidence, outcome and reports match uninterrupted execution |
| P2-05 | Inject failure after evidence insert, after run/incident writes, after facts/checkpoint, and immediately before COMMIT. No partial receipt/effects/checkpoint survive; retry produces exactly the baseline |
| P2-06 | A definite failed commit followed by retry uses a fresh context and succeeds once. A lost response after a successful COMMIT is separately simulated: retry finds the committed receipt and makes no new effects |
| P2-07 | Sequential and concurrent duplicate request/input deliveries return one committed result; same request key with different payload/context fails; same evidence through another request ID remains one interpretation |
| P2-08 | Repeated recovery input and concurrent delivery create one recovery per Incident/successful run and one status transition. A new success is not a reason to recover an already resolved Incident again |
| P2-09 | Repeated diagnostic lines for the same Incident/run add evidence to one Occurrence; another run with that exact unresolved problem appends another. Different keys, case, leading zeros, namespace, Profile, team and session do not merge |
| P2-10 | Direct SQL under the application role cannot cross ownership boundaries, alter published versions or finalized facts, insert two unresolved episodes for one problem, duplicate occurrences/recoveries/facts, or insert recovery tied to a failed run |
| P2-11 | Delete/rebuild only derived metric projections; rebuild twice and after restart, with concurrent new completion. Published generation equals canonical facts at its watermark; no extra contributions |
| P2-12 | Event yesterday/processing today/commit later remain different values. Test reporting midnight and DST, zero denominator, five zero-filled dates, different source timezone, and precision boundaries |
| P2-13 | Two contexts update one incident revision: stale writer gets a conflict, no lost update/history. Exercise persistence compare-and-swap primitives without inventing a new user workflow or auth UI |
| P2-14 | Activation with an open cycle is rejected; safe activation uses the next input sequence; in-flight receipt/retry retains old version; old runs/config remain unchanged. Incompatible identity/recovery semantics require an explicit future decision |
| P2-15 | Changed draft/sample/engine provenance invalidates publication eligibility; simulation data cannot contribute to durable-harness operational metrics; same published version is reconstructed byte-for-byte from stored snapshot |
| P2-16 | Competing worker and activation commands serialize; out-of-order sequence rejected; blocked lane remains blocked after restart; other application lanes continue |
| P2-17 | Forced identity-digest collision still distinguishes full keys; same exact key cannot be registered twice. Priority ties on unrelated rules remain legal; same-input competing ties still diagnosed by the existing engine |
| P2-18 | Migrations, role permissions, dump/restore and replay pass; a database unavailable at startup or COMMIT cannot produce a false success result |
| P2-19 | Competing draft/source edits and activation changes use expected revisions: one succeeds, stale command conflicts; duplicate successful command returns prior result. Editing invalidates validation eligibility |
| P2-20 | CAM v7 completes historical runs; publish and activate v8 at safe boundary; new inputs use v8, replayed v7 inputs and their run/occurrence/fact provenance stay v7; supersession changes no historical interpretation |
| P2-21 | Rebuild application state after discarding cached summaries: unresolved Error → Error; otherwise unresolved Warning → Warning; after recovery Stable if any finalized run, otherwise unobserved. Compare durable reconstruction to engine |
| P2-22 | Inspect raw timestamp text, normalized instant/quality, per-input processing and first-completion processing independently, including missing/date-less source timestamps; preserve original parser behavior and completion-date attribution |
| P2-23 — explicit amendment | PostgreSQL COMMIT succeeds; terminate the processing child process before it returns success; restart and retry the identical request. The committed receipt must be returned with no added runs, Occurrences, recoveries or metric contributions |

Fault hooks inject failures at named adapter boundaries, not fake database implementations. Include a real connection termination/COMMIT acknowledgment-loss exercise in addition to deterministic hooks. Tests must assert persisted row contents/counts, checkpoint and receipt identity, not just that an exception was thrown.

## Completion and review gate

Phase 2 completes only when all 42 Phase 1 tests still pass, the new PostgreSQL scenarios pass without hidden skips, fixture parity and restart equivalence are demonstrated, and schema/constraints/migrations can be reviewed and reproduced from documented commands. Deliver source links, actual test counts/TRX, fixture report comparisons, crash/race outcomes, generated migration SQL, development setup, and any unresolved limitations. Do not claim production file-monitor or end-to-end website guarantees.

Stop for user review before Phase 3. Phase 3 is a separately approved interpretation/workflow increment: unresolved cycle/order policies, expected durations/grace/fallback scheduling, approved manual Incident transitions, and version-transition policies beyond the safe boundary in this proposal. Production file acquisition remains Phase 4 in the existing roadmap; accounts/API, website/Profile editor, Home Page, pilot, notifications and deployment remain later or separately requested. No infrastructure is added merely to anticipate those phases.

## Implementation review

The user approved this bounded increment with both amendments. See [Phase 2 review](../phase-2-review.md) for source, 81 passing tests, PostgreSQL evidence, concrete representations and limitations. Phase 3 remains unauthorized.
