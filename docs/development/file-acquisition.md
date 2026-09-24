# Phase 4 acquisition operation and pilot preparation

The worker is an entry point into the modular monolith. It has no HTTP listener, frontend, accounts, notifications or production deployment. All supplied tests use synthetic files. Real GiroSol access requires separate approval. Canva Home/Search is unchanged.

## Build and test

Use the SDK/package environment in `postgresql.md`. Build Release before running tests; crash tests launch the Release persistence harness and worker. Do not rebuild assemblies while test processes hold them open on Windows.

```powershell
./.tools/dotnet/dotnet.exe build Sonda.sln -c Release --no-restore
./.tools/dotnet/dotnet.exe test Sonda.sln -c Release --no-build --logger trx --results-directory artifacts/phase4/verified-tests
```

`SONDA_TEST_ADMIN` must reference a disposable PostgreSQL 18 development server. Tests create isolated databases and a runtime role without DELETE/DDL privileges; owner-only setup runs migrations and injected database faults. Credentials stay in process environment/ignored development files, never in manifests or reports.

Migration chain:

1. `20260924023552_DurablePersistence`
2. `20260924032911_InterpretationPolicies`
3. `20260924044645_FileAcquisition`

Review `artifacts/phase4/migration.sql` for a fresh schema and `upgrade.sql` for migration 003. Apply explicitly using the persistence harness `migrate`, with the worker stopped. Startup never auto-migrates. Existing migrations 001/002 are unchanged. Down refuses destructive rollback; restore the pre-upgrade database backup and retained external files together. An older binary must not run on the new schema.

## Explicit worker manifest

`WorkerManifest` contains `scope` (teamId, sessionId, applicationId), `hostAuthority` and `sources`. A scope must already exist from validated, published Profile provisioning. There is no implicit session reset or bootstrap from a guessed log format. Each source specifies profileId, sourceKey, immutable revision, absolute root, include/exclude patterns, enabled/required, encoding, rotation, limits and completeness policy. Default completeness is None. No passwords belong in this file.

```powershell
./.tools/dotnet/dotnet.exe src/Sonda.Worker/bin/Release/net10.0/Sonda.Worker.dll validate-manifest <explicit-manifest.json>
# With SONDA_DATABASE supplied privately to a development process:
./.tools/dotnet/dotnet.exe src/Sonda.Worker/bin/Release/net10.0/Sonda.Worker.dll register <explicit-manifest.json>
./.tools/dotnet/dotnet.exe src/Sonda.Worker/bin/Release/net10.0/Sonda.Worker.dll run <explicit-manifest.json> --once
./.tools/dotnet/dotnet.exe src/Sonda.Worker/bin/Release/net10.0/Sonda.Worker.dll diagnose <explicit-manifest.json>
```

Validate performs no file-source or database reads. Register writes explicit acquisition configuration but reads no source. `run` without `--once` continuously reconciles with bounded visits; `--once` stops and releases its lease after one pass. Diagnose reads durable operational/business projections only. Actual source paths are intentionally absent from this document.

Enable/disable and acquisition-revision changes use fenced store methods; re-running register is not an administrative bypass. Disable retains every checkpoint and completeness obligation. Identity/framing/coverage changes after a generation exists require a reviewed new-source boundary; the implementation rejects an unsafe live change. Poll/read budgets can change at a drained boundary. Quarantine is sticky and cannot be cleared by disable/enable.

## Bytes, restart and limits

The worker opens sources read-only with read/write/delete sharing. Root containment is checked before open and against the final handle path; reparse traversal is refused. A Windows file ID, volume/authority and creation incarnation identify an object. A generation plus byte start identifies evidence. Paths, text and timestamps do not identify records.

LF/CRLF physical framing supports strict UTF-8 and UTF-16 LE/BE, BOM agreement, blank lines and incomplete writes. Unterminated bytes remain uncommitted even at EOF. Defaults: 64 KiB read, 1 MiB visit, 256 records per visit, 1 MiB record, 1,024 matching files. Maximum record bytes cannot exceed the visit budget. Hard limits include 16 MiB visit, 10,000 inventory generations, 256 aliases per object, and the existing signed 32-bit command sequence ceiling. Exceeding a bound blocks with a diagnostic; there is no purge/reset/skip-to-EOF.

