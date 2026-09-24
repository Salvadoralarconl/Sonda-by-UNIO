# Phase 4 proposal: acquisition storage, transactions and interfaces

Status: **approved and implemented as additive migration 003 and acquisition adapters**; see [review](../phase-4-review.md). Parent: [Phase 4 scope](phase-4-scope.md). Existing migrations, 237 tests and historical Profile bytes remain unchanged.

## Delivered implementation mapping

The sections below preserve the approved design. The concrete adapter is `PostgresIngestionStore` (partial files beside the existing stores), and the generated third ID is `20260924044645_FileAcquisition`. Interfaces are consolidated in `Sonda.Application/Acquisition/Contracts.cs`; there is no second outcome engine or separate infrastructure service.

Migration 003 adds fourteen tables: `monitoring_sessions`, `source_acquisition_revisions`, `source_acquisition_runtime`, `source_membership_sets`, `ingestion_owners`, `file_objects`, `file_path_observations`, `file_generations`, `file_checkpoints`, `ingestion_pending_commands`, `file_record_evidence`, `source_scan_batches`, `source_operational_events`, and `frontier_certificates`.

The pending slot is reused only after its committed receipt exists. It is never deleted by the runtime role. Physical-record identity is `(team_id, generation_id, start_offset)`; generation identity carries its canonical Profile/source/object and immutable acquisition revision. The physical bytes/hash, exact framing record, raw-evidence reference and receipt remain durable after slot consumption. A predecessor chain preserves admitted generation order. New earlier bytes after a committed successor create a gap instead of historical reordering.

Both existing persistence adapters expose internal transaction-scoped helpers while retaining their public transaction wrappers for simulation/legacy callers. A monitored session requires fenced admission, including manual workflow and activation commands; bypassing that boundary is rejected by database guards. The same transaction writes evidence/effects/receipt/checkpoint and consumes the pending slot. Ownership is checked again by deferred COMMIT constraints on business receipts and acquisition metadata/reservations.

Source revisions and operational enablement are separate. Generation configuration is the exact registered immutable revision, even when its current enablement differs. Operational budget revisions are allowed at drained boundaries; changing physical identity/framing/coverage after acquisition is conservatively blocked. A new source boundary needs explicit review; history is never recast under new settings.

Producer coverage and verified archive-lineage proofs share immutable `source_scan_batches` with explicit payload types. A consumed frontier references its AdvanceTime receipt. Default None produces no timer. CLI diagnostics reconstruct acquisition availability around the existing availability projection and retain business health separately.

See [review](../phase-4-review.md), [fresh SQL](../../artifacts/phase4/migration.sql), [upgrade SQL](../../artifacts/phase4/upgrade.sql), and [operations](../development/file-acquisition.md) for implementation limits and remaining environmental verification.

## Current integration points and necessary adapter work

`IPolicyProcessingStore.ExecuteAsync` currently owns its transaction. `PostgresPolicyStore` writes evidence with `SourceKey="sample"`, `Generation=command.EvidenceKey`, ordinal zero, then commits domain facts/receipt. Wrapping a second checkpoint transaction around that public method would not be atomic. Phase 4 must not do that.

Propose an internal shared PostgreSQL unit of work that accepts an existing DbContext/transaction and optional acquisition provenance. Both the existing public simulator/harness adapter and the new ingestion adapter call it. The original entry point keeps the accepted sample metadata and behavior. The production entry point supplies actual source/generation/byte range and advances the checkpoint inside that same transaction. The interpreter, result/classification/workflow rules and command semantics are unchanged. All 237 tests exercise the shared refactor as regression gates.

LegacyV1 evidence can use the original processing path through this transaction boundary; never auto-upgrade a Profile. Production deadline/Policy observation commands target explicit revision-2 Profiles only. Legacy source availability is stored/projected operationally, without injecting revision-2 commands into a LegacyV1 session. Pilot recommendation is a validated revision-2 Profile. Raw bytes are not embedded in Profile patterns.

## Proposed additive schema

Names are reviewable proposals, not existing tables. Use UUIDs for new durable identities, bigint byte offsets/revisions/epochs, timestamptz plus exact ticks/original offset where parity requires it, date plus captured reporting timezone/rules as already accepted. EF maps composite team/owner FKs explicitly with restrictive delete behavior. JSONB stores immutable configuration/manifest/envelope details; indexed identity and offset fields are relational.

