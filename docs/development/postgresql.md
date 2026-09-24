# Local Phase 2 PostgreSQL setup

Phase 2 uses a real PostgreSQL 18 database. It has no production monitor, web host, API, accounts or deployment. All test data is synthetic. The existing simulator remains database-independent.

## Dependencies and isolation

Pinned SDK: `global.json`. EF Core 10.0.4, Npgsql provider/driver 10.0.3, PostgreSQL 18.6 were used for the acceptance run. Dependency lockfiles are checked in. Restore with `--locked-mode`.

Provide a local PostgreSQL 18 server and matching `pg_dump`/`pg_restore` tools. The integration fixture creates a random `sonda_test_<hex>` database and restricted `sonda_test_<hex>_app` role, applies migrations as owner, runs the adapter as the restricted role, and drops only those generated test objects. The dump/restore test creates and drops another generated `sonda_restore_<hex>` database. The supplied admin must be able to create databases/roles and terminate its test backend. Never point these tests at a production server. Tests fail instead of silently skipping when prerequisites are absent.

Two local options:

- Docker: set an ephemeral `SONDA_DEV_PASSWORD`, then `docker compose -f deploy/development/compose.postgres.yml up -d`. It binds only loopback port 55432 and uses disposable storage. Install local PostgreSQL client binaries for the dump/restore subprocess test and pass their directory below. No Redis/queues/other infrastructure.
- Portable Windows binaries: use the official [PostgreSQL Windows download](https://www.postgresql.org/download/windows/) / [EDB binary archive](https://www.enterprisedb.com/download-postgresql-binaries). The acceptance environment extracted binaries to `.tools/postgresql/pgsql`, initialized `.tools/pgdata` with SCRAM authentication and a random ignored password file, and bound `127.0.0.1:55432`. No Windows service was installed. Start with `pg_ctl -D .tools/pgdata -l .tools/pg-server.log -w start`; stop with `pg_ctl -D .tools/pgdata -m fast -w stop`. Windows sandbox restrictions may require the normal user context to start the process.

Credentials, cluster files and binary downloads stay under ignored `.tools/`; do not commit them. Do not run both options on the same port.

## Verification

From PowerShell at the project root, set `$testConnection` locally (do not paste credentials into reports) and run:

```powershell
./deploy/development/Test-Phase2.ps1 -AdminConnection $testConnection -PostgresBin 'C:/path/to/postgresql/bin'
```

Results go to `artifacts/test-results/review/phase1.trx` and `phase2.trx`, separately to prevent one test project's TRX overwriting the other. Parity and crash evidence go to `artifacts/phase2/`. These artifacts contain synthetic inputs and generated team/session identities, not connection strings. Restore and crash subprocess tests launch with hidden windows and pass credentials only in child environment variables.

The portable SDK resides at `.tools/dotnet/dotnet.exe` in this workspace. Elsewhere use a matching installed .NET 10 SDK. The crash test currently expects the workspace SDK/path on Windows; configure that path before moving the acceptance harness to a different development environment. This is an explicit development limitation, not production portability support.

## Explicit migrations and harness

Set `SONDA_DATABASE` to an explicitly created **development** database. The harness does not migrate as a side effect of processing:

```powershell
./.tools/dotnet/dotnet.exe run --project tools/Sonda.PersistenceHarness -c Release -- migrate
./.tools/dotnet/dotnet.exe run --project tools/Sonda.PersistenceHarness -c Release -- simulate tests/fixtures/mixed-orders.json
```

`simulate` persists synthetic inputs using the same Domain/Application engine and prints durable state. Different fixtures intentionally reuse the Profile ID/version with differing content; use a separate disposable database or separately named team for each fixture. The adapter correctly rejects conflicting immutable version content. Tests isolate team IDs and compare both adapters with that identical isolated request.

`process <request.json>` executes one serialized `ProcessingRequest`. The developer-only `crash-after-commit` switch exits the subprocess with code 73 after COMMIT and before returning. `crash-before-commit` exits with code 74 and lets PostgreSQL roll back the broken connection. Do not add these fault switches to a production host.

The generated EF migration and model snapshot live in `src/Sonda.Infrastructure/Persistence/Migrations`. Its Up operation loads reviewed embedded `001_initial.sql`, including constraints/triggers which EF does not generate automatically. `artifacts/phase2/migration.sql` is the runnable EF script, including migration history. Down migration deliberately refuses destructive rollback; restore a development dump instead. There is only one initial migration, so upgrading from a nonexistent prior SONDA database is not claimed. Reapplication and fresh creation are tested. Future changes require a new migration and review of trigger/constraint changes; never edit an already deployed migration.

Actual commit timestamps are optional audit metadata. The tests support both `track_commit_timestamp=on` and `off`; changing it requires restarting this disposable server. Disabled/unavailable tracking leaves `database_commit_at` null and records an explicitly named acknowledgment timestamp. Neither value substitutes for engine processing or completion-processing time.

Acceptance status: the portable Windows setup was tested; the optional Docker Compose setup was not executed here. All 39 integration tests passed with commit tracking both enabled and disabled. The local test server was stopped after verification. See [Phase 2 review](../phase-2-review.md).

## Phase 3 command workflow

Phase 3 adds migration `20260924032911_InterpretationPolicies` after the original migration. Use `artifacts/phase3/migration.sql` for a fresh database and `artifacts/phase3/upgrade.sql` for the explicit v1-to-v2 increment. Do not run older binaries against this schema. The existing Phase 2 commands and profiles retain LegacyV1 behavior.

After setting the same SDK/package variables and private `SONDA_TEST_ADMIN`, run the complete gate:

```powershell
./.tools/dotnet/dotnet.exe test Sonda.sln -c Release --no-restore --logger 'trx;LogFilePrefix=phase3' --results-directory artifacts/test-results/local-phase3
./.tools/dotnet/dotnet.exe run --project tools/Sonda.Simulator -c Release -- --input artifacts/phase3/fixtures/same-cycle-retry/request.json --output artifacts/local-policy-report
```

The simulator detects the `commands` envelope and uses the same PolicySession engine as persistence. JSON reports preserve exact Problem Identity, attempts, versions, workflow, receipt diagnostics, deadlines, availability and metrics. Exit 2 means a simulation retained blocking/rejected input, not successful production processing.

`policy-process <request.json> [crash-after-commit]` in the persistence harness accepts `scope`, `profileId` and a serialized `PolicyCommand`. Publication/session bootstrap is explicit; this command does not invent a source, migrate a database or continuously process files. Test fault switches belong only to the developer harness.

The real PostgreSQL matrix restarts the adapter after every scripted command and compares durable facts to simulation. Deadline/frontier commands are explicit; there is no wall-clock scheduler. See `docs/phase-3-review.md` for final evidence and limitations.

## Phase 4 acquisition

The Phase 3 description above is historical. Phase 4 adds migration `20260924044645_FileAcquisition` and a producer-proof-gated dispatcher; it never infers timers from EOF or idle time. See [acquisition operation](file-acquisition.md) and [review](../phase-4-review.md). The final runnable SQL is in `artifacts/phase4/migration.sql` (fresh) and `upgrade.sql` (003 only). No changes were made to delivered migrations 001/002.

Build the complete Release solution before running tests, because acquisition crash and console-lifecycle tests launch the Release harness/worker. `tools/Verify-Phase4Evidence.ps1` validates final TRX totals and the 237 accepted cases. The `ingest-record` developer harness mode accepts a serialized fence/generation/physical-record envelope and an optional explicit synthetic byte-file path. Exit codes 76/75/74/73 identify after-read/after-reservation/before-COMMIT/after-COMMIT boundaries. These fault switches are absent from the worker.
