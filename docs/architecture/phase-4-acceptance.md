# Phase 4 proposed acceptance matrix and completion criteria

Status: **approved acceptance requirements; local tests implemented and run**. See [review and coverage qualifications](../phase-4-review.md). Controlled SMB and installed-service/boot gates remain environmental blockers. Preserve all 237 accepted tests. All business comparisons use the existing engine and accepted revision-specific semantics. No mocked EF database is acceptable evidence of durable ingestion.

## Test layers

1. Pure byte/framing/identity-reconciliation fixtures with every split point for representative UTF-8/UTF-16 records, deterministic discovery manifests, explicit clocks and bounded memory.
2. Real local Windows temporary files and producer processes; actual rename/truncate/recreate/read-sharing races.
3. Real disposable PostgreSQL, process termination before/after COMMIT, concurrent owners, exact row/range/receipt counts, migration and dump/restore.
4. Real controlled SMB share under a dedicated test identity, including disconnect/reconnect/access denied. Transport fakes supplement these tests; they do not substitute for UNC capability proof. If unavailable, mark UNC verification blocked and do not claim full completion.
5. Disposable Windows service-host lifecycle test (install/start/stop/restart/boot under explicit test-machine authorization), no production deployment. Manual real-log comparison is a separately authorized pilot gate, not fabricated synthetic evidence.

Every successful file fixture exports original bytes, source manifest/configuration versions, generation/offset trace, immutable command envelopes, receipts, final facts and reconstructed report. Replay those exact admitted commands in the simulator and compare interpretations, identifiers, attempts, episodes, recoveries, workflows, versions and metrics. Restart at each named boundary. Check offset equals the end of the longest contiguous committed record chain, not merely that duplicate counts look plausible.

## Required scenarios

