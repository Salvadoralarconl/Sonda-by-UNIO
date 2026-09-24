# Phase 5 proposed acceptance, exclusions and completion criteria

Status: **approved design, implemented in Phase 5; see [delivery review](../phase-5-review.md) for results and exact delivered limits**. Parent: [scope](phase-5-scope.md); [security](phase-5-security.md); [API contracts](phase-5-api.md).

## Test strategy

All **309 Phase 1–4 tests**, including all **237 original regression cases**, remain unchanged and must pass. The accepted core three-migration assertion also remains unchanged because access migrations have a separate context/history. Record original source/migration hashes before implementation; compare them at delivery. No core semantic or persistence-model modification is authorized by this proposal.

Use the actual ASP.NET Core middleware/endpoints via WebApplicationFactory/TestServer plus real disposable PostgreSQL for Identity/access stores, tenancy joins, conflicts, receipts and queries. Add a small real HTTPS loopback/Kestrel test for cookie/transport behavior that an in-process test client cannot prove. No EF in-memory substitute for persistence or authorization race claims. HTTP tests are not a replacement for blocked SMB/service capability tests.

Use two independent teams with deliberately colliding human names, Profile IDs and source identifiers, multiple users/roles, known denied IDs, LegacyV1 and revision-2 Profiles, and synthetic monitored sessions registered through existing procedures. Include separate simulator sessions to prove they do not leak into operational dashboards. Host/command integration uses real synthetic files and the accepted application owner; tests never access a GiroSol source.

Existing fixtures are the oracle for run/incident/metric behavior. New API tests compare complete semantic DTO data against durable facts and accepted projections; they must not make endpoint implementation details their own expected truth. Evidence/source timestamps and request authentication clocks remain explicitly injectable test inputs where appropriate, without modifying core time rules.

## Required acceptance matrix

| ID | Scenario | Required result |
|---|---|---|
| P5-01 | Full accepted suite and source audit | 309/309 pass unchanged; core 001–003/snapshot unchanged; 237 subset identifiable |
| P5-02 | Fresh access schema, repeated migration, existing core database | Separate access history; core chain and prior rows/hashes unchanged; no hidden history edits |
| P5-03 | Bootstrap first Admin twice/concurrently | Exactly one valid bootstrap; no default/shared credential or HTTP bootstrap bypass |
| P5-04 | Invitation valid/expired/replaced/used concurrently | One redemption; correct preassigned team/role; replacement revokes prior grant; no secret retrieval |
| P5-05 | Login, failed login, lockout/rate limit | Generic failure shape; configured lockout/rate policy; no user enumeration or secret logging |
| P5-06 | Cookie, idle/absolute expiry, logout/restart | Secure/HttpOnly/host-only attributes; expiry and revocation enforced; protected keys persist across restart |
| P5-07 | Password change/reset, disable, demotion | Existing session fails next authorization check; no stale-role access; historical actor retained |
| P5-08 | Concurrent demotion/disable of two Admins | At least one enabled Admin; stale revision rejected and account/audit transaction rolls back together |
| P5-09 | CSRF and cross-origin requests | Unsafe requests missing valid token rejected before mutation, including login/logout; GET has no business mutation |
| P5-10 | Anonymous and wrong-role access across every route | Deny-by-default; no forgotten raw evidence, simulation, diagnostic, audit or operation endpoint |
| P5-11 | Guessed IDs/cursors/parent-child joins across teams | No body, count, snippet, permission or existence disclosure; same missing/foreign 404 behavior |
| P5-12 | Spoofed team/actor/role/sequence/fence/processing time | Client cannot set internal authority or processing context; stable server account actor in workflow audit |
| P5-13 | Account disable races command admission | Declared linearization holds; already core-admitted command retains actor; unadmitted request cannot bypass current permission |
| P5-14 | Dashboard concurrent commits | Related metrics/rows describe one DB snapshot; no mixing helper-created transactions |
| P5-15 | Accepted mixed-order successful-parent fixture | Separate parent/child outcomes, one failed-order Incident, Logs Today 3, System Health 75% |
| P5-16 | Late processing, midnight/DST, zero denominator, browser timezone | Event-date health vs processing-date volume; null empty health; five correct buckets; team totals independent of viewer zone |
| P5-17 | Colliding RunIds in separate application sessions; simulation data present | Composite scoping prevents collapse; only selected registered monitoring data contributes |
| P5-18 | Success with independent diagnostic, manual resolution, Ignore cases | Existing engine metric/incident distinctions remain; no HTTP-specific health rule |
| P5-19 | Unobserved/stale/disconnected/disabled/gap sources | Last business state plus honest availability; no invented healthy status or business Failure |
| P5-20 | Search raw/normalized/linked entities, Ignore/unmatched/quarantined data | One evidence row; explicit link/filter semantics; no conversion of raw entries into run/Incident counts |
| P5-21 | Literal wildcards/injection/long query/unsupported regex | Parameterized literal search; enforced caps; no SQL/regex execution or filesystem read |
| P5-22 | Search pagination under new/late commits and status changes | Stable keyset semantics; filter-bound tamper-resistant cursor; no false cross-page snapshot claim |
| P5-23 | Search bounded historical windows / query timeout | Limits and plans documented; honest timeout, no truncated response masquerading as complete |
| P5-24 | Concurrent draft edit and stale expected revision | One revision winner; no silent overwrite; retry ID bound to same semantic request |
| P5-25 | Exact draft simulation, edit after preview, mismatched report | Existing engine result/provenance; stale/mismatched/incomplete report cannot authorize publication |
| P5-26 | Malicious/oversize sample, costly regex, canceled preview | Text remains data; no arbitrary files/processes; bounded preview isolation; no domain/source effects |
| P5-27 | Publication duplicate/COMMIT acknowledgment loss | Existing immutable version and config receipt reused; no duplicate version, no implicit activation |
| P5-28 | Pending file input then activation during an open run | Pending admitted first; existing version pinning/compatibility unchanged; old/new versions visible correctly |
| P5-29 | Manual status and automatic recovery race | Existing engine/revision winner; conflict explained; no duplicate history/recovery, no metric rewrite |
| P5-30 | Same operation ID twice/concurrently/different body/actor | Same authorized result for matching semantic intent; conflicting reuse rejected; no cross-actor receipt leak |
| P5-31 | HTTP crash before core reservation | Audit intent is not success; currently authorized retry can allocate valid current sequence; no phantom business effect |
| P5-32 | Crash with existing core pending command | Recover its exact envelope/time/sequence before unrelated API command; no new command identity |
| P5-33 | Core COMMIT then crash before access result/HTTP acknowledgment | Receipt proves result; replay/poll reconciles audit; one business effect/history/contribution |
| P5-34 | Stale/lost owner or concurrent standalone worker | HTTP cannot steal ownership or bypass fence; no endpoint issues a deadline or checkpoint mutation |
| P5-35 | LegacyV1 reads, unsupported live mutation | Unchanged legacy facts; explicit capability response instead of test-primitive bypass or semantic upgrade |
| P5-36 | Diagnostics visibility and SQL/secret failure paths | Member-safe vs Admin detail enforced; no credentials/raw content/stack paths in routine logs/errors |
| P5-37 | API/browser unavailable while worker runs | Direct acquisition continues independently; API read outage doesn't advance/drop bytes or fabricate outcomes |
| P5-38 | Access audit failure / domain receipt uncertainty | No dispatch before durable intent; committed core fact never rolled back/repeated because access result failed |
| P5-39 | Backup/restore access+core+key material | Account/membership/session policy preserved; core hashes/checkpoints intact; unavailable keys force safe reauthentication rather than bypass |
| P5-40 | Boundary review | No frontend, collector, source repair/control backdoor, business model rewrite, notifications, deployment or fake environmental pass |