The file buffer is bounded. The accepted interpreter still reconstructs durable history in memory; indefinite high-volume operation needs measured capacity work before rollout. All source visits share one application admission sequence, with one fenced owner and a two-minute renewable lease. Failure backoff grows to 60 seconds. A database outage stops admission; pending bytes remain in PostgreSQL and the worker exits with failure for service recovery. Files must be retained by the producer while the database is unavailable.

Rename/replacement retains old generations and handles; observed unread loss or inconsistent prefix blocks as a gap. Same-size overwrite beyond continuity samples cannot always be detected. File IDs can be reused and SMB providers differ. Lossless guarantees require producer retention/rotation contracts; the worker does not pretend to recover already overwritten bytes.

Copy/truncate requires an explicit `RotationManifest` under the allowed root, configured contract, old generation ID, current/archive paths, exact archive identity/length, immutable archive and full-lineage assertions. The reconciler verifies every already committed prefix byte against the archive, then continues the unread suffix under the original generation. Text similarity is never lineage. Ordered files require a named `sequence` regex group with unique, lexically sortable keys; ambiguous initial inventory blocks.

## Completeness and clocks

None means deadline evaluation is deferred. EOF, read success, quiet time, file age and wall time are never proof. Producer manifests must assert complete inventory, all earlier records flushed and no future earlier evidence, and enumerate each generation's committed range/configuration revision. Their trust boundary is the configured producer and read-only source ACL, not a cryptographic signature.

The scheduler persists proof, checks membership, generations, pending work, gaps, source status and offsets, then reserves the existing AdvanceTime command. It revalidates before consuming the certificate in the engine transaction. Repeated watermark polling is a no-op. Late evidence against a consumed watermark retains the accepted late-evidence interpretation and blocks the source as a producer-contract violation.

Keep source timestamp, normalized event instant, processing clock, completion-processing instant, optional database commit time and reporting date/timezone distinct. A backward host clock blocks new reservations until a valid nondecreasing time is available. Reservations retain their original clocks on retry. Time-only inputs require explicit sample/date context and Profile timezone; there is no server-date guess. A fixed date context is not a daily rollover algorithm. Multi-day time-only producers need a reviewed per-generation date contract before connection.

If an already reserved clock's proof is invalidated by exceptional external reconciliation, recovery stops; do not delete its pending row or force its certificate consumed. Operator recovery tooling for that exceptional state is not implemented. Preserve the evidence and investigate the producer/proof boundary before resuming.

## Windows service verification boundary

The Generic Host uses WindowsServiceLifetime when run by SCM. It does not install itself. Shutdown has a 30-second host budget and a separate five-second lease-release attempt; forced termination relies on lease expiry and pending recovery. Some Windows/SMB open operations are synchronous and cannot be cancelled promptly by .NET; SCM recovery on an authorized test host must verify that case.

Real console child processes have verified start, graceful one-pass stop, restart, lease release and append continuation. This session is not elevated and SMB share enumeration returns Access denied. No service was installed and no SCM restart/boot or positive SMB reconnect/identity test is claimed. See `artifacts/phase4/environment-capabilities.json`.

Remaining disposable-host procedure, requiring an authorized elevated test machine: provide a dedicated read-only test identity and synthetic root/share; publish the Release worker; register an explicitly named test service with the manifest and securely supplied database credential; verify start/stop during idle/read/transaction, kill/recovery, reboot startup, same session/fence adoption and denied roots; disconnect/reconnect the controlled share and verify stable/change identity branches; uninstall the test service afterwards. Do not execute this against a production host or real GiroSol location as part of Phase 4 verification.

## Separately approved one-source pilot checklist

- Obtain the exact authorized read-only source, service identity, share ACL and retained archive policy. Never infer GiroSol paths or phrases.
- Confirm encoding, delimiters, multiline exclusion, source date/timezone and daily rollover. Simulate representative samples and manually reconcile identifiers/outcomes/incidents/metrics first.
- Confirm handle identity and rotation behavior on that actual filesystem/SMB provider. Record retention longer than plausible outage, maximum line size and expected volume.
- Keep completeness None unless an explicit trustworthy producer contract is available. Deferred deadlines are an expected safe result.
- Pre-provision the validated Profile and persistent application scope; validate manifest with sources disabled. Review backup/restore and operational recovery responsibilities.
- After separate pilot approval, enable only that source and compare byte ranges and business results against the manual sample. Record throughput, memory/history growth, backlog and retry behavior before expanding.

Real pilot status: pending separate approval; no real source has been accessed.

