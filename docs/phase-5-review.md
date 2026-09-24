# Phase 5 delivery review

Phase 5 implements the shared backend and minimal safe Admin provisioning. **Stop here for user review; Phase 6 is not authorized.** The original Domain/Application engine, acquisition implementation, checkpoint model, core EF model and migrations, and Canva reference remain unchanged. An added Infrastructure partial adapter attaches a transactional command receipt to the existing fenced source-configuration boundary.

## Results

The final evidence is [test-summary.json](../artifacts/phase5/test-summary.json), with individual cases and TRX filenames, and [core-integrity.json](../artifacts/phase5/core-integrity.json). Run `tools/Verify-Phase5Evidence.ps1` to verify the results against the preserved source baseline.

| Gate | Result |
|---|---|
| Phase 1 accepted contract | 42 passed |
| Phase 3 pure engine/policy suite | 76 passed |
| Accepted acquisition unit suite | 11 passed |
| Accepted PostgreSQL persistence/acquisition suite | 180 passed |
| New access/path/provisioning/migration suite | 28 passed |
| New API/account/query/workflow/HTTPS/host suite | 45 passed |
| Total | **382 passed, zero failed/skipped** |
| Original accepted Phase 1–4 gate | **309/309 unchanged**, including **237/237 original cases** |
| Frozen original source files | **123/123 unchanged** |

Of these, 235 cases use real disposable PostgreSQL; the remaining 147 are pure engine/acquisition/path cases. The API suite uses the actual ASP.NET middleware/endpoints where HTTP behavior matters, and direct host/query adapters for transactional fault and projection checks. The HTTPS restore test runs a real loopback Kestrel child process. No mocked database is counted as PostgreSQL evidence.

The final API TRX contains all 45 API cases. The first Release attempt's snapshot test correctly encountered the existing ingestion fence when its setup tried an unfenced write. The test was corrected to commit concurrent synthetic evidence through the accepted fenced acquisition adapter. That first failed attempt is retained in `artifacts/phase5/attempt-1-tests`; final gate files are in `artifacts/phase5/final-tests`. No accepted assertion was changed.

## Delivered behavior

- Shared JSON ASP.NET Core host, PostgreSQL Identity/account storage, current team membership authorization, Admin/Member roles, secure durable cookie sessions, antiforgery, lockout/rate limits and protected persistent keys.
- Local first-Admin/recovery commands; single-use invitation/reset grants; durable revocation and last-Admin concurrency protection. No account UI or email integration.
- Admin Application creation, initial inactive Profile Draft creation, and allowed-root source configuration. Team/actor and generated identities are server-owned. Local and syntactically allowed UNC configuration is accepted without file probing. Outside roots, traversal/device paths, mapped drives and credential/browsing/repair controls are rejected or absent.
- Draft edit, validation, bounded existing-simulator preview, immutable provenance, exact-revision publication and explicit initial/later activation. Creation does not read logs. New Profiles stay inactive until the workflow completes.
- Dashboard, bounded Search, evidence/run/Incident investigation, supported Incident workflow, operation status, audit and role-filtered monitoring diagnostics. Source/event/processing/completion/DB-commit/acknowledgment timestamps remain separate. Unknown actual commit time remains null.
- Existing in-process file pump/deadline scheduler behind the same application admission gate and PostgreSQL owner fence as API workflow/activation. No browser is needed for monitoring. Pending reserved input recovers before a new command.

The formal HTTP artifact is [openapi.json](../artifacts/phase5/openapi.json). The [permission and acceptance map](phase-5-acceptance-results.md) connects delivered routes/scenarios to tests. [Source inventory](../artifacts/phase5/source-inventory.json) records new project/files and their hashes. The [operation runbook](development/shared-backend.md) covers private configuration, migrations, bootstrap, lifecycle, replay, key recovery and limits.

## Persistence and migration evidence

[Access migration SQL](../artifacts/phase5/access-migration.sql) is an idempotent EF-generated script for exactly:

1. `20260924130738_AccessFoundation`
2. `20260924141909_AccessSecurityConstraints`

They use `sonda_access.__AccessMigrationsHistory`. The original `sonda` schema/model/snapshot and the exact core three-migration chain remain unchanged. Access constraints cover membership/roles, grants, session membership, immutable preview/audit records, operation identity and finalized outcome immutability. Provisioning joins core/access contexts in the same PostgreSQL transaction. Live source-configuration receipt and acquisition revision commit in the same existing core transaction.