## Explicit exclusions

- No Canva frontend, Home/Search rendering, Profile form editor, login/account/admin pages, CSS, component library or visual redesign. Phase 6 uses the existing Canva source of truth.
- No remote collectors, upload-as-production-ingestion API, microservices, Redis, brokers, Elasticsearch, Kubernetes or new distributed infrastructure.
- No modifications to domain/acquisition models, core tables/migrations, checkpoint identity/transactions, deadline proof rules, interpretation, recovery/episode policy, reporting-date semantics or accepted assertions.
- No automatic retention/purge, arbitrary reprocessing, force-resolution of detected outcomes, direct checkpoint/gap repair or general-purpose SQL/file access.
- No public team/organization registration, user-created allowed filesystem roots, source credentials or arbitrary source repair/control. The approved narrow Admin provisioning amendment is included; general provisioning CRUD is not.
- No unsupported live LegacyV1 workflow/activation route, implicit policy upgrade or use of internal revision-test primitives.
- No SSO/OIDC, MFA, service-to-service OAuth/API keys, public SaaS billing, email sending, notifications, saved-search product feature, export jobs or live push/WebSockets.
- No real GiroSol pilot, positive SMB claim, service installation, boot tests or production deployment without their separate environment/access authorization.

## Completion and review package

After approval and implementation, stop only when the applicable new gates and all 309 regressions pass, or report a concrete blocker without claiming completion. Deliver:

1. Frozen OpenAPI/DTO contracts and permission matrix, including unsupported capability responses and bounded query semantics.
2. Source/file list and proof that existing core/test hashes and migration chain were preserved.
3. New access-schema SQL and fresh/upgrade/reapply/restore evidence, with no core persistence redesign.
4. Real PostgreSQL + HTTP account/authorization/isolation results, revocation/race evidence and cookie/CSRF checks.
5. Dashboard/Search/report parity, date/health semantics and query-plan/limit measurements.
6. Fenced manual workflow/activation, audit-intent/receipt reconciliation and crash-before/after-commit evidence.
7. Bootstrap, credential/key recovery and same-server worker composition runbook; no public default credentials.
8. Known limitations and unchanged status of all five Phase 4 environmental gates.

No exact new test count is promised before implementation; tests must cover these behaviors rather than inflate counts. Approval of this proposal would authorize only this bounded backend increment. It would not certify Phase 4 environmental capabilities, authorize deployment or start Phase 6.

