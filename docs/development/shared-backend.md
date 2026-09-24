# Shared backend operation and review runbook

Phase 5 adds the API and account boundary around the accepted engine. This is a development/review runbook, not authorization to deploy. Phase 6 and all five Phase 4 environmental verification gates remain pending.

## Build and regression gates

Use the repository SDK pinned in `global.json`. Restore locked packages, then build/test `Sonda.sln -c Release`. New projects are `Sonda.Access`, `Sonda.Api.Contracts`, `Sonda.Server`, `Sonda.Access.Tests`, and `Sonda.Api.Tests`. The simulator remains its original project and implementation.

Set `SONDA_TEST_ADMIN` privately to a local PostgreSQL 18 administrative connection. Tests require a real server and create/drop only uniquely named disposable databases. API tests also start synthetic loopback HTTPS, generate temporary certificates and use workspace-contained synthetic log files. No SMB, Windows-service installation or GiroSol access is performed. PostgreSQL tools and the local SDK used by the HTTPS restore test are under `.tools/postgresql/pgsql/bin` and `.tools/dotnet`.

Example test command after the connection is set (do not print it):

```powershell
dotnet test Sonda.sln -c Release --no-restore --logger trx --results-directory artifacts/phase5/final-tests
./tools/Verify-Phase5Evidence.ps1
```

Use a fresh results directory per full run; do not combine duplicate test runs. The verifier checks the 309 accepted cases, the original 237 subset and all 123 baseline source hashes. Prior failed/intermediate runs are separate from final evidence.

## Schema and database identities

Apply the three original `SondaDbContext` migrations first. Apply the two separate `AccessDbContext` migrations with an operator/migration identity, using the reviewed `artifacts/phase5/access-migration.sql` or EF tooling. The server never migrates automatically. Access history is `sonda_access.__AccessMigrationsHistory`; core history stays unchanged.

```powershell
dotnet ef database update --project src/Sonda.Access --context AccessDbContext
```

The design-time factory reads `SONDA_DATABASE`. Use private environment/configuration injection, not a connection string in command arguments or committed JSON. Use a restricted runtime login with schema USAGE and the required SELECT/INSERT/UPDATE privileges in `sonda` and `sonda_access`; it must not own schemas or have migration/role privileges. No HTTP feature requires DELETE. The restricted-role test verifies provisioning, rejection of DDL/deletion and rollback when audit INSERT is denied. Operator recovery and backups use separately controlled privileges. Existing Phase 4 fence/immutability triggers still apply.

## Required host configuration

| Setting | Meaning |
|---|---|
| `SONDA_DATABASE` | Restricted runtime database connection; private configuration |
| HTTPS Kestrel endpoint/certificate | Serve directly over HTTPS; HTTP is rejected outside the dedicated test environment |
| `Security:KeyDirectory` | Persistent directory for encrypted ASP.NET Data Protection keys |
| `Security:KeyCertificateThumbprint` | Encryption certificate available to the host identity |
| `Security:KeyCertificatePath` / `Security:KeyCertificatePassword` | Alternative operator-managed PFX and private password configuration |
| `Monitoring:AllowedRoots` | Explicit fully qualified Windows local roots or UNC server/share roots; empty means deny all |
| `Monitoring:HostAuthority` | Stable host authority matching monitoring registrations; default is machine name |
| `Monitoring:RunEnabled` | Explicit opt-in to the existing hosted acquisition worker; default false |
| `Simulation:DotnetExecutable` | Operator-controlled .NET executable path |
| `Simulation:Assembly` | Built original `Sonda.Simulator.dll`; never supplied in HTTP |

The application name for key protection is `SONDA.SharedServer`. Protect certificate private keys, key directory, configuration and database credentials with the intended identity's ACLs. Dedicated service-account ACL verification itself remains an unfulfilled Phase 4 environmental gate. Do not use the `Testing` environment for a real host. No forwarded-header trust or cross-origin browser access is enabled; reverse-proxy exposure needs explicit later configuration/review.

Do not run the standalone Phase 4 worker and the shared worker as competing owners. The shared host composes the accepted file pump and deadline scheduler behind the same per-application admission gate and durable owner fence used by API workflow/activation. It recovers a reserved input before admitting another command. Ownership conflicts are surfaced, not stolen. Browser sessions do not control worker lifetime. An inaccessible source remains unavailable; an allowed UNC string does not prove it is reachable.

## Bootstrap and account recovery

Provision the initial core Team through the controlled operator setup already required by earlier phases; public team registration is not exposed. With private host configuration present:

```powershell
dotnet src/Sonda.Server/bin/Release/net10.0/Sonda.Server.dll bootstrap-admin <existing-team-id> <login>
dotnet src/Sonda.Server/bin/Release/net10.0/Sonda.Server.dll recover-admin <existing-team-id> <login>
```

These are local commands and return before starting the HTTP server. They display a one-time grant only in the operator's protected output. They do not accept a password in arguments and do not install a service. Do not capture output into routine logs or shared transcripts. Recovery targets an existing Admin of the team and is audited.

