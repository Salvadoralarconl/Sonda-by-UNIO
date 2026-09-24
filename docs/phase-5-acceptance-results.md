# Phase 5 acceptance and permission map

Read with [delivery review](phase-5-review.md) and [runtime limits](development/shared-backend.md). Final case names, outcomes and TRX files are enumerated in `artifacts/phase5/test-summary.json`. This map distinguishes new boundary tests from the unchanged engine/acquisition tests reused as the behavior oracle. It is not SMB/SCM/boot/ACL capability certification.

## HTTP permissions

All routes default to authentication. Authorization resolves a current durable session and enabled membership on every request; no team parameter can broaden it. Every unsafe method requires antiforgery and an acceptable Origin when supplied. Unknown body/query authority fields are rejected.

| Surface | Access | Additional condition |
|---|---|---|
| Public liveness, CSRF token, login, grant redemption | Anonymous | Only minimal liveness; credential endpoints rate-limited; login/redemption require CSRF |
| Own session, logout/all, own password | Member/Admin | Current password for change; revocation on change/logout |
| Dashboard, applications, published interpretation, activation state, runs, Incidents, evidence, Search | Member/Admin | Team/actual parent/session join; registered monitoring sessions only for operational evidence |
| Incident status | Member/Admin | Supported revision-2 lane, reason, expected workflow revision and operation UUID |
| Monitoring | Member/Admin | Member projection excludes configured paths/host authority and detailed acquisition fields |
| Member list/invite/role/disable/reset/invitation replacement | Admin | Current team; last-Admin guard; expected revision for edits; secrets shown once only |
| Application/Profile/source provisioning and source read/configure | Admin | Allowed-root configuration; inactive initial Draft; live changes fenced/drained |
| Draft read/edit, validation, simulation/report, publication, activation | Admin | Exact revision/provenance and applicable version preconditions; no automatic publication/activation |
| Audit, dependency readiness, OpenAPI | Admin | No secret/raw payload dump in administrative audit |
| Operation result | Submitting actor or current Admin | Same team, matching operation identity; read reconciliation cannot dispatch commands |
| Filesystem browser/read, checkpoint/generation/gap/force-read/delete/rename/credentials | Nobody | No routes exist |

OpenAPI captures all exposed paths and the cookie/CSRF/Admin requirements. Endpoint enumeration tests verify anonymous denial across protected routes and Member denial across all Admin routes, including new provisioning and source configuration. Foreign/missing configuration and session queries, cross-team parent commands, bound cursors, spoofed authority and current-membership admission races are separately exercised.

## Approved matrix coverage

