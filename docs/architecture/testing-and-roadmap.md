# Engine validation and implementation roadmap

Status: [Phase 1 scope](phase-1-scope.md) approved and implemented; 42 automated cases pass. See [actual Phase 1 results](../phase-1-review.md). The broader matrix below includes later-phase planned acceptance criteria and must not be mistaken for executed persistence, monitoring, or website tests.

## Test philosophy

Prove the interpretation model before connecting to GiroSol. The engine takes explicit input and time, produces explainable domain changes, and is deterministic under the same Profile, evidence sequence, policy, and clock events. Tests assert final records, intermediate state transitions, evidence links, history, and metric facts—not just strings appearing in a UI.

Use synthetic fixtures first. Every fixture contains a versioned Profile, original log bytes with encoding/date context, ordered append/rotate/restart/clock actions, and independently reviewed expected outcomes. Neither CAM phrase recognition nor fixture-specific logic belongs in the engine.

Simulation uses the production Domain/Application path with a fake clock, synthetic source adapter, and isolated database/session. Fast-forward time explicitly. Never sleep for real production timeouts. The fixture runner outputs a structured report explaining parsed fields, matched alternatives, selected rules, correlations, lifecycle transitions, incidents, automatic resolutions, and health/volume contributions.

Simulation is a first-class draft workflow, not just automated unit testing. A human must be able to submit sample logs before publication/activation and inspect runs, extracted IDs, Success/Failure/Undefined results, severities, Incidents and repeated Occurrences, and recovery. Phase 1 uses isolated in-memory adapters and reports; later phases add persistence and the visual paste/upload/review workflow. All use the same engine contract. Reports identify draft/sample hashes, priorities, winners and suppressed matches, warning coverage, and simulated clock/timezone; changed drafts invalidate prior report applicability.

## Required business scenarios

| Test | Inputs | Required assertions |
| --- | --- | --- |
| B01: failed order, successful cycle | CAM begin; order 93822 begin/connection/failure; cycle success | One Success cycle, one Failure order, one Active Warning; CAM Warning; 1/2 health-success units |
| B02: successful later retry | B01 then next cycle with order 93822 success | Two separate attempts; original failure unchanged; warning automatically Resolved with resolving-run link; CAM Stable if no other problems; total health 3/4 |
| B03: application failure/recovery | Failed cycle then successful cycle | Error incident first Active then automatically Resolved; two preserved cycles; 1/2 health-success units |
| B04: incomplete order at parent end | Order begins, no order outcome, cycle ends | Order Undefined, configured Error/Warning, incident Active, unsuccessful unit; no Undefined workflow status |
| B05: unfinished parent | Cycle/order begins, no reliable closing boundary | Both initially Open; no global timeout. Only explicitly configured fallback timeout/grace may finalize under the implemented Profile policy; future restart test does not invent success |
| B06: numeric specification example | 980 successful orders, 20 failures, 5 successful cycles, 1 failed cycle | Numerator 985, denominator 1006, display 97.91% at two decimals |
| B07: same ID across Profiles | CAM/93822 fails; UNITELLER/93822 succeeds | No cross-Profile recovery or evidence grouping |
| B08: interleaved orders | A begin, B begin, A continuation, B success, A failure | Correct separate attempts/evidence; B cannot resolve A |
| B09: alternative patterns | Different Begin/Success alternatives for same configured rules | Equivalent events; exactly one effect when several alternatives match one rule |
| B10: contains/exact/case/regex | Selected parsed message and raw field variations | Explicit match semantics; unsupported regex diagnosed; ID case unaffected by match-case setting |
| B11: Ignore diagnostic | Normal retry message matching Ignore | Match auditable; no incident or negative health caused by that match |
| B12: priority conflict | Generic Error plus higher-priority specific Ignore | Ignore wins regardless of severity; overlap warning lists all matches and winner/suppressed rules; reverse priorities to prove selection follows configuration |
| B13: manual status | Failure → Investigating → Resolved | Actor/history retained; failed run and health contribution unchanged; current state follows approved D06 |
| B14: repeated failure and recovery | Same unresolved scoped problem fails in two attempts, then succeeds later | One Incident with two failed Occurrences, separate successful recovery record, Resolved status, all attempts/evidence preserved; different identities stay separate |
| B15: out-of-order recovery | Older-cycle success arrives after a newer failure | Newer failure remains unresolved; old occurrence may recover only under approved policy |
| B16: explicit End | Begin; Success marker; End | Open until End, then Success; incomplete child sweep at End |
| B17: conflicting explicit-end outcomes | Begin; Success evidence; Failure evidence; End | Approved D03 conflict result, one cycle, all evidence preserved |
| B18: source parsing | Timestamp/level/message extraction; level ERROR without a detection rule | Parsed data correct; no incident inferred just from the level name |
| B19: Profile publication | Old cycle, new draft, queued activation, clean boundary, new cycle | Old version remains pinned; cutover audited; no retrospective changes or duplicate raw capture |
| B20: source/Profile disable | Disable one source, then Profile | Other source continues after single-source disable; Profile disable stops all new capture; history remains |
| B21: health vs diagnostic result | Success marker with independent Warning/Error | Assert approved D04 separately for terminal result, current incident, and metric contribution |
| B22: same-cycle retry | Failure attempt then explicit new Begin and Success | Distinct attempt numbers; recovery only if approved; no overwritten failure |
| B23: overlapping structural/filter match | One Order Failure line also matches its Warning filter | Same target/condition produces one occurrence with both rule matches preserved |
| B24: diagnostic outside cycle | Application-scoped Error rule matches before any Begin | Approved D14 creates evidence-linked incident, no synthetic cycle or health denominator unit |
| B25: ambiguous priorities | Enabled rules at equal priority | Unrelated predicates/targets may share priority; proven overlap or actual same-input/target collision is invalid; unproven regex intersections are warnings, not blanket rejection |
| B26: simulation revision | Test draft, then edit a pattern or priority | Report retains old provenance and is stale for publication of the edited draft |
| B27: completed-order-only volume | Many raw lines, cycles, incidents, and two completed Order Runs | `completedOrderRunsProcessedToday` is 2; count neither raw lines nor cycles/incidents; Open orders add zero |
| B28: processing date and replay | Yesterday's terminal event first processed today; repeat same completion receipt/fact | Today's processed-order bucket gets one; original event date preserved; replay/rebuild adds no count |
| B29: mixed-order successful cycle | One successful parent containing two successful orders and one failed order | 1 Warning Incident; exact failed-order problem key; Logs Today 3; System Health evaluates all four runs independently, 3/4 = 75% |