The client first requests `/api/v1/session/csrf`, keeps its secure antiforgery cookie and sends `X-CSRF-TOKEN` on redemption/login and all other unsafe requests. Redeem the grant with a new password, then sign in. There is no default password or anonymous registration. Invitations last 24 hours; reset grants last 30 minutes. Store only token hashes. If a grant response is lost, replay returns `secretUnavailable`; issue an explicit replacement operation. The API does not email grants.

Password length is 15–128 characters. Five failed password attempts lock an account for five minutes. The credential endpoints share a ten-per-minute/IP limiter with no queue. Sessions expire after 30 minutes idle or eight hours absolute. Logout, password reset/change, membership disable or role change invalidates existing sessions. The last enabled Admin cannot be removed through member administration.

## Add a monitored application/log

1. Admin POSTs Application name/optional description with a new operation UUID. The actor's team is authoritative.
2. Admin creates a Profile for that Application. It starts as an editable, inactive revision-2 Draft with no guessed detection phrases.
3. Admin creates source configuration: explicitly allowed root, include/exclude names, recursion, supported encoding, required/enabled and accepted acquisition options. No file reads occur. Configuration IDs are server-generated.
4. Edit the Profile using its expected draft revision. Validate it, simulate bounded samples, review the immutable report, and publish that exact revision/report (acknowledging warnings where present).
5. Read `/profiles/{id}/activation` for active version and **activation** revision, then activate explicitly. Initial activation uses expected version/revision zero. Activation revision is not the lane's per-input revision.
6. Acquisition occurs only after activation and when the operator has enabled the worker. Desired source `enabled=true` alone cannot start processing.

This is narrow provisioning, not arbitrary CRUD. There is no Application/Profile delete, arbitrary file browser/read, source credentials, force read, checkpoint/generation edits, gap clearing or rename/delete. Add a new log to an existing monitored Application by configuring a new Profile and going through the same lifecycle. Adding a new source directly to an already activated Profile is rejected; existing source changes use the accepted fenced/drained boundary. After generations exist, only the existing permitted operational options (enabled, polling/read/visit/file limits) can change. Identity/framing/coverage changes are rejected while obligations exist. No repair workaround is exposed.

Inactive source edits use the source metadata revision. Live source edits use the current acquisition revision and durable command receipt. Published source snapshots remain immutable. An edit before first activation that differs from the published source snapshot requires a new publication. Path allowlist changes are operator-only and rechecked by the worker; they never rewrite history.

## Command recovery and investigation

Reuse the same operation UUID and semantic payload after a lost response. Do not generate a new UUID automatically on a transport error. Provisioning/account mutations commit the change, access receipt and audit together. Existing engine/configuration adapters have their own authoritative core receipt; the access ledger records intent before dispatch and derives the final result afterward. A Submitted operation is not proof of either failure or success.

GET `/operations/{id}` is limited to the submitting actor or a current Admin. It can reconcile an already committed core receipt but cannot execute an unactioned intent or advance input. An authorized retry can resume an intent that had not reached the core. After core reservation, system recovery uses the recorded command/actor/time. Current permission is checked on every retry/read; a receipt is not a permission bypass. Conflicting payload/actor reuse returns 409.

Back up PostgreSQL plus encrypted key files and the required certificate/private key. The loopback test restores a `pg_dump` into a separate disposable database and verifies a cookie across restored keys, then rejection with unrelated keys. If keys cannot be recovered, restore data but require reauthentication/reissue grants; never bypass authentication. A backup is not a rollback procedure that may silently replay file checkpoints.

## Limits and qualification

Ordinary bodies: 256 KiB, including chunked requests. Simulation: 1 MiB/1,000 entries, one active preview/publication, no queue; preview child process has a 30-second kill deadline and 16 MiB report cap. The original publication adapter repeats deterministic simulation in-process with its existing regex/input bounds; that repeat has no separate killable process deadline. Validation/drafts allow at most 128 rules/512 alternatives. This is not an OS sandbox or a capacity guarantee for adversarial configurations.

Search: 31-day window, literal text 3–256 characters, default page 50/max 200, five-second database command timeout. No regex/SQL search. Cursors expire after 15 minutes and bind team, filters and size. Pages are separate committed snapshots; late arrivals and live status changes may require refresh. Search snippets cap raw text at 4,000 characters and run links at 200 with explicit truncation flags; detail returns the committed evidence, never a path read.

Dashboard uses one read-only repeatable-read transaction, server reporting timezone and persisted contribution facts. It rejects incompatible saved timezone rules rather than reinterpreting history. System Health uses completion event date; completed-order volume uses completion-processing date. Empty health is null. No observations or disabled/gapped sources do not become green. Initial bounds are 200 applications/dashboard, 200 Profiles/detailed diagnostic and 10,000 observation receipts per availability projection (limit becomes Unknown). These are explicit review limits, not measured production capacity. No retention/purge policy is implemented.

All Phase 4 SMB/SCM/boot/ACL gates remain pending. No frontend, real GiroSol access, remote collectors, notifications or production deployment is delivered here.