| Proposal gate | Evidence |
|---|---|
| P5-01 | All 309 unchanged; original 237 identifiable; verifier checks 123 original hashes |
| P5-02 | Access migration chain/reapply and populated Phase 4 upgrade fingerprint tests; separate histories |
| P5-03 | Concurrent bootstrap test; local-only command entry point; no HTTP/bootstrap default password |
| P5-04 | Concurrent redemption, replacement/expiry/hash-only grant tests |
| P5-05 | Durable lockout/generic failure and actual credential rate-limit HTTP tests |
| P5-06 | Cookie attributes, idle/absolute expiry, logout and real HTTPS encrypted-key restart/restore |
| P5-07 | Password change/reset, disable-cookie failure and membership-revision enforcement |
| P5-08 | Concurrent Admin demotion/disable preserves one enabled Admin; last-Admin HTTP conflict |
| P5-09 | Login/unsafe CSRF and cross-origin tests; reads do not dispatch business commands |
| P5-10 | Actual route metadata enumeration for anonymous and Member/Admin policy boundaries |
| P5-11 | Foreign/missing parent resources, foreign run sessions and team/filter-bound cursors |
| P5-12 | Strict JSON/query contract, spoofed team/authority rejection; server-built workflow actor/sequence/time |
| P5-13 | Revocation between cookie validation and admission cannot reuse stale tracked membership |
| P5-14 | Dashboard repeatable-read snapshot while accepted fenced input commits another successful cycle |
| P5-15 | Accepted mixed-order fixture: successful parent/independent failed order, 75%, three completed orders, one Incident |
| P5-16 | Late event/processing-day split, null empty health, five buckets and server-only timezone; unchanged accepted midnight/DST semantics remain regression-tested |
| P5-17 | Composite team/session/run joins, foreign-session rejection and exclusion of unregistered simulator data; no global bare-RunId grouping |
| P5-18 | Ignore Search and unchanged diagnostic/outcome/manual-recovery policy regression matrix; dashboard reads facts and unresolved Incidents independently |
| P5-19 | API projection of NotObserved/Fresh/Stale/Disconnected/disabled Unknown; accepted gap/deadline acquisition tests |
| P5-20 | Linked evidence filters, Ignore and quarantined evidence, null event timestamp exclusion and no metric creation |
| P5-21 | Literal injection/wildcard input, unknown regex query rejection, body/page/text/window limits |
| P5-22 | Once-per-evidence keyset pages; filter/team/tamper rejection; live-page semantics explicitly documented, with no captured cross-page snapshot promise |
| P5-23 | Actual PostgreSQL plan artifact and blocked-query timeout; bounded historical-window test |
| P5-24 | Concurrent draft edits have one revision winner; omitted precondition HTTP 428; conflict/replay checks |
| P5-25 | Immutable exact preview, replay after edit, stale report publication rejection and complete new-Profile lifecycle |
| P5-26 | Oversize/canceled sample, executable-looking text treated as data, process environment/path containment; unchanged regex bounds; no source/runtime effects |
| P5-27 | Simulated core publication COMMIT acknowledgment loss, operation reconciliation and duplicate immutable version prevention |
| P5-28 | Reserved file Begin recovers before API activation; old open cycle remains v1, next cycle is v2 |
| P5-29 | New concurrent workflow revision test plus unchanged PostgreSQL manual/automatic recovery lock-order tests; same admission gate serializes file and API commands |
| P5-30 | Concurrent identical provisioning, conflicting payload/actor reuse and workflow/source retry identities |
| P5-31 | Fault before reservation: durable intent but no phantom core effect; authorized retry |
| P5-32 | Fault after pending reservation/before interpretation: recover exact recorded command before next work |
| P5-33 | Fault after core COMMIT: poll/retry reconciles the committed receipt, one workflow history/effect; source and provisioning lost-ack tests |
| P5-34 | Standalone owner and mismatched host authority cannot be bypassed; original fence/checkpoint regression tests unchanged |
| P5-35 | Legacy Incident is readable, advertises no workflow actions and live mutation fails with explicit unsupported capability without changing the row |
| P5-36 | Admin/Member route tests, role-filtered diagnostic source inspection and safe exception/logging boundary review |
| P5-37 | Actual synthetic local file hosted worker, inactive-before-activation, browser-independent read, repeated visit without duplicate evidence/facts |
| P5-38 | Denied audit INSERT rolls provisioning back; ledger intent precedes core dispatch; crash receipt reconciliation completes without repeating a core effect |
| P5-39 | Actual pg_dump/pg_restore of combined data plus encrypted keys, core fingerprint equality and cookie rejection with unrelated keys |
| P5-40 | Original-source hash audit, OpenAPI route review and explicit exclusions; no frontend/collector/deployment/repair implementation |

This matrix does not claim new independent load/fuzz/penetration coverage for every combination. Accepted interpretation/timing/recovery behaviors continue to be tested in their original suites, while the new suites focus on the HTTP/account/persistence/host boundary. Production volume, network-share identities and installed service lifecycle require their own later evidence.

## Admin provisioning amendment

All ten requested cases are covered by the new provisioning and HTTP tests: Admin creation; Member denial; cross-team denial; matching duplicate replay; conflicting duplicate rejection; outside-root rejection; valid local configuration; syntactically allowed UNC without share access; inactive Draft until validation/simulation/publication/activation; preservation of historical data. Additional tests cover concurrent duplicates, COMMIT lost acknowledgment, stale source revisions, immutable published source configuration and the core-data upgrade/restore fingerprint.

No general provisioning CRUD or filesystem control was added. Source IDs and team authority are server-owned. Initial activation creates only the required new durable configuration/runtime records; it never rewrites accepted historical interpretations.
