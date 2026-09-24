# File Location monitoring and durable ingestion

Status: proposal for the shared website. The backend monitor runs on a designated host with read access to team-configured paths. Browsers do not continuously tail local files. Remote collectors are a future extension unless the pilot proves the chosen host cannot reach the required logs.

The overall direction is approved, but Phase 1 is implemented and stopped for review before Phase 2. Business completion follows approved deterministic cycle boundaries first; reader EOF, poll intervals, and framing deadlines must never become implicit global run timeouts. Profile fallback timeout/grace policies apply only where reliable business boundaries are unavailable.

## Source ownership and discovery

Each Profile owns an array of Log Sources. Each source records host, file/directory path, include/exclude patterns, recursion, encoding, framing, stream association, poll interval, first-read policy, and operational limits. A service on Windows uses its own identity and UNC paths for network shares; a user's mapped drive letter may not exist in the service session.

The service opens logs read-only and never rotates, truncates, renames, or repairs GiroSol files. Initial support should cover append-only text files, rolling filenames, and verified rename rotation. Compressed archives, proprietary formats, and cross-host merged streams require explicit adapters and tests.

Discovery uses a periodic reconciliation scan as the correctness mechanism. FileSystemWatcher can wake the scanner sooner, but notifications are only hints: they may be repeated or missed. Rescan after overflow/error and periodically even when no notifications arrive. [Microsoft FileSystemWatcher documentation](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher?view=net-10.0).

Resolve overlapping configured paths to physical file identity on the monitoring host. Reject duplicate ownership within a team by default; do not interpret the same source under two Profiles accidentally. If deliberately required in future, separate interpretations must be explicit. Filesystem identity reliability varies by filesystem/share and must be tested in the pilot.

## Physical file identity

A pathname is not a unique file identity. Track host + stable filesystem file ID when available + a generation number. Preserve renamed path aliases. Store a bounded fingerprint and file metadata as supplementary continuity checks, not as proof that identical text means the same record.

- **Append:** resume at the committed byte offset for the same generation.
- **Rename rotation:** keep the original identity/cursor and drain its handle or renamed file; discover the replacement as a new identity. Rotation does not terminate a business cycle by itself.
- **Truncation:** if length shrinks below the cursor or continuity checks show rewritten content, close the old generation, report any gap, and start a new generation according to policy.
- **Replacement under the same filename:** a new file ID creates a new generation, even if its size resembles the old one.
- **File-ID reuse:** compare creation/continuity metadata and generation history; do not resume blindly into a newly created file that reused an old ID.
- **SMB identity unavailable/unstable:** use a documented weaker fingerprint/generation strategy and expose uncertainty. Do not claim an exactly-once guarantee across ambiguous replacement.

Copy/truncate rotation can destroy unread bytes, or truncate and regrow beyond the cursor between polls. No byte reader can reconstruct data it never captured. Continuity checks help detect some cases but cannot prove detection of every overwrite. For reliable capture, require append/rename retention long enough to catch up, or a future writer/collector protocol. Never hide detected gaps behind a healthy monitor status.

## Initial-read policy

The admin chooses explicitly on first activation (D12): from beginning, from a supported historical boundary, or from the current end. Recommended onboarding: test on copied logs, then choose the production start point with a preview of volume and skipped history.

Tail-from-now stores its initial offset as an audited bootstrap event. If EOF is in the middle of a line, skip that preexisting partial line through its next terminator rather than interpreting a suffix as a complete message. From-beginning runs must preserve event dates so backlog does not inflate today's figures. Timestamp-free history requires an explicit fallback policy before import.

## Byte-level capture and normalization

1. Reconcile files and acquire the source-file cursor/ownership token.
2. Open the file with sharing appropriate for a concurrently writing producer and rotation, without requesting write permission.
3. Read bounded byte batches beginning at the committed offset. Offsets are bytes, never decoded character counts.
4. Find complete physical line boundaries using the configured encoding. Handle CRLF, LF, BOM, UTF-8 multibyte splits, and supported UTF-16 byte alignment deliberately.
5. Persist original bytes and start/end offsets for complete lines. Commit the raw rows and next byte offset in the same database transaction.
6. Retain a trailing partial physical line for the next read; do not advance the durable cursor past its start until captured. If it is lost before capture, record the gap. For a permanently closed rotated file without a final newline, use an explicit final-fragment policy and mark that evidence accordingly.
7. A separate normalizer assembles logical entries, such as a message plus stack-trace continuation lines, from already-durable physical lines. Persist pending frame references and parser version with its normalization checkpoint.
8. Commit normalized entry, raw spans, and normalization progress together. The interpreter then applies it once using its durable receipt.