Tests verify fresh/repeated application, upgrade over populated Phase 4 data with a full core-row fingerprint before/after, and a restricted runtime login that can provision but cannot perform DDL or delete audit. Denying audit INSERT rolls back the newly created Application and operation record. The model has no pending EF changes; see [migration evidence](../artifacts/phase5/migration-evidence.json).

## Crash, replay, concurrency and parity

- Before core reservation, after pending reservation/before interpretation, and after core COMMIT/before access acknowledgment: restart/retry reuses the durable identity and produces one workflow effect. Polling can reconcile a committed core receipt without redispatch. Four mixed-fixture metric facts remain four.
- Provisioning COMMIT with lost acknowledgment replays one Application. Source configuration COMMIT with lost acknowledgment replays one acquisition revision/command receipt; conflicting duplicate or stale revision fails without extra rows.
- Publication acknowledgment loss reconciles the immutable version's core receipt. A replayed preview still returns its original report after a later draft edit; that stale report cannot publish a changed draft. Concurrent draft edits have one revision winner.
- Concurrent invitation redemption has one winner; concurrent Admin removals preserve an enabled Admin. Disabling an account between cookie validation and command admission cannot exploit a previously tracked EF membership. Concurrent workflow requests respect the expected Incident revision.
- Pending file input is recovered before activation. The open cycle retains v1 and the next cycle uses v2. The API activation precondition uses the engine's activation count, not the per-input lane revision. This adapter defect was found and corrected during the new acceptance testing.
- The mixed-order reference gives **75% System Health, three completed orders and one failed-order Incident**. Dashboard reads remain coherent when another transaction commits during a captured snapshot. Late processing affects the processing-day order count while health stays on completion event date. Ignore/quarantined evidence stays searchable; missing event timestamps are not invented. Quarantined evidence does not add completed runs/metrics.
- Fresh/stale/disconnected/disabled source projections preserve business health separately and do not invent business failures or healthy green state. Unregistered simulation data is excluded. Existing accepted deadline/overlap/episode/manual-recovery/DST contracts remain regression gates.
- Real `pg_dump`/`pg_restore` of access and core data plus encrypted keys preserves the authenticated session and core fingerprint. Unrelated keys reject the old cookie. This proves the tested local restore path, not SCM/boot/service-account capability.

[Actual Search plan](../artifacts/phase5/search-plan.json) captures `EXPLAIN (ANALYZE, BUFFERS)` for the real parameterized query and a nine-evidence synthetic fixture. A separate blocking-lock check verifies the five-second query timeout. This small-fixture measurement is not a production scale benchmark; no core indexes or extra infrastructure were added.

## Known limitations and retained boundaries

- All five Phase 4 environmental gates remain **pending**: controlled SMB/UNC dedicated identity; disconnect/reconnect/share identity; installed Windows Service/SCM; boot/recovery; service-account ACL verification. An accepted UNC configuration is not a passed SMB test.
- Initial Team creation stays operator-controlled. Accounts are one-team-per-account; no public registration, SSO/MFA, email, notifications or production exposure is included.
- Add a new log under an already monitored Application through a new Profile/lifecycle. Adding a source directly to an already active Profile is not exposed. Existing source changes require the accepted fenced/drained boundary; existing generation obligations prevent identity/framing/coverage changes.
- Preview is a bounded child process using the same original simulator. Publication's original adapter repeats deterministic simulation in-process with existing bounds; it has no separate killable-process timeout. Preview is not an OS security sandbox. Sample endpoints expose evidence simulation, not arbitrary production engine/control commands.
- Search and dashboard have explicit bounds described in the runbook. Search has no cross-page snapshot promise or unbounded count/export. No production volume/latency claim is made. Stored evidence/previews have no new purge policy. Timezone changes require a future explicit migration policy.
- OpenAPI describes requests, security and typed query DTOs. Some report/operation result envelopes remain flexible JSON; their concrete fields are defined by endpoint source and the preserved report contracts, rather than fully expanded generated response schemas.
- Core session Kind retains the accepted `DurabilityHarness` value; durable monitoring registration is the operational discriminator. The API does not infer production data from that legacy label.
- Only one configured host authority owns a monitored application. The worker remains directly attached to local/UNC paths with the existing Phase 4 limitations. No remote collectors or replacement ingestion model were introduced.

`@Canva → Sonda Home Page Design` remains the approved Phase 6 visual authority: Page 1 Home, Page 2 Search. No frontend, Canva change, real GiroSol access, source repair, notification or deployment occurred. Review Phase 5 before authorizing any Phase 6 work.
