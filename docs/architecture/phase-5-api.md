# Phase 5 proposed HTTP, query and command contracts

Status: **approved design; see [delivery review](../phase-5-review.md) and [operating contract](../development/shared-backend.md)**. Read with [scope](phase-5-scope.md) and [security](phase-5-security.md). The delivered endpoint schema is recorded in `artifacts/phase5/openapi.json`.

## Common contract

Version prefix `/api/v1`. JSON DTOs, UTC ISO-8601 instants with named timestamp roles, ISO reporting dates, string enums, opaque stable IDs. IDs are not authorization. Return decimal percentages already calculated by the accepted metric policy; no client recomputation. Error responses use Problem Details with a stable `code`, correlation ID, safe detail and field/engine diagnostics where authorized. No stack traces or raw SQL.

Read responses carry `asOf`, reporting timezone where relevant, and data/availability qualifiers. Pagination uses an opaque integrity-protected cursor bound to team, resource, normalized filters, sort and expiry. Always reauthorize. Proposed cursor lifetime 15 minutes, default page 50, max 200. Unsupported fields/enums/sorts are rejected, not silently ignored. Never bind EF entities or accept database column names from clients.

Mutation bodies carry a caller-generated `operationId` and the **relevant** expected revision; ETag/If-Match can carry the revision consistently. Omitted precondition is 428; stale precondition 409 with an authorized current revision. Draft revision, activation revision, incident workflow revision and access membership revision are different values; never substitute one for another. Internal processing sequence/fence/version routing/actor/processing clock are server-only.

Useful response distinctions: 400 malformed input; 401 no valid session; 403 insufficient own-team role; 404 missing/foreign resource; 409 stale revision, idempotency conflict, unavailable capability or existing engine compatibility conflict; 413 size bound; 422 Profile/domain validation rejection with diagnostics; 429 bounded capacity; 503 DB/owner unavailable before admission. A core rejected receipt is durable: return its safe rejection and receipt reference without retrying it as a new command automatically.

## Proposed route inventory

Route paths are stable proposal contracts, not frontend layouts. `profileId`/`incidentId` and related IDs are always resolved inside the authenticated team.

| Methods / resource | Access | Purpose |
|---|---|---|
| GET `/session/csrf`; POST `/session/login` | Anonymous with rate/CSRF policy | Pre-login token and credential authentication |
| GET `/session`; POST `/session/logout`; POST `/session/logout-all` | Authenticated | Current account/role/capabilities and session revocation |
| POST `/session/password`; POST `/account-grants/redeem` | Self / single-use grant | Password change; invitation/reset redemption with bounded generic errors |
| GET `/team`; GET `/team/members` | Self team / Admin member list | Team metadata; administration projection |
| POST `/team/members`; POST `/team/members/{id}/invitation` | Admin | Inactive account plus invitation; replacement invitation |
| PATCH `/team/members/{id}`; POST `/team/members/{id}/reset-grants` | Admin | Role/enabled change with revision; one-time reset grant |
| GET `/dashboard` | Member/Admin | One coherent dashboard snapshot |
| GET `/applications`; GET `/applications/{id}` | Member/Admin | Current business/availability state and Profile references |
| GET `/application-runs`; GET `/application-runs/{id}` | Member/Admin | Parent cycle search/detail and pinned version |
| GET `/order-runs`; GET `/order-runs/{id}` | Member/Admin | Separate logical attempts, parent cycle and extracted identity |
| GET `/incidents`; GET `/incidents/{id}` | Member/Admin | Episode/problem identity, severity/status, revision and permitted actions |
| GET `/incidents/{id}/occurrences`, `/recoveries`, `/history`, `/evidence` | Member/Admin | Bounded linked facts, without flattening their entity identities |
| POST `/incidents/{id}/status` | Member/Admin, supported policy | Accepted manual workflow command with expected workflow revision and reason |
| GET `/profiles`; GET `/profiles/{id}/versions`; GET `/profiles/{id}/versions/{n}` | Member/Admin | Published version metadata/rules; server-path fields only for Admin |
| GET/PUT `/profiles/{id}/draft` | Admin | Existing Profile draft read/edit with expected draft revision |
| POST `/profiles/{id}/validate` | Admin | Pure validation of the exact draft; no activation |
| POST `/profiles/{id}/simulations`; GET `/profiles/{id}/simulations/{reportId}` | Admin | Bounded sample test and immutable report/provenance |
| POST `/profiles/{id}/publish` | Admin | Publish exact current draft and reviewed complete sample report |
| POST `/profiles/{id}/activate` | Admin, supported policy | Existing fenced activation with expected activation revision/active version |
| GET `/search`; GET `/evidence/{id}` | Member/Admin | Persisted evidence search and bounded detail with interpretation links |
| GET `/monitoring`; GET `/monitoring/applications/{id}` | Member/Admin | Sanitized current diagnostics; Admin-only detailed fields |
| GET `/operations/{operationId}` | Submitting actor or Admin | Current authorized outcome of an admitted request |
| GET `/audit` | Admin | Paged access/administrative audit, no secret/raw payload dump |
| GET `/health/live`; GET `/health/ready` | Minimal public liveness / operator policy | Process liveness; authenticated dependency readiness |