| Table | Keys, representative fields and purpose |
|---|---|
| `monitoring_sessions` | PK `(team_id,application_id)`; unique `(team_id,session_id)` FK to existing processing session/runtime; host authority, creation instant, state. One stable production session per Application, retained across restart. Pilot/simulation use isolated databases/session kinds |
| `source_acquisition_revisions` | PK `(team_id,profile_id,source_key,revision)` FK existing Log Source; immutable root/globs/encoding/BOM/recursion/rotation/ordering/limits/required-source/completeness contract, hash and author audit |
| `source_acquisition_runtime` | Same owner/source key; active revision FK, enabled state, row revision, reader state and last observation pointers. Checkpoints are not stored in this rebuildable status projection |
| `source_membership_sets` | PK `(team_id,profile_id,set_revision)`; immutable list of active/required source revisions and retained old-version obligations. Activation/effective admission sequence recorded; no inferred membership from current directory listing |
| `ingestion_owners` | PK `(team_id,application_id)`; instance ID, fence epoch, lease expiry/heartbeat, revision. Claims/reclaims require row lock and database-time checks; all mutating ingestion transactions revalidate ownership |
| `file_objects` | PK `(team_id,file_object_id)`; authority/volume/file ID, incarnation, identity capability, owner source, first seen, continuity metadata. Unique active mapping of provider identity/incarnation per team; prevent cross-source double subscription |
| `file_path_observations` | Append-only alias/path, file object, scan ID, observedAt and operation. Paths do not identify evidence; alias changes do not reset checkpoints |
| `file_generations` | PK `(team_id,generation_id)`; file object FK, epoch, immutable acquisition revision, source owner, encoding/BOM decision, predecessor/rotation lineage, first seen, seal/gap references. Unique `(team_id,file_object_id,epoch)`; no destructive reuse |
| `file_checkpoints` | PK/FK generation; `committed_offset`, revision, last committed physical-record key, last receipt and fence epoch. Starts at zero, monotonically advances through contiguous committed records; never store volatile buffer position here |
| `ingestion_pending_commands` | PK `(team_id,application_id)` gives at most one outstanding reservation; immutable command ID/envelope/fingerprint/processing time/sequence, optional generation/start/end/raw bytes/hash/encoding; state and reservation audit. Unique command ID and physical candidate identity; no checkpoint advancement during reservation |
| `file_record_evidence` | PK `(team_id,generation_id,start_offset)`; end offset, terminator/BOM metadata, exact `bytea` bytes, content hash, source revision, receipt FK and existing Raw Evidence FK. Unique Raw Evidence link. Immutable after commit; record length equals `end-start`, including delimiters |
| `source_scan_batches` | Immutable bounded discovery/read snapshot: source/config/set revision, discovered generations and lengths, completed enumeration flag, identity/read result, scan times. A scan is byte coverage evidence, not itself a semantic frontier |
| `source_operational_events` | Append-only event ID, source/revision, generation optional, read/discovery/error/gap state, effective and observed/processing instants, safe diagnostic code, optional ordered Policy receipt. Distinguish missing root, no expected file, denied access, network outage, mutation and encoding error |
| `frontier_certificates` | PK team/certificate; Profile, membership-set revision, coverage time/domain, source watermark/manifest proofs and checkpoints, scan references, through-sequence, validation status and consuming clock receipt. One successful consumption; unused invalidated proofs retained |

Existing `raw_evidence` stores decoded text with actual source key, generation UUID text and start byte as ordinal. Existing receipts still reference it and retain parsing/timestamp provenance. `file_record_evidence` adds exact physical bytes/range without rewriting sample rows. Identity is canonical `(team,generation,start)`; the stable command evidence key is a versioned serialization of source/generation/start, not raw text, mtime, current path, line number or Profile Version. Hash/end offset/encoding must agree on redelivery; same key with different bytes is an integrity conflict, not new evidence.

No global deduplication of unrelated copied files by content. Rotation archive bytes alias the original generation only after proven lineage; record identity then remains original generation/start. A new epoch after truncation is genuinely a new generation even if contents happen to repeat.

### Constraints and indexes

