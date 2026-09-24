# Phase 3 approved acceptance matrix

Status: **approved acceptance requirements; executable coverage and measured results are mapped in [Phase 3 review](../phase-3-review.md)**. See [proposed policies](phase-3-scope.md). All fixtures use explicit synthetic inputs, policy revision 2 unless marked legacy, clock instants, stable command IDs and the monitoring-server reporting timezone. All named phrases below are fixture data, never engine constants.

Notation: `B(c)` parent Begin, `S(c)` Success, `F(c)` Failure, `E(c)` explicit End; `OB(c,x)`, `OS(c,x)`, `OF(c,x)` order Begin/Success/Failure; `D(c,key,severity)` diagnostic; `T(time,frontier)` clock advance. Times start at 10:00 on an explicit fixture date. Unspecified inputs advance by one second; test requests record exact event and processing instants. `H=s/n` counts successful/evaluated completions for the chosen reporting day; `V` counts completed orders processed that day. Recovery never removes a failed denominator unit.

## Business fixture specifications

| ID / proposed fixture | Input and configuration | Required assertions |
| --- | --- | --- |
| P3-01 `legacy-contracts` | All existing fixtures/profiles omit new policy revision | All 42+39 tests unchanged; deferred Ignore/Failure and old safe-boundary test still pass; old snapshot/report bytes and hashes unchanged |
| P3-02 `application-duration` | ExplicitEnd; expected 5m, grace 1m, fallback 10m; B(A); T at 6m then 11m with drained frontier | At 6m run remains open, Overdue only after threshold, no incident/contribution. At 11m Undefined, configured Error, H=0/1, V=0; effective deadline distinct from processing/commit |
| P3-03 `failure-before-missing-end` | B(A), F(A), no End, T(deadline) | Incident begins at Failure; parent remains open/no contribution until deadline; closes Failure, same occurrence, H=0/1 |
| P3-04 `success-without-end` | B(A), S(A), no End, T(deadline) | Undefined, observed Success retained; no invented End or Failure |
| P3-05 `incomplete-explicit-end` | B(A), OB(A,x), E(A), no terminal markers | Parent and order Undefined; severity separately configured (e.g. parent Error/order Warning); H=0/2, V=1; two outcome problems |
| P3-06 `timing-disabled-and-invalid` | No fallback; B(A), EOF and later clock. Separately negative duration, grace alone, timeout below expected, missing required event anchor | No fallback means no forced closure; invalid Profiles cannot publish; missing clock anchor diagnosed; processing anchor only used if explicitly configured |
| P3-07 `order-fallback` | Parent remains open; order timeout 2m + grace 30s; OB(A,x), T at 2m30s | Order Undefined/Warning; one occurrence, H=0/1,V=1; parent still open. Later parent Success gives H=1/2,V=1 |
| P3-08 `boundary-before-timer` | Pending order; parent Success at exact deadline included in frontier, then T | Parent closure wins; child Undefined once, no duplicate timer effect. Variant OS at deadline before parent Success yields H=2/2,V=1 |
| P3-09 `backlog-frontier` | Success exists among declared but unprocessed earlier inputs; attempt T past deadline | Clock rejected/deferred without completion; consume success and retry T, no timeout. Duplicate T and restart preserve state |
| P3-10 `same-cycle-retry` | B(A), OB(A,x), OF(A,x), OB(A,x), OS(A,x), S(A) | Attempts 1/2 linked; first Failure preserved; one incident with failed occurrence and successful recovery; H=2/3,V=2 |
| P3-11 `open-duplicate-begin` | B(A), OB(A,x), OB(A,x) while first is open | Ambiguity diagnostic; no attempt2 and no business mutation from second Begin; duplicate committed command only returns receipt |
| P3-12 `correlated-overlap` | B(A),B(B); OB(A,x),OB(B,x); OF(A,x), OS(B,x); close both Success | Two parents and independent orders; B's overlapping success does not recover A; H=3/4,V=2; later new eligible retry can recover |
| P3-13 `invalid-correlation` | Correlated Profile lacks key extractor; alternatively routed inputs miss key, duplicate open key, or reuse finalized key without epoch | Publication/input rejection as applicable; no latest-cycle guess; partition-local failures do not stop a separately proven partition |
| P3-14 `serial-new-begin` | B(A),OB(A,x),B(B) | Default rejects without closure. Explicit CloseIncompleteAndStartNew closes A/child Undefined and starts B atomically; H=0/2,V=1 before B closes |
| P3-15 `explicit-end-conflicts` | B(A),S(A),F(A),E(A); reverse Success/Failure order variant | Failure both ways; one outcome occurrence; early Failure creates incident, End adds one completion; H=0/1 |
| P3-16 `same-input-contradiction` | One input matches both Success/Failure; separate case Application Begin+End | Whole input has diagnostic and no business mutation; contradictory configuration visible in simulation; no priority-based structural guess |
| P3-17 `failure-and-ignore` | Order Failure also matches specific priority50 Ignore and broad priority5 Error | Ignore wins classification; broad diagnostic suppressed; order still Failure with outcome Warning; explanatory trace; with successful parent H=1/2,V=1 |
| P3-18 `success-with-diagnostic` | B(A), diagnostic Error condition q, S(A) | Parent Success; q remains unresolved, current Error, H=1/1,V=0. Parameterize Warning and ManualOnly/NextSuccessfulRun; own run cannot recover q |
| P3-19 `later-clean-recovery` | After P3-18, later B(B), S(B), no q diagnostic | Eligible NextSuccessfulRun q resolves; ManualOnly q stays open. Variant q appears on B: cannot recover q. Other problem keys remain independent |
| P3-20 `manual-workflow` | Failure incident; Active→Investigating→Active→Resolved; separate Investigating→Resolved | Exact transition audit/actor/reason; no metric changes. Resolved→Active rejected; same status gives no-op receipt; empty resolution reason rejected; duplicate command no duplicate history |
| P3-21 `manual-then-success` | Failure, manual resolution, later eligible successful retry | Original manual resolution retained; one successful confirmation attached, no new episode/status transition. Further success/replay adds no recovery. ManualOnly variant has none |
| P3-22 `recurrence-episodes` | Failure, resolve (manual and automatic variants), later new attempt Failure | Same Problem Identity, episode2 new incident ID; episode1 unchanged; at most one unresolved episode. Old receipt retry/old occurrence enrichment cannot create episode2 |
| P3-23 `recovery-causality` | Success run begins before later failure; success completes afterward | Does not clear that failure. New attempt beginning after all covered failures may resolve. Exercise manually resolved old episode plus newer active episode independently |
| P3-24 `late-terminal` | Finalized Success, then correlated late Failure/Success | Original outcome, metric, completion and associations unchanged; late disposition retained; no synthetic attempt/automatic recovery |
| P3-25 `late-diagnostic` | P3-24 plus rule explicitly permits late Error observation | Independent evidence occurrence/current Error; unchanged H and V. Without opt-in no business incident; ambiguous target stays unlinked |
| P3-26 `open-out-of-order` | Terminal event predates run start; separate continuation after start but earlier than latest received event | Pre-start terminal quarantined; valid continuation follows processing sequence, does not rewrite earlier facts. Crossed frontier invokes late policy |
| P3-27 `pinned-version-transition` | Revision2 v7 B(A); activate compatible v8; v7 completes A (including new child after activation); v8 B(B) | A/children/deadlines remain v7; B uses v8; receipt retries keep routing/version. Correlated variant has both versions open concurrently |
| P3-28 `incompatible-transition` | Open run; change routing extractor/mode/source ownership; try activation | Drain-required, no mutation; activation after drain works. Legacy upgrade drains. Stale activation revision conflicts; no forced closure |
| P3-29 `versioned-recovery` | v7 failed x; v8 success x with matching compatible contract; then incompatible namespace/condition variants | Compatible success recovers old incident with both versions evidenced; changed identity does not. Same key plus incompatible recovery semantics rejected at publication/activation |
| P3-30 `outside-cycle` | Application-wide Error without B; repeat distinct input; duplicate first input | One incident/two evidence occurrences, no run, H denominator0,V0; Ignore creates none. Later explicitly eligible new cycle can recover; order-scoped rule without parent rejected |
| P3-31 `availability` | No observations; then completed Success; read success; clock advances beyond threshold; read failure; read success | No observations gives null business health/NotObserved. Empty read cannot prove run success. Stable+Fresh then Stable+Stale then Stable+Disconnected; recovery read alone does not clear overdue run cadence |
| P3-32 `required-sources` | Two required sources, one optional; missing/stale/failed/successful combinations | Required failure → Disconnected; missing required coverage → Unknown; expired required freshness → Stale; optional success cannot hide these; all configured requirements pass → Fresh |
| P3-33 `clock-and-date` | Source event yesterday, processing today; deadline across midnight and DST; delayed clock command tomorrow | Event/effective date drives health; completion-processing date drives V; five zero-filled ordered dates; source/normalized/processing/commit clocks never substituted. Source ambiguity diagnostics preserved |