The additional specification's 10:00/10:05 CAM trace should be a dedicated fixture. Its time-only timestamps receive an explicit fixture date so the parser does not depend on the day the test runs. Expected state after the first cycle is Warning and after the later retry is Stable, assuming no other unresolved conditions.

## File, restart, and idempotency scenarios

- Write one byte at a time, including CR/LF separation, UTF-8 splits, supported UTF-16 alignment, BOM, empty lines, and a final unterminated fragment.
- Repeated watcher notifications and deliberately missed notifications produce the same captured physical records after reconciliation.
- Kill the process before/after each capture transaction, normalization commit, interpretation commit, and UI-command response. On restart, compare to uninterrupted baseline.
- Rename rotation while the old handle remains open; daily new-file discovery; replacement with same name; truncation; fast truncate/regrow with explicit gap limitations.
- Same message text at two distinct offsets remains two raw entries; same physical record redelivered remains one committed interpretation.
- Simulate a disconnected share, permissions loss, database unavailability, full storage, and a missing rotated file. No cursor advances past uncaptured bytes; loss is reported.
- Two monitors compete for one source; stale ownership token cannot commit. Two workers deliver one normalized record; one receipt and one set of effects result.
- Persist a multiline frame across restart. Change Profile framing only at an approved boundary without splitting an entry.
- Bound oversized/malformed records and regex timeout inputs; preserve evidence and diagnose instead of exhausting memory or silently returning no match.
- Exercise backlog before deadline firing. Captured earlier Success must win over a timeout caused solely by engine lag.

Use actual PostgreSQL for constraints, row-lock races, transaction rollback, timezone queries, and unique-index behavior. EF in-memory substitutes are not sufficient for these tests. Use real temporary Windows files for append/rename/sharing tests; SMB behavior requires an approved test share before production, not an assumption from local-disk success.

## Metrics and time tests

Assert both canonical arithmetic examples, zero denominator, one application with much larger volume than another, no averaging of percentages, and no metric changes from manual resolution. Test cycle/order counts separately to prevent accidental double insertion while preserving the intentional parent+child formula.

Test just before/at/after midnight, spring/fall daylight-saving boundaries, a run spanning midnight, a prior-day retry, late historical capture, date-less timestamps, and source timezone different from the server reporting timezone. Different viewer browser timezones must return identical team totals. Five-day series has exactly five ordered date buckets with coverage flags.

Change the simulated server timezone: a projection rebuild must switch atomically without mixed buckets. Delete/rebuild only derived projections in a test environment and compare counts against authoritative run facts. Disabled applications retain earlier contributions under the proposed policy.

## Authorization and API tests

Test bootstrap admin setup, disabled public registration, invitation expiry/single use, member activation, password reset/session invalidation, CSRF protection, login lockout, and last-admin protection. Verify the approved Admin/Member capability matrix server-side.