- Every source/generation/evidence/receipt relationship has a team-qualified composite FK, including Profile/session ownership consistency. Runtime cannot rebind an old generation to another Profile or framing configuration.
- Offset checks: `0 <= start < end`, checkpoint nonnegative, immutable committed bytes and identities. One record per generation/start and no overlapping committed ranges. Under checkpoint lock, new start must equal committed offset; database guard validates matching raw evidence and receipt, then advances exactly to that end. An unchanged replay cannot increment checkpoint revision a second time.
- Deferred consistency constraint verifies each checkpoint extension has the matching committed physical-record/receipt chain in the transaction. No offset can leap over a record or partial tail. Administrative initial baseline, if ever enabled, is a distinct audited baseline record, never a forged evidence receipt.
- Keep all existing uniqueness for processing requests, application sequence, runs/attempts, occurrences, recoveries, episodes and metric facts. New indexes cover owner expiry, pending command, source/generation states, unresolved gaps and open deadline candidates. Source length is observed metadata and must not constrain checkpoint against a newly truncated current object; generation remains separate.
- Reserved command sequence equals existing application NextSequence. Reservation does not advance it. Other command paths for a monitored Application must respect the pending reservation; no manual/activation/scheduler bypass is permitted. After successful commit the pending slot is released atomically, while receipt retains its immutable envelope/audit.
- Runtime role cannot bypass owner/checkpoint guards. No DDL, trigger disabling or delete/purge privilege. Migration owner remains separate. Candidate payloads and proof records have bounded sizes and restrictive FKs.

## Exact checkpoint transaction protocol

Reading and SMB calls happen outside database locks. One application-owner coordinator performs these stages:

1. Reconcile identity and seek the last committed offset. Frame the next complete record within bounded buffers. A stale read is merely a candidate, never permission to advance.
2. In a short reservation transaction, lock owner/runtime/checkpoint, verify active fence/configuration/expected start and no pending command, and persist exact bytes plus the stable command envelope. Choose processing timestamp/sequence once, using the existing nondecreasing-time contract. Save any file-date context explicitly for time-only log timestamps; do not infer it from server Today. Commit reservation only. The checkpoint and business facts are unchanged.
3. In a fresh processing transaction, lock owner/runtime/checkpoint in that order, adopt any old owner's pending command under the current fence, and verify immutable candidate bytes/identity. If a receipt already exists, verify linkage and return it without reinterpreting or advancing again. Otherwise invoke the shared existing pipeline with the reserved command and source provenance.
4. Insert raw/normalized evidence, immutable physical range, receipt and every resulting run/incident/occurrence/recovery/history/metric fact. Advance checkpoint from exactly candidate start to end, advance application sequence, and release pending slot **in this one transaction**. Deferred guards validate consistency at COMMIT.
5. Only after successful COMMIT may the reader discard those bytes and seek from the new durable offset. Actual commit timestamp observation remains optional and separate. A missing response is resolved by receipt/checkpoint lookup, not by trusting memory.

Reservation is a bounded PostgreSQL work record, not an external queue. It preserves the original command timestamp/fingerprint if the process dies before interpretation. Processing/completion-processing timestamps therefore mean the admitted attempt's stable timestamp, as with an explicit replayed command; they do not become wall-clock COMMIT time on retry. Store reservation/first-read audit separately and explain a long delay in lag diagnostics. Do not create a new timestamp/sequence for a retried receipt, which would produce the existing idempotency conflict.

| Crash/failure point | Durable outcome and restart |
|---|---|
| After read, before reservation | Checkpoint unchanged; reread same retained bytes |
| After reservation, before interpretation | Pending exact bytes/envelope survive; process them first, offset unchanged |
| During transaction / COMMIT rejected | No evidence/effects/checkpoint extension; pending reservation survives for retry |
| COMMIT succeeds, response lost | Evidence/effects/receipt/checkpoint all exist; recover result, no duplicate business effect |
| Retry with changed path | Reconcile same object/generation; path is irrelevant to record key |
| Retry with changed contents at same key | Stop with integrity conflict; never reinterpret the changed record as the old one |
| Producer deletes after reservation | Captured candidate can commit; subsequent unavailable unread range is an explicit gap |
| Database unavailable | No checkpoint movement; bounded memory stops further read-ahead; retry stable pending work after reconnect |

A domain-rejected input is safely committed evidence with a rejection receipt, so its exact range may advance; the Profile then pauses and its frontier remains blocked. A decoding/framing failure has no valid interpretation envelope and does not advance. Later manual correction/reprocessing is not silently performed. Empty/no-op physical records still have receipts; this preserves a contiguous auditable byte chain.

