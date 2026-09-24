# Phase 2 implementation review

Status: **implemented; stopped for user review. Phase 3 has not started.** The approved Phase 1 Domain/Application behavior remains the reference contract. PostgreSQL persistence is an adapter around that engine; the existing simulator still runs without a database.

## Verified results

Release build succeeded with zero warnings/errors. **81 distinct tests passed: 42 Phase 1 and 39 PostgreSQL integration tests; zero failures or skips.** The 39 PostgreSQL tests passed a second time with optional commit-timestamp tracking disabled (120 passing test executions in these three recorded suites, not 120 distinct tests).

| Suite | Passed | Evidence |
| --- | ---: | --- |
| Original Phase 1 contract | 42 | [TRX](../artifacts/phase2/phase1.trx) |
| PostgreSQL, commit tracking on | 39 | [TRX](../artifacts/phase2/postgres-on.trx) |
| PostgreSQL, commit tracking off | 39 | [TRX](../artifacts/phase2/postgres-off.trx) |

[Machine-readable totals](../artifacts/phase2/test-summary.json). Tests used real disposable PostgreSQL **18.6** databases, applying migrations as owner and processing with a restricted application role. They did not use EF's in-memory provider or a mocked database. The local test server is now stopped; its development configuration has been restored to tracking enabled for a future explicit start.

EF reports **no pending model changes**. [Generated migration SQL](../artifacts/phase2/migration.sql) includes the initial schema, database integrity triggers, constraints and migration history. Fresh creation and reapplication were tested. A real `pg_dump`/`pg_restore` test restored an open run, replayed its receipt, continued processing and preserved recovery.

## Source and architecture boundaries

| Responsibility | Entry point |
| --- | --- |
| Application persistence ports and request/receipt contracts | [Contracts.cs](../src/Sonda.Application/Persistence/Contracts.cs), [configuration port](../src/Sonda.Application/Persistence/IConfigurationStore.cs) |
| Shared parse/match/apply path | [InterpretInput.cs](../src/Sonda.Application/Processing/InterpretInput.cs) |
| Explicit engine state export/restore | [InterpreterState.cs](../src/Sonda.Domain/Processing/InterpreterState.cs), [ProfileInterpreter.cs](../src/Sonda.Domain/Processing/ProfileInterpreter.cs) |
| EF entities and mapping | [Entities.cs](../src/Sonda.Infrastructure/Persistence/Entities.cs), [SondaDbContext.cs](../src/Sonda.Infrastructure/Persistence/SondaDbContext.cs) |
| Transactional processing, deduplication, reconstruction | [PostgresProcessingStore.cs](../src/Sonda.Infrastructure/Persistence/PostgresProcessingStore.cs) |
| Drafts, publication, immutable versions, activation | [PostgresConfigurationStore.cs](../src/Sonda.Infrastructure/Persistence/PostgresConfigurationStore.cs) |
| Internal concurrency primitive | [IncidentRevisionStore.cs](../src/Sonda.Infrastructure/Persistence/IncidentRevisionStore.cs) |
| Database-owned constraints | [001_initial.sql](../src/Sonda.Infrastructure/Persistence/Migrations/001_initial.sql) |
| Explicit developer harness | [Program.cs](../tools/Sonda.PersistenceHarness/Program.cs) |
| Repeatable verification | [Test-Phase2.ps1](../deploy/development/Test-Phase2.ps1), [setup instructions](development/postgresql.md) |

The modular monolith remains intact. Domain does not depend on PostgreSQL/EF. Application ports do not depend on Infrastructure. The shared interpretation helper extracts the simulator's existing per-input behavior; it does not replace the domain state machine. No application-specific detection phrases were introduced into the engine.

## Transaction and idempotency evidence

One logical input acquires the application runtime row lock and commits evidence, receipt, run changes, incident changes, occurrences, recovery/history, runtime counters and completion contribution facts in one PostgreSQL transaction. Receipt lookup precedes interpretation. A duplicate request returns the stored receipt; a conflicting reuse is rejected. A new request ID for the same source evidence binds to the existing receipt and cannot later be rebound to different content.