## Cross-cutting execution requirements

Every accepted revision-2 business fixture runs through the same Application/Domain engine in the in-memory simulator and PostgreSQL harness. Compare traces, run versions/attempts/results, exact problem keys/episodes, occurrence subjects, histories/recovery coverage, deadline dispositions, availability and metrics. Restart between inputs/commands; replay all committed commands and verify unchanged authoritative row counts/facts. Rejected cases assert retained diagnostics and absence of business mutation, not merely an exception.

Add integration matrices beyond the business rows:

| ID | Required real PostgreSQL proof |
| --- | --- |
| DB3-01 | Upgrade from delivered Phase 2 schema with completed/open legacy runs, unresolved/resolved incidents and receipts; old hashes/reports/replay remain identical; all original39 tests pass on latest schema |
| DB3-02 | Rollback at evidence/run/incident/deadline/metric boundaries for each new command family; PostgreSQL constraint rejection produces no partial success |
| DB3-03 | COMMIT succeeds then child process exits before response for deadline, workflow and compatible activation commands; restart/retry returns receipt with no duplicate completion, occurrence, recovery, history or contribution |
| DB3-04 | Competing same-cycle Begin requests cannot allocate same attempt; same Problem Identity recurrence cannot allocate duplicate episode; correlated keys cannot collide across ownership |
| DB3-05 | Manual status vs automatic recovery race in both lock orders; no lost history/incorrect resolution kind; stale expected revision conflicts |
| DB3-06 | Evidence vs due timer race at declared frontier, activation vs Begin/completion race; one deterministic disposition and pinned version per command |
| DB3-07 | Direct SQL violates occurrence XOR subject, cross-team/parent version FK, duplicate recovery, immutable completed results or episode uniqueness: PostgreSQL rejects |
| DB3-08 | Rebuild business health, availability and all metric views from durable facts twice; no new contributions; legacy/new policy datasets both match simulator |
| DB3-09 | Dump/restore with concurrent correlated parents on v7/v8, pending deadlines and manually resolved episode awaiting confirmation; continuation equals uninterrupted execution |
| DB3-10 | Commit timestamp tracking on/off; actual commit metadata nullable without affecting semantics; schema model/migration consistency and fresh/repeated migration application verified |

Test counts are intentionally not predicted: parameterization and focused tests will determine the actual total. Completion requires every applicable scenario above and all existing81 tests, with no hidden skips, passing evidence and a reviewable explanation for any approved scope adjustment. No tests should require real time sleeps to cause a business timeout.