Oversized lines/frames must not create unbounded buffers or be silently discarded. Capture bounded evidence chunks with a diagnostic and stop interpretation of that record until an explicit policy handles it. Physical chunks are evidence, not counted logical records. Invalid decoding retains original bytes and a parse diagnostic; it does not fabricate a business incident or a successful run.

A framing timeout is not an order timeout. Incomplete multiline assembly and business completion deadlines are separate settings. When framing cannot finish reliably, keep evidence pending/quarantined and expose lag.

## Transaction checkpoints and replay

Three independent progress markers are deliberate:

| Stage | Atomic commit | Restart behavior |
| --- | --- | --- |
| Capture | Raw bytes/positions + file cursor | Resume from last committed complete physical line |
| Normalize | Logical entry + raw spans + framing checkpoint | Rebuild pending frames from persisted evidence |
| Interpret | Receipt + run/incident/history/metrics/deadline effects | Re-deliver unapplied entries; skip committed receipts |

Unique keys on physical generation/byte offset and session/normalized entry make retried delivery idempotent. Identical messages at different byte offsets remain distinct evidence; content hashes are not general deduplication keys. Reprocessing cannot use only “last line text” or last-write time as its checkpoint.

If a transaction succeeds but the process crashes before acknowledging it, retry finds the committed key and returns its result. If it rolls back, the cursor/effects roll back with it. This provides at-least-once delivery with effectively-once committed interpretation for identifiable source records. It is not a guarantee against lost source files, ambiguous copied files, or missing producer events.

## Ordering and multiple sources

Maintain strict byte order within each physical file and preserve rotation lineage for a single stream. A Profile may own several sources, but owning several paths does not automatically establish a total order between them.

Initial safe modes: independent serial streams, or a verified sequence of rolled files for one logical stream. Interleaved/concurrent cycles across multiple files require reliable cycle correlation and an approved ordering/lateness policy (D09). Shared configuration supports future expansion, but unsupported combinations fail validation instead of silently mixing runs.

Source event timestamps are valuable for reporting, but not sufficient to reorder all incoming lines safely. Do not sort a complete application history by timestamps: clocks can skew and multiple events can share a timestamp. Preserve source positions, recorded processing sequence, and extracted correlation keys. A late event from an older cycle cannot clear a newer failure.

## Ownership, concurrency, and backpressure

Initially one backend service owns acquisition. Add a database-backed host ownership lease with a monotonically increasing fencing token if duplicate startup/failover is possible; every cursor commit verifies the token. A restarted or stale worker cannot commit after ownership transfers. Distinct application lanes can run concurrently while row locks serialize their own state changes.

Use bounded read batches and in-memory queues. When interpretation lags, captured entries stay in PostgreSQL. If storage approaches limits or the database is unavailable, stop advancing cursors and retry with bounded backoff. Source files must remain available long enough to catch up. Surface backlog, oldest unapplied record, free space, and storage faults. Do not replace an unbounded memory queue with an undocumented local spool.

Shutdown stops new reads, finishes or rolls back in-flight transactions, persists pending normalized-frame references, and releases ownership. No reliance on a browser tab, workstation lock state, or a user session for continuous monitoring.

## Retention and recovery

Before a real pilot, agree retention and source-rotation windows. Retain original incident evidence and required run associations; preserve archived evidence links and checksums if storage is moved later. Purging raw data must not cascade-delete business facts or leave misleading links. There is no automatic retention deletion in this proposal.

Backups must include database state, Profile versions, source identities/cursors, authentication key material, and deployment configuration. Restore in a stopped-monitoring state, validate file continuity, then resume. A backup is not a copy of the current source logs; logs deleted after the backup may prevent complete recovery. Test that case and report the gap.

## Required monitoring observability

Per source: availability, last discovery/read, current generation/offset, bytes behind, oldest unprocessed input, decoder/framing errors, permission failures, rotation gaps, and active Profile version. Per application: open-cycle/order counts, oldest open run, pending deadlines, rule timeouts, quarantine count, activation status, and last committed sequence.

These facts explain whether SONDA can observe an application. They do not replace the business health calculation. A disconnected source must never be presented as fresh evidence that everything is healthy.