No generic “execute engine command,” ingest line, AdvanceTime, create session, raw SQL, download arbitrary path, reset cursor, clear gap or collector-upload endpoint. No account/admin UI is included. OpenAPI and test HTTP clients exercise the contracts.

## Dashboard and distinct facts

Use one short read-only repeatable-read PostgreSQL transaction for dashboard counts, recent rows and required source/health observations. Capture one server `asOf` and the established reporting timezone; new query code must not call separate store helpers that each open their own snapshot and label the combined result coherent. Read persisted facts under that transaction and reuse accepted projection logic/reference parity fixtures. Do not hold the transaction while clients paginate or perform unrelated work.

Return:

- `asOf`, `reportingDate`, `reportingTimeZoneId`, metric policy identifiers and coverage/lag information.
- `systemHealth`: successful evaluated runs, total evaluated runs, nullable percentage. The accepted `MetricCalculator` uses **completion event date** for System Health; both finalized Application Runs and completed Order Runs count independently. Success counts successful, Failure/Undefined unsuccessful; zero denominator returns null. Independent diagnostic Incidents do not silently change a Success result's contribution.
- `completedOrderRunsProcessedToday`: logical completed Order Runs only, by **completion-processing date**; no raw lines, incidents or parent cycles.
- `fiveDayCompletedOrderHistory`: exactly five oldest-to-newest reporting-date buckets, same completed-order processing definition, plus coverage qualification. A zero is not a claim that an unobserved source was healthy.
- `unresolvedIncidentCount` (Active + Investigating), highest unresolved eligible severity, recent Incident rows (which can include Resolved history), and per-application state.
- Application `businessHealth` (Stable/Warning/Error or null), separate `availability` (accepted enum), `healthyNow`, last successful read/run, deferred deadline/lag qualifiers. A disabled/stale/unobserved source never gains an unqualified green state through this API.

Time roles remain explicit: source timestamp text, normalized event instant/quality, processing instant, completion event instant, completion-processing instant, nullable actual DB commit instant, acknowledgment time if distinct, and server reporting date/timezone. Read the existing persisted timezone rules for facts; do not silently replace them with viewer timezone or reinterpret history after an OS timezone change. If active sessions disagree with the configured reporting zone, surface a reporting-configuration conflict instead of merging unlike daily buckets. A timezone migration/reprojection policy is outside this phase.

