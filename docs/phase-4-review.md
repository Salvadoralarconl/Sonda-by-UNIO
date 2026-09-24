# Phase 4 implementation review

Date: 2026-09-24. Authority: [user approval](reference/phase-4-approval.txt). **Implementation and local verification are provisionally accepted; full Phase 4 capability sign-off remains blocked on the five environmental gates listed below. Phase 5 proposal preparation only is authorized.** No real GiroSol source was accessed, and no frontend, HTTP API, authentication UI, notifications or deployment was added. Canva Home/Search and the visual guide are untouched.

## User acceptance update

The user provisionally accepted this implementation and all 309 passing tests. All 309, including the 237 earlier regressions, are future gates. Final capability sign-off remains pending the five environmental checks: controlled SMB/dedicated identity; disconnect/reconnect/share identity; installed SCM lifecycle; boot/recovery; service-account ACLs. Phase 5 documentation-only proposal preparation is authorized; see [scope](architecture/phase-5-scope.md). No Phase 5 implementation or remote collectors.

## Results and regression contract

The authoritative final run is [`artifacts/phase4/verified-tests`](../artifacts/phase4/verified-tests); the machine-readable summary is [`test-summary.json`](../artifacts/phase4/test-summary.json). Build evidence is [`build.txt`](../artifacts/phase4/build.txt).

| Gate | Passing cases |
|---|---:|
| Accepted Phase 1 | 42 |
| Accepted Phase 3 pure domain/application | 76 |
| Accepted Phase 1–3 PostgreSQL cases | 119 |
| New acquisition/framing/Windows boundary cases | 11 |
| New Phase 4 PostgreSQL/file/worker cases | 61 |
| Total | **309** |

There are no intentionally skipped mocked-database substitutes. PostgreSQL cases use disposable PostgreSQL 18 databases and runtime roles with SELECT/INSERT/UPDATE, no DELETE/DDL. The final run uses commit timestamp tracking enabled; the original tracking-disabled Phase 3 results remain historical evidence, not a new Phase 4 tracking-disabled run.

All **237 accepted regression cases** pass. [`source-integrity.json`](../artifacts/phase4/source-integrity.json) verifies accepted source hashes. Only these prior files changed:

- EF's model snapshot includes the approved additive acquisition schema.
- The current-schema migration test expects the exact ordered three-ID chain. Reapply/no-op and PostgreSQL major-version assertions remain.
- The historical v1→v2 test's two setup calls explicitly target migration 002 instead of latest. Every assertion is unchanged. Reversing only these documented setup edits and the approved chain extension reproduces the accepted source hashes. Migrations 001/002 and all other accepted test sources are byte-for-byte unchanged.

Earlier checkpoint TRX folders retain intermediate results, including the corrected new capacity-test setup failure and the six-hour host-pause run in which two new tests correctly lost their leases. They are not the final delivery gate.

## Delivered source

| Location | Responsibility |
|---|---|
| `src/Sonda.Application/Acquisition` | Explicit source/identity/range/proof/manifest contracts, strict bounded byte framer |
| `src/Sonda.Infrastructure/Files` | Read-only Windows handle identity, root containment, reconciliation/pump, verified copy/truncate, proof-only deadline scheduler |
| `src/Sonda.Infrastructure/Persistence/PostgresIngestionStore*.cs` | Reservation, fencing, atomic processing/checkpoint, source revisions, proof validation, diagnostics and recovery |
| `AcquisitionEntities.cs`, `SondaDbContext.cs`, migration 003 | Acquisition tables, keys, mappings and PostgreSQL constraints |
| `PostgresProcessingStore.cs`, `PostgresPolicyStore.cs` | Shared transaction helpers around the existing LegacyV1/revision-2 engines |
| `src/Sonda.Worker` | Generic Host/Windows Service composition, bounded continuous polling, cancellation/backoff, explicit manifest registration/validation and diagnostics |
| `tools/Sonda.PersistenceHarness` | Developer-only byte/read/reservation/COMMIT process-exit injection |
| `tests/Sonda.Acquisition.Tests`, `tests/Sonda.Persistence.Tests/Phase4*.cs` | New deterministic, real Windows, real PostgreSQL and child-process acceptance cases |

The worker contains no interpretation phrases or outcome logic. The existing simulator remains the same engine. Profile pinning, incidents/occurrences/recovery, independent parent/child outcomes and metrics remain authoritative.

## Atomicity, replay and crash evidence

The durable identity is team/source → physical object → generation → byte start. A read-ahead cursor never updates the durable checkpoint. One pending application command freezes exact bytes, decoded record, sequence, Profile routing context and processing timestamp. That reservation commits separately with no progress. The subsequent transaction invokes the accepted engine and commits raw/normalized evidence, receipt, runs/incidents/metrics, physical-record range and checkpoint together, then consumes the pending slot.