Discovery, lease heartbeats and operational read failures have their own transactions and cannot move file offsets. Revision-2 source observations and clock commands use the same reservation/receipt coordinator, but have no physical record and never manufacture raw evidence or offset changes. Successful scan proof references the matching checkpoints; clock certificate use shares the clock command transaction.

## Interfaces and proposed files

| Boundary | Proposed responsibility |
|---|---|
| `IFileCatalog` / `IFileIdentityProvider` | Enumerate/open/identify with root containment and provider capabilities; no interpretation |
| `IByteSource` / `IRecordFramer` | Bounded reads and exact byte ranges; deterministic decoder/framer; no PostgreSQL or business phrases |
| `IIngestionOwnerStore` | Claim/renew/release and fence validation |
| `IIngestionCommitStore` | Reserve immutable envelope; commit record through existing unit of work; return durable checkpoint/receipt; recover pending |
| `ISourceCompletenessProvider` | Produce verifiable generation/time coverage proof or explicit unknown; no heuristic default |
| `IFrontierStore` / `IDeadlineDispatcher` | Validate/consume certificate; submit existing AdvanceTime, never calculate a new business outcome |
| `ISourceDiagnosticsReader` | Rebuild reader/backlog/availability projections for CLI reports; no HTTP endpoint |

Proposed additions after approval:

- `src/Sonda.Application/Acquisition/`: records, coordinator, ordering, proof validation and ports.
- `src/Sonda.Infrastructure/Files/`: Windows handle identity, local/UNC reader, reconciliation and strict encodings.
- `src/Sonda.Infrastructure/Persistence/Acquisition/`: new mappings, fencing/checkpoint store and shared unit-of-work integration.
- `src/Sonda.Worker/`: Generic Host/Windows Service composition, lifecycle, bounded polling/scheduler and structured operational logs. No ASP.NET host.
- `tests/Sonda.Acquisition.Tests/`, new Phase4 persistence tests, `tests/fixtures/phase4/`: byte/framing, filesystem/SMB, concurrency/crash/service fixtures using the same engine.
- `tools/Sonda.IngestionHarness/` or a bounded existing-harness extension: explicit source manifest validation, dry run, fault injection and diagnostic/report export; no background work unless explicitly started.
- Additive EF migration 003, `artifacts/phase4/` evidence and `docs/phase-4-review.md` only during approved implementation.

No new business-policy revision is proposed. If composing the accepted transaction/sequence contracts exposes an incompatible invariant, stop and report it rather than changing a regression assertion. Existing 32-bit command sequence bounds must be checked before exhaustion; no wrap/reset of a live session. Production volume capacity/widening requires a measured, compatible plan, not a hidden ID reset.

## Migration, restore and retention

Add a third migration after the unchanged Phase 2/3 chain. Create only acquisition tables/guards and carefully extend provenance validation for real source identities. Old sample rows need no fake file offsets or retroactive source claims. EF snapshot/migrations and generated fresh/upgrade SQL must agree. Old binaries must not run against the new schema; retain the existing no-destructive-Down/restore approach.

The accepted migration assertion currently names exactly two migrations. **This approval was explicitly granted**: if Phase 4 migration 003 is approved, narrowly update that single expectation to the exact ordered three IDs after generating the actual ID, preserving version/no-op/history checks. No other accepted test assertion is proposed to change. Implementation approval must explicitly include this necessary test-chain extension; otherwise report that gate before modifying it.

Upgrade seeded Phase 3 databases with old receipts, open pinned runs, unresolved/manual-resolved incidents and deadlines; compare prior hashes/rows/reports. Restore a live-test dump containing checkpoint, pending record and domain state; recover the pending command exactly and resume retained files. After restoring an older backup, reconciling with current file identities and retained archives is mandatory; a database backup alone cannot restore externally deleted source bytes. Do not reset checkpoints to current EOF.

No automatic raw-evidence, receipt, candidate, generation or operational-history purge. Terminal pending slots can be released only because their full record/envelope are linked to durable evidence/receipt. Future retention must preserve unresolved-incident evidence and the identities/receipts needed to reject old replay. Capture a capacity estimate during pilot preparation, but do not implement deletion policies.