Team aggregation uses composite durable identities (team/session/application/run), not bare fixture RunId strings, which can repeat across sessions. Exclude simulator/test sessions from production dashboard/Search by default; join the existing persistent monitoring-session registration. Investigation of explicitly selected archived registered monitoring data remains team-scoped. Cross-application percentages use summed numerators/denominators, never averages of percentages.

Current application state is reconstructed from unresolved Incident facts plus accepted availability observations. Query caches/DTOs have no authority to finalize runs, auto-resolve incidents or edit contribution facts. Search snippets and Home row labels are display projections; return their entity IDs and relationships rather than pretending each raw entry is an Incident.

## Search API

Phase 5 search defaults to **committed raw/normalized evidence**, matching the approved Search screen's Application/Time/Message semantics without designing its UI. An evidence row appears once even if it links to several runs/occurrences. Return evidence ID, application/Profile, original message or bounded snippet, event timestamp/quality, processed timestamp, receipt disposition, interpreted version and links. Raw, normalized, Application Run, Order Run, Incident and Occurrence IDs remain distinct.

Filters: application/Profile, explicit time basis (`event` or `processed`), half-open time range `[from,to)`, literal message substring, exact extracted order identifier, result, classification and linked incident status. Result/classification/status filters have explicit existential semantics over linked facts; none changes what an evidence row represents. Return filter metadata describing this and default to processed time so quarantined/unparsed records remain findable. Ignore/unmatched records stay searchable unless an explicit filter excludes them. Event-time filtering excludes missing/unparseable event instants and states that fact.

Default range: current reporting day. Proposed maximum interactive range: 31 days; users can request older bounded windows, not unbounded history. Text limit 256 characters; minimum three characters for substring searches, with exact identifier lookup for shorter IDs. Case behavior must be explicit (`caseSensitive=false` default for literal text); identifier normalization is the accepted Profile rule, not a global uppercase transform. SQL is parameterized and LIKE wildcard characters are escaped as literals. No user regex, executable expressions, full-text extension or Elasticsearch in this increment.

Exact identifier filters require an explicit Profile/identifier namespace and compare persisted interpreted identifiers. Do not re-extract old identifiers using the currently active Profile. Repeated identifiers across cycles yield distinct Order Run attempts with their parent cycle/version; they never become one globally grouped transaction.

Stable keyset ordering: selected timestamp plus immutable composite evidence key. Cursor binds the filter and sort. Each page is a committed snapshot; multi-page browsing is not promised to be one long historical snapshot. New/late commits can appear on refresh; the cursor contract must not claim a captured `asOf` timestamp excludes transactions that commit later. No exact unbounded total-count query; use `hasMore`. Incident status filters can change between pages and the response documents that live behavior.

Execution timeout (proposed five seconds), date/page/body limits, explicit timeout response, and query-plan evidence bound expensive text scans. Do not return partial results as complete or silently drop a filter. Add core indexes only with separately reviewed measured need; an unsatisfied latency target is a reported limitation, not permission to replace persistence.

## Profile management and simulation

Existing `IConfigurationStore.EditDraftAsync` remains draft authority. Each validation/simulation is bound to Team/Profile/draft revision/Profile hash, sample hash, explicit sample date/timezone, policy revision and engine build. The web sample is data, never instructions. No arbitrary executable/path/URL sample sources; accept bounded pasted text or a bounded explicit upload. Store preview provenance in access metadata; do not fabricate a published core Profile Version just to save a preview.

Draft DTOs expose configurable interpretation fields only. Team/Application/Profile ownership is resolved from the authorized route; mismatching ownership fields, internal IDs or acquisition credential/path mutations are rejected rather than passed into a deserialized domain object.

Use the existing simulator/PolicySession, including LegacyV1 semantics and revision-2 command behavior. A read-only sandboxed local simulator process using the existing executable is proposed to enforce a hard preview time budget (30 seconds, one concurrent job) without changing the engine; temporary input/output is private and bounded, and no source file access/DB credentials are supplied. No remote collector or background distributed job service is involved. Timeout/oversize/incomplete report blocks publication and remains explainable.