The database checks contiguous ranges, receipt/evidence provenance, immutable generation/configuration identity, immutable physical records, ownership at mutation and a **deferred fence check at COMMIT**. A stale owner can finish an OS read but cannot commit work. Checkpoint update requires a matching physical-record receipt; the deferred reverse check requires that record's checkpoint to commit too.

[`crash-after-read-trace.json`](../artifacts/phase4/crash-after-read-trace.json), [`crash-after-reservation-trace.json`](../artifacts/phase4/crash-after-reservation-trace.json), [`crash-before-commit-trace.json`](../artifacts/phase4/crash-before-commit-trace.json) and [`crash-after-commit-trace.json`](../artifacts/phase4/crash-after-commit-trace.json) come from actual child processes. The child reads the synthetic physical range before the named exit boundary. The recovery input succeeds after an earlier failed order, so duplicate retries verify runs, occurrence, recovery and metric facts together. The traces enumerate every contiguous start/end/receipt; replay twice leaves state unchanged.

Additional cases inject each existing domain persistence boundary, real deferred COMMIT fence expiry, PostgreSQL disk-full SQLSTATE, conflicting bytes, concurrent duplicate readers and competing/stale owners. The disk-full case raises the real database error through a deferred trigger; it does **not** claim to have filled a physical disk. The unavailable-database case uses an unreachable local endpoint and verifies retained pending work.

## File, engine and metric parity

[`byte-parity`](../artifacts/phase4/byte-parity) records UTF-8/BOM, UTF-16 LE and BE byte ranges, restart after incomplete final write, and exact revision-2 command/simulator state equality. Every split point in representative multibyte, surrogate, BOM, CRLF and blank-line streams is exercised by the framer tests.

[`legacy-mixed-byte-parity.json`](../artifacts/phase4/legacy-mixed-byte-parity.json) delivers the original accepted mixed-order fixture through actual split file writes with explicit source date/timezone. It compares exact run state and metrics with the original simulator: successful parent, independent successful/failed children, one failed-order Incident, one contribution per completed logical order, and separate parent/child System Health units.

Real local tests cover append/unchanged poll, rename/recreation, truncation, explicit copy/truncate archive prefix verification, retained backlog, lost unread bytes, rapid detectable mutation, daily filename ordering, late earlier-file discovery/append, hard links, overlapping patterns, root containment, actual Windows junction refusal and read-sharing denial/recovery. A fully read unchanged file performs no full content read; bounded prefix continuity samples remain intentional.

## Migration, reconstruction and restore

Migration 003 is **`20260924044645_FileAcquisition`**. Review [fresh SQL](../artifacts/phase4/migration.sql), [incremental SQL](../artifacts/phase4/upgrade.sql), [model check](../artifacts/phase4/model-check.txt) and [seeded upgrade evidence](../artifacts/phase4/upgrade.json).

The upgrade begins on the delivered Phase 3 schema, seeds receipts, a failed order/Incident and open retry, snapshots every prior table, applies migration 003 and re-applies it. Prior rows/hashes and replay state remain identical, then a successful retry produces the accepted recovery. The full legacy upgrade, reconstruction and backup/restore tests also remain regression gates.

[`restore.json`](../artifacts/phase4/restore.json) and the test dump exercise pg_dump/pg_restore with an open Application Run, checkpoint, consumed completeness proof and pending order record. The restored database recovers the exact pending command, then the real byte reader consumes remaining retained file bytes; domain state equals the uninterrupted control. Database backups cannot recreate deleted external bytes; missing observed unread ranges block as a gap.

## Deadlines, availability and version transitions

Default completeness None always defers. Read success/EOF/quiet time is not a timer. [`producer-boundary.json`](../artifacts/phase4/producer-boundary.json) verifies an actual producer manifest, partial earlier success blocking the timer, completed boundary-time success admitted before AdvanceTime, and duplicate watermark suppression. Inventory/membership/pending/partial/failure changes invalidate proof. Contradictory late evidence retains the accepted late policy and blocks additional source frontiers.

Pending evidence must commit before activation. Compatible activation pins an open run to the old version and routes the next run to the new version; the result exactly matches PolicySession replay. Disable/re-enable keeps offsets and generation framing configuration. Unsafe root/encoding/coverage changes are rejected after generations exist.

[`availability.json`](../artifacts/phase4/availability.json) demonstrates NotObserved/Fresh/Stale/Disconnected/recovery/disabled handling without invented business Failures, Incidents or metric contributions. Business health and source availability remain distinct. Required disabled/uncertain sources cannot appear healthy merely because an old read succeeded.

## Acceptance map and capability limits

This maps all proposed IDs to evidence, without treating environmental or unexecuted sub-scenarios as passed. Test method names are in the referenced source files and TRX results.