Database constraints protect cycle identities, order attempts, exact problem keys, unresolved incident episodes, occurrences, successful recovery links and one metric contribution per finalized run. Full problem keys are compared even when digests collide. Team/session/profile keys constrain relationships. Immutable evidence, profile versions and contribution rows reject direct modification.

| Scenario | Executed evidence |
| --- | --- |
| Successful commit, restart and complete replay | Three parity fixtures reload state for each input and replay every committed request without effects |
| Failure before commit | Four injected adapter boundaries roll back the complete input |
| PostgreSQL rejects COMMIT | A real deferred constraint failure leaves no committed input effects; retry succeeds after removal of the test-only failing trigger |
| Connection dies before commit | Actual backend termination rolls back; retry succeeds |
| COMMIT succeeds, process dies before response | Separate processing child exits with code 73 immediately after COMMIT; restart/retry returns the committed receipt |
| Concurrent duplicate input | Competing requests produce one committed result |
| Duplicate business identities | Direct SQL attempts are rejected by PostgreSQL constraints/triggers |
| Repeated recovery/reprojection | Full replay preserves recovery and contribution counts; rebuilding metrics inserts no new facts |
| Draft/status races | Competing contexts produce one accepted revision and one conflict, without lost history |
| Backup/restore | A generated database dump restores into a separate generated database and processing resumes |

The explicit crash-after-COMMIT artifact records **6 runs, 2 occurrences, 1 recovery, 5 metric facts**, with **zero duplicates** of each after retry: [crash/retry evidence](../artifacts/phase2/crash-retry.json). This checkpoint precedes the final parent completion, explaining the five completed-run facts versus six runs. The process exits before postcommit observation or a caller success response. Optional missing commit audit never causes domain replay.

Tests: [transaction/parity](../tests/Sonda.Persistence.Tests/PersistenceTests.cs), [constraints/versioning](../tests/Sonda.Persistence.Tests/InvariantTests.cs), [crash/migration](../tests/Sonda.Persistence.Tests/CrashAndMigrationTests.cs), [concurrency](../tests/Sonda.Persistence.Tests/ConcurrencyTests.cs), [restore](../tests/Sonda.Persistence.Tests/RestoreTests.cs), [reconstruction](../tests/Sonda.Persistence.Tests/ReconstructionTests.cs). The last includes restart ordering across the four-digit allocation-counter boundary.

## Profile versions and clocks

Publication stores an exact immutable Profile snapshot, content hash, rule/pattern rows, parsing/identifier configuration, source snapshot, validation and simulation provenance. Publication requires matching validated draft/report provenance. Activation and administrative edits use expected revisions and idempotent command receipts. Tests retain historical v7 facts after activating v8, reject version mutation and stale revisions, and verify source edits do not alter published source configuration.

**Rejecting activation while an Application/Order Run is open is a temporary Phase 2 safeguard, not permanent SONDA business policy.** Conservative compatibility checks also prevent unsupported identity/recovery changes while unresolved problems exist. Later policy work must review these boundaries explicitly.

Source timestamp text, normalized timestamp/quality, per-input processing time, first-completion processing time, database recording time, actual commit time and acknowledgment time remain distinct. Exact engine timestamp ticks/context survive storage. Server reporting timezone determines calendar buckets. Actual commit time is nullable optional audit metadata; with tracking disabled it is not fabricated from processing or acknowledgment time. Both database configurations passed.

## Phase 1 report parity

Tests compare per-input traces, runs, incidents, metrics and replay stability against the same Phase 1 simulator, using identical isolated synthetic team IDs for each comparison. Artifacts include the request, Phase 1 report, durable state and comparison summary. Original fixture hashes identify the source fixtures; random team isolation prevents unrelated fixture configurations from sharing an immutable version key.