| ID | Input/fault | Required evidence |
|---|---|---|
| P4-01 | Append after checkpoint; poll unchanged file repeatedly | Only appended bytes read (apart from bounded continuity samples); old receipts unchanged; one effect per record |
| P4-02 | Graceful restart and forced process exit | Same persistent session/generation; resume committed offset; no repeated historical scan |
| P4-03 | Exit after byte read, before reservation | No offset/evidence/effects; retry reads identical retained range |
| P4-04 | Exit after reservation, before domain transaction | Pending bytes, sequence and timestamps recovered exactly; offset unchanged until commit |
| P4-05 | Fault after raw evidence/runs/incidents/metrics/checkpoint update, before COMMIT | Full transaction rollback at every hook; pending preserved; successful retry one chain/effect set |
| P4-06 | Real deferred COMMIT rejection | Same atomic rollback; no separate checkpoint success; retry stable envelope |
| P4-07 | COMMIT succeeds, child dies before response | Receipt and offset already committed; restart lookup creates no duplicate runs/occurrences/recoveries/metrics |
| P4-08 | Duplicate byte read/request alias; changed bytes at same key | Exact duplicate returns prior receipt; conflicting content blocks, no second evidence or domain effect |
| P4-09 | Rename old file and create replacement; old handle still appended | Old identity/generation retained across aliases; drain proven predecessor; replacement distinct; restart finds retained archive |
| P4-10 | Truncate to zero/smaller length; then append | New epoch/generation; old checkpoint immutable; unread lost tail becomes gap, never green/complete |
| P4-11 | Copy/truncate with proven archive lineage | Copied prefix not counted twice; unread archive suffix recovers under old generation; new stream starts at zero |
| P4-12 | Unproven archive, identity reuse, reconnect changes identity; rapid truncate/regrow | No unsafe attachment/offset skip; explicit uncertainty/unsupported producer contract; no claim of impossible lossless guarantee |
| P4-13 | Daily files enumerated in shuffled order | Configured sequence/date contract orders generations; same serial cycles/report as fixture; ambiguous series rejected |
| P4-14 | Delete/recreate before old tail drained; archive unavailable | New identity distinct; missing old range retained as gap; frontier held |
| P4-15 | Partial final line, split CRLF, split character/code unit/BOM; restart | Complete record emitted once only after delimiter; byte offsets exact; volatile prefix reread without whole-file scan |
| P4-16 | UTF-8/BOM, UTF-16 LE/BE, LF/CRLF, blank lines | Correct decoded evidence and exact bytes; BOM counted in range, not message; same interpretation as accepted text fixture |
| P4-17 | Invalid bytes, BOM mismatch, over-limit line, sealed unterminated tail | Bounded memory; diagnostic and stalled range, no silent substitution/drop or synthetic completion |
| P4-18 | Local denied file, missing required file, inaccessible SMB share | Source operational failure only; no fabricated run Failure/Error or metric unit |
| P4-19 | Share recovers; required file returns, identity same/different | Reconcile identity before resuming; successful-read observation; retained backlog/gaps not erased |
| P4-20 | Hours of retained backlog with active appends | Bounded visits/memory; contiguous processing; lag decreases; source fairness; timers wait |
| P4-21 | Two owners contend; old owner stalled during lease expiry/reclaim | One accepted fence epoch; stale owner cannot reserve/commit/checkpoint/emit timer; pending work adopted safely |
| P4-22 | Two sources, same Profile, shared identifiers/correlated cycles | Per-source order and durable admission sequence preserved; no latest-cycle routing; unsupported cross-source causality rejected |
| P4-23 | Overlapping globs, hard-link/path alias, second Profile subscription | One canonical physical owner or explicit conflict; no double interpretation |
| P4-24 | Watcher overflow/missed events; reconciliation enumerates change | Watcher not authority; bounded metadata reconciliation finds retained generations/append |
| P4-25 | Timer due while earlier success is unread/partial/pending/on unavailable source | No AdvanceTime until complete certificate; earlier success interpreted first; no false Undefined |
| P4-26 | Idle EOF/no producer watermark, large observed event timestamp | Deadline deferred with reason, regardless of elapsed wall time; no inferred completeness |
| P4-27 | Valid manifest/watermark across all sources, boundary-time success | Verify every declared byte range/sequence then clock; existing tie behavior and immutable deadline context preserved |
| P4-28 | New generation/source membership/rotation gap appears before clock commit | Invalidate stale certificate; no unsafe timer; certificate consumption and receipt atomic |
| P4-29 | Duplicate scheduler command, restart, post-COMMIT crash, later contradictory event | One closure/contribution; late policy preserved; producer-contract violation suspends new frontiers |
| P4-30 | Source observations: none, read success, stale, failure, recovery; optional/required/disabled | Existing NotObserved/Unknown/Fresh/Stale/Disconnected semantics; empty read not business success; metrics unchanged |
| P4-31 | Compatible Profile activation during backlog/open cycle; pending command | Pending commits before activation; old runs/children retain version, new runs use active version; receipt retry unchanged |
| P4-32 | Encoding/root/required-source revision change, disable/re-enable | Existing generations keep config; checkpoint retained; old completeness obligations not erased; unsafe live change blocked |
| P4-33 | Time-only timestamps/date context; midnight/DST; OS clock moves backward | Explicit source date/zone required; processing monotonicity and all timestamp roles remain separate; no server-date guess |
| P4-34 | Graceful service stop during read/transaction; boot/service recovery | Bounded cancellation, no partial commit; recover pending/checkpoint; no browser dependency |
| P4-35 | Database outage, low storage, long record | Bounded backpressure, no offset advance/data purge, lag reported unknown where unmeasurable |
| P4-36 | Direct SQL checkpoint jump/backward move, wrong receipt/source/team, overlapping range | PostgreSQL rejects invariant violations; triggers active and runtime role least-privileged |
| P4-37 | Phase 3→4 migration, reapply, old profiles/receipts | Exact three-migration chain under specific approval; all 237 gates; prior hash/report/facts unchanged |
| P4-38 | Dump/restore with open runs, pending record, checkpoint and frontier proof | Same remaining-file continuation as uninterrupted control; missing external bytes become gap rather than guessed progress |
| P4-39 | Replay entire file/redeliver all committed records twice; rebuild projections | One physical evidence/receipt, same domain facts and metric counts; checkpoints unchanged |
| P4-40 | Dedicated account accesses allowed root and denied/out-of-root/reparse path | Read-only source operation; no credential/raw-content leakage in routine logs; boundary validation enforced |
| P4-41 | Accepted mixed-order fixture ingested as real split byte writes | Successful parent, independent failed/successful children, exact incidents/recoveries, Logs Today and System Health match accepted engine |
| P4-42 | Counter/capacity limit and conflicting configuration | Stop with actionable diagnostic, no integer wrap/session reset or silently weakened contract |

## Completion criteria and review artifacts

Phase 4 implementation may be presented as complete only when:

- All 237 regression tests pass. No accepted business assertion changes; only the exact migration-chain extension if specifically approved.
- Applicable P4-01–42 scenarios pass with real Windows files/PostgreSQL, plus controlled SMB and service lifecycle evidence. Report environmental blockers honestly; do not replace real tests with mocks and claim completion.
- Crash-before/after-COMMIT evidence proves the contiguous checkpoint invariant and no duplicate domain effects. Tests measure read byte ranges and memory limits, not just final report equality.
- All supported encodings/rotations, source observations, fenced ownership, backlog, pinned routing and certificate-gated deadlines match the same engine/simulator.
- Forward migration/fresh-schema SQL, unchanged old migration hashes, seeded upgrade, model consistency and restore evidence are produced.
- Source manifest validation and the one-source read-only pilot checklist are ready. Actual GiroSol pilot results are reported separately as passed/pending/blocked depending on access authorization and manual review; no source access is implied by implementation approval.
- Deliver `docs/phase-4-review.md`, test TRX/JSON results, byte/checkpoint/crash traces, simulation-versus-ingestion reports, ownership/frontier diagnostics, service/SMB capability results, limits and rollback/recovery instructions.
- No auto-purge, frontend, accounts UI, HTTP API, notifications, billing, remote collector or production rollout is included. Stop for review before any later phase.

This matrix is the approved acceptance target. Local implementation/testing is recorded in the review; no production service installation or real GiroSol source connection has occurred.