The report exposes Application/Order Runs, attempt/cycle grouping, identifiers, Success/Failure/Undefined, winning/suppressed rules, severity, exact Problem Identity/episode, Occurrences, Recoveries, workflow, metrics, pinned versions and diagnostics. An edited draft makes an old report stale. Publication requires the current exact draft, a complete matching report and explicit acknowledgment of warning IDs; errors or changed hashes block it. Call the existing `PublishAsync` with the same frozen simulation request. Its own validation/re-simulation remains authoritative; do not replace that check with a web checkbox. Bound and concurrency-limit publication too; if safe limits cannot be met without changing the core adapter, report the limitation for approval.

Publication does not activate. Supported revision-2 activation goes through local fenced admission and existing compatibility checks. Return active/pinned version distinctions, not a promise that every open run changes version. LegacyV1 live activation/workflow unsupported by that adapter is a documented 409 capability result. Public new-Profile/session provisioning and source-path edits are excluded as described in scope.

## Fenced commands, audit and retry

1. Authenticate, authorize the team/resource/action, validate body limits/preconditions, and bind `operationId` to actor/team/action/target/semantic payload hash in `sonda_access`. Write append-only admitted intent before any core side effect. If this fails, do not dispatch.
2. Enter the host's bounded per-application admission coordinator. Do not hold a DB transaction while waiting. Recheck authorization before unadmitted execution, acquire/use the **existing local owner**, recover its earlier pending input, and read the relevant current revision. If another process owns the application, return owner unavailable; never steal its fence to serve HTTP.
3. Resolve existing core receipt/pending reservation for this operation before constructing a new command. Assign sequence and nondecreasing processing time server-side at actual admission. Use `operationId` as the core command identity where the existing adapter supports it; `Actor` comes from stable account identity and `Reason` from validated user input.
4. Submit through `PostgresIngestionStore.SubmitControlAsync` for supported live status/activation. It freezes the command at reservation and applies the accepted engine/receipt transaction. Never call the internal revision-test primitive or update Incident SQL from a controller.
5. Record the authoritative receipt/outcome in access metadata and return it. If COMMIT succeeds but the response/access-outcome write fails, retry/poll consults that same core receipt and repairs access outcome; no duplicate effect. If no core receipt exists but a pending reservation does, recover that exact envelope, including its original time/sequence. If neither exists, the command was not core-admitted; a currently authorized retry may allocate a fresh sequence/time for the same semantic operation. A speculative pre-reservation allocation is not an immutable engine fact.

Do not replay a stale envelope after unrelated processing advances the sequence. The coordinator serializes admission with file/clock work, while PostgreSQL fences/locks remain the cross-process safety boundary. No unlimited in-memory channel. Proposed bounded wait: five seconds before returning capacity/unavailable; dispatch that is already admitted is not rolled back merely because an HTTP client disconnects.

For draft/publication, reuse existing configuration command IDs/fingerprints and core `command_receipts`. For account changes, operation/audit/result share the access transaction. Every response distinguishes acknowledged commit, durable rejection and uncertain outcome; `202` is allowed only when a durable operation/pending fact can be polled, not as a promise that a new autonomous queue will execute work. No generic queued work system is added.

Access audit records original actor, authorization revision, semantic action/target, expected and resulting revision, core receipt reference and timestamps. Recovery appends a reconciliation event; it does not forge an original successful completion timestamp. Audit intent and core commit are separate transactions by design. An incomplete access outcome must remain visible until reconciliation proves committed/rejected/unadmitted; it cannot be labelled success from HTTP arrival alone.

Manual status changes affect workflow/current unresolved health only, preserving engine-defined reopen/episode/recovery semantics and historical metric contributions. The endpoint supplies `allowedActions` and policy revision so clients do not hardcode rules that differ by Profile semantics.