Create two teams in tests even if the first deployment uses one. A member cannot retrieve another team's Profile, incident, raw evidence, export, simulation, or account list by guessing IDs. Database constraints reject cross-team joins. Background jobs must not lose tenant scope. Test concurrent workflow edits and retries with the same command ID.

## UI verification after engine gates pass

Use API fixtures from the tested engine. Compare Home at 1366 × 736 against the unchanged approved image. Check semantic values, hierarchy, layout, colors, accessible state descriptions, empty/stale data, and real long names. Exercise Profile visual forms, parsing preview, alternatives, priority explanation, and immutable-version publication. The form never stores new rules merely because a preview ran.

Tests should verify the actual website flows; no native shell or per-user installer is part of the accepted deployment interpretation. Do not redesign the Home Page during implementation.

## Phased roadmap and exit criteria

| Phase | Work | Exit criteria |
| --- | --- | --- |
| 0. Architecture decisions and Phase 1 approval | Overall direction and five decisions are approved; review the bounded Phase 1 scope and preserve remaining policy questions | Explicit Phase 1 scope approval before any implementation, as currently requested |
| 1. Domain foundation and Profile simulator | Minimal .NET projects, typed domain/configuration, priority matching/validation, serial-cycle/order core, occurrences/recovery, fake-clock sample harness, explainable reports and core metrics; details in Phase 1 scope | Approved boundary/priority/repeated-failure/completed-order scenarios pass; sample report is reproducible; unresolved policies diagnosed, no live sources or production infrastructure |
| 2. Durable persistence — accepted | Bounded [scope](phase-2-scope.md), [schema](phase-2-schema.md), [transactions](phase-2-transactions.md); persistence adapter around accepted engine, synthetic inputs only | Existing 42 tests and real-PostgreSQL parity/crash/replay/constraint/projection tests pass; development restore works; stop for review |
| 3. Complete interpretation behavior | Both cycle completion modes, order grouping, configured classification, deadlines, workflow, scoped recovery, metrics | All approved business and time scenarios pass; explainability report traces every incident and contribution |
| 4. File acquisition | Discovery, read-only tailing, byte cursors, framing, rotation, reconciliation, backpressure | Filesystem/restart matrix passes on local files and intended share type; detectable gaps reported |
| 5. Shared API and accounts | Identity, bootstrap admin/member flow, team authorization, audited commands, query snapshots | Account/isolation/race tests pass; no unauthenticated access to business data |
| 6. Profile configuration and investigation website | Visual editor and first-class sample paste/upload/simulation/report-review workflow before publication/activation; incident detail/evidence/history | Admin reviews exact version's runs/results/IDs/severities/incidents and overlap warnings before activation; edited drafts invalidate old reports; member permissions enforced |
| 7. Approved Home integration | Exact Home composition connected to real read models, five-day chart, current-state list | Visual comparison passes and all cards/table match authoritative engine facts |
| 8. GiroSol shadow pilot | Approved sanitized copies first, then read-only actual logs; measure volume/rotation/IDs/timeouts; compare expected outcomes manually | No unexplained grouping/recovery; agreed capacity/lag/retention; service restart/restore tested |
| 9. Operational release | Service installation, HTTPS, monitoring diagnostics, rollback/backup runbooks, admin handoff | Approved deployment with supportable recovery and documented limitations |

The simulation harness and 42 passing tests are now accepted by the user. The approved Phase 2 increment is implemented; see [results](../phase-2-review.md). Phase 3 needs approval. No scaffolding overlap authorizes excluded work; later-phase rows remain planned work.

## Pilot and capacity evidence

Before phase 8, collect application cadence, peak bytes/lines per second, typical/maximum entry size, daily file volume, concurrent orders/cycles, rotation method, retention window, timestamp formats, and identifier reuse evidence. Set an agreed processing-lag objective from actual operator needs. Do not claim a fixed events-per-second capacity from the technology choice alone.

Run a sustained load above observed peak with simultaneous dashboard queries and incident edits. Measure backlog recovery, query latency, memory, storage growth, and raw-to-domain expansion. Test slow/broken regex separately. If performance is inadequate, profile the bottleneck before adding partitions, parallelism, a broker, or additional services.

Phase 1 delivered source, console simulation, synthetic fixtures, tests, and reports only and is accepted. No live GiroSol files, database, account system, monitoring service, website, deployment, or notifications were involved. Phase 2 is accepted. Phase 3 has a [bounded proposal](phase-3-scope.md); implementation has not started.

## Current roadmap checkpoint (2026-09-24)

Phase 4 is provisionally accepted: 309 passing tests, five environmental capability gates still pending. Phase 5 follows the original shared API/accounts milestone; its [bounded proposal](phase-5-scope.md) is documentation only, pending approval. All 309 tests remain regression gates. Phase 6 implements the approved Canva Home/Search visual reference; no frontend or collector is included in Phase 5.