| IDs | Implemented/local evidence | Qualification |
|---|---|---|
| P4-01 | FilePump restart/append/unchanged-poll cases | Bounded continuity reads are separate from content bytes |
| P4-02–07 | CrashTests; WorkerTests; deferred COMMIT expiry | Actual child exits, durable retry; installed-service stop is separate |
| P4-08 | Duplicate/concurrent/conflicting physical key tests | One receipt/effect set |
| P4-09–11 | RotationTests; Windows handle tests | Local rename/truncate and explicit archive lineage verified |
| P4-12 | Rapid-regrowth/lost-tail fail-safe tests | Universal ID reuse/undetectable overwrite is not a lossless guarantee; SMB change/reconnect blocked |
| P4-13–14 | Ordered backlog/late earlier-file cases; deleted unread generation | Ambiguity becomes a durable gap |
| P4-15–17 | FramingTests, FilePump/FileSafety tests | Unterminated final record remains stalled, never synthetically completed |
| P4-18–19 | Missing local file and actual sharing-denial recovery | Dedicated ACL/SMB identity tests require controlled host/share |
| P4-20 | Bounded daily backlog visits and per-source worker scheduling | Synthetic retained backlog; no real-volume throughput claim |
| P4-21 | Stale/contending owners, deferred expiry, duplicate race | Fences checked at COMMIT; ownership adoption preserves pending work |
| P4-22 | Two-source durable sequence and exact engine parity | Existing correlated routing acceptance remains a regression; no global source timestamp sort is introduced |
| P4-23 | Actual hard-link alias/overlapping glob test; canonical unique physical owner | Intentional multiple Profile subscriptions are unsupported |
| P4-24 | Poll-only append/discovery tests | No watcher is used, so watcher overflow cannot be authoritative |
| P4-25–29 | FrontierTests, ControlTests, ProducerTests | Proven producer contract only; default deferred; no unsupported contract claimed |
| P4-30 | AvailabilityTests and operational-only failure fixtures | Disabled required sources remain unavailable/unknown |
| P4-31–32 | Pinned activation, pending ordering, config/disable/enable tests | New identity/framing boundary after acquisition requires explicit review |
| P4-33 | Physical time-only legacy parity, backward-clock test; accepted parser/DST regressions | Fixed explicit date is not multi-day rollover inference |
| P4-34 | Actual console start/one-pass graceful stop/restart/lease release | **Installed SCM/boot/forced blocked-SMB shutdown testing environmentally blocked** |
| P4-35 | Unavailable DB, deferred disk-full error, record limits | Disk exhaustion simulated by real PG error; no destructive disk fill |
| P4-36 | Direct SQL checkpoint rejection, physical/fence guards, restricted runtime role | No trigger is hidden or disabled |
| P4-37–39 | Fresh/upgrade/reapply, restore continuation, full replay/parity | Original migrations unchanged; receipts/history retained |
| P4-40 | Read-only handle, out-of-root and actual junction tests | Dedicated service-account ACL validation remains environmental |
| P4-41 | Original mixed-order fixture through split real byte writes | Exact runs/metrics and independent outcomes |
| P4-42 | Capacity/manifest/sequence checks | No integer wrap, reset or automatic deletion |

## Known limitations and remaining verification

- **Controlled SMB share and dedicated service identity are unavailable.** Share enumeration returns Access denied. No positive UNC transport, disconnect/reconnect, failover identity or share-ACL capability is claimed.
- **No elevation/disposable service-host authorization is available.** The real Generic Host console lifecycle is verified; service installation, SCM recovery, reboot and service-account ACL tests are not. These are remaining Phase 4 verification gates, not Phase 5 authorization.
- Synchronous Windows/SMB opens may exceed the host's cancellation window; test SCM recovery against the actual provider before production use.
- Producer manifests/archive lineage are explicit trusted contracts. No contract means no frontier. There is no recovery for bytes deleted before observation, and same-size mutations beyond samples are not universally detectable.
- Unsafe framing/root/coverage changes after acquisition and sticky quarantines require reviewed operator recovery. A reserved clock whose proof becomes invalid stops safely; there is no automatic cancellation/repair workflow that deletes pending history.
- File read buffers/visits are bounded, but the accepted engine's full-history reconstruction grows with retained history. Capacity is intentionally conservative and requires pilot measurement; no auto-purge or session reset exists.
- Fixed time-only date context does not implement automatic multi-day date rollover. No real GiroSol encoding, filename, phrase, path, timezone or rotation behavior has been assumed.

See [operation/service test procedure and pilot checklist](development/file-acquisition.md), [environment capability evidence](../artifacts/phase4/environment-capabilities.json), and the preserved acceptance matrix. **Do not call full Phase 4 production capability complete until the environmental gates and provider-specific limits are resolved. Stop for review; do not begin Phase 5.**