| Fixture | Observed contract | Comparison |
| --- | --- | --- |
| Mixed orders | Parent Success; 2 successful orders and 1 failed order; one Warning; Logs Today 3; System Health 3/4 = 75% | [Parity](../artifacts/phase2/mixed-orders/parity.json) |
| Repeated recovery | One incident, 2 failed occurrences, 1 successful recovery; Logs Today 3; System Health 4/6 = 66.67% | [Parity](../artifacts/phase2/repeated-recovery/parity.json) |
| Priority Ignore | Specific Ignore wins; no incident; successful parent; Logs Today 0; System Health 100% | [Parity](../artifacts/phase2/priority-ignore/parity.json) |

Current application health is reconstructed from unresolved incident severity plus finalized-run observation. Metrics are rebuilt from immutable per-run contribution facts using the existing calculator: application cycles and completed orders each contribute once to evaluated-run health; only completed orders contribute to Logs Today/five-day volume. No mutable dashboard color/count is authoritative.

## Concrete storage choices and limitations

- Separate EF storage rows persist the engine's typed state. Application/Order Runs share one constrained `runs` table with explicit scope and parent relationships; EF inheritance is unnecessary here. Profile version identity is the composite team/Profile/version number. Exact snapshots and typed payloads use serialized text alongside relational identity/constraint columns. This is not an opaque whole-engine checkpoint replacing relational facts.
- Current state is reconstructed from durable rows, evidence links and saved allocation counters. The adapter currently reloads complete lane history for correctness. Large-history throughput/memory and production ingestion capacity have not been measured.
- Metric rebuild reads canonical facts directly. Optional cached projection generations/watermarks were not introduced; no cache deletion/swap protocol is claimed. Concurrent live dashboard querying belongs to a later host increment.
- Simulation/report provenance is captured through the developer workflow. There is no draft editor, sample upload UI, authentication or live operational ingestion. Persisted fixture sessions are synthetic, explicitly scoped datasets.
- Callers must retry the same immutable processing command after rollback or uncertain acknowledgment. No background retry scheduler, production reader cursor or delivery service was added.
- Postcommit observation can be absent after a crash. Receipt existence, not audit availability, proves committed interpretation. Historical PostgreSQL transaction metadata may later become unavailable; actual commit time is not a business identity.
- The internal status revision primitive proves concurrency/history storage only. It does not approve manual workflow policy or expose an API. Unresolved Phase 1 policies, fallback deadlines, same-cycle retries and mid-cycle version transition rules remain deferred.
- Tests cover the pinned local Windows SDK/PostgreSQL setup. The crash harness currently expects the workspace SDK and Release executable paths. Portable PostgreSQL was exercised; the supplied optional Docker Compose setup was not executed on this machine.
- One initial migration exists. No upgrade from an earlier deployed SONDA schema, production rollout or destructive Down migration is claimed. Down deliberately refuses; use development restore. Future deployed schema changes require new migrations.

## Review gate

Both user amendments are implemented and evidenced. No production monitoring, continuous ingestion, API/UI, authentication, Home/Search frontend, notifications, remote collectors, production deployment or distributed infrastructure was added. Canva remains the approved visual authority; the visual guide and Home reference image were not redesigned.

**Stop here for review. Phase 3 requires explicit approval.**

## Subsequent user acceptance

The user accepted Phase 2, including PostgreSQL, crash/retry, versioning, parity, reconstruction and backup/restore evidence. All 81 Phase 1/2 tests are now regression gates. The next authorized work is the [Phase 3 proposal](architecture/phase-3-scope.md) only; implementation requires approval. The earlier stop statements record the delivery gate, not a continuing Phase 2 approval request.

Subsequent authorization: the user approved Phase 3 implementation and the narrow obsolete migration-count assertion update. The Phase 2 delivery limitations above are historical; revision-2 extensions and final verification are documented in [Phase 3 review](phase-3-review.md). LegacyV1 remains the accepted contract.
