# Architecture decisions and approval register

Status: **Phase 1 implementation accepted; its 42 passing tests are the reference contract. Phase 2 accepted; all 81 Phase 1/2 tests remain regression gates. Phase 3 proposal prepared, implementation not authorized**. Monitoring, authentication, website implementation, deployment and Phase 3 remain unauthorized. See [Phase 2 results](../phase-2-review.md).

## Confirmed user decisions

| ID | Confirmed decision | Source |
| --- | --- | --- |
| C01 | Approved Home Page image and `visual-design-guide.md` are permanent visual references; do not redesign Home | Direct user request |
| C02 | SONDA is a website with an admin and individual member accounts; configure logs once per team | User clarification after the initial deployment question |
| C03 | Team members share configuration and results under the shared website model | Direct team configuration requirement; shared data model is the proposal implementing it |
| C04 | “Today” uses the monitoring server's computer timezone so team totals agree | User's explicit timezone answer |
| C05 | Identifiers are scoped to their application/Profile; each Order Run occurrence belongs to its Application Run Cycle | User answer and additional parameter specification |
| C06 | All log-format knowledge comes from Profile parameters, including parsing and markers | Additional parameter specification |
| C07 | Profiles have multiple independently enabled Log Sources | Additional parameter specification |
| C08 | Support terminal-marker completion and independent Application Run End completion | Additional parameter specification |
| C09 | At parent-cycle completion, an order without Success/Failure becomes Undefined and creates an incident; Undefined is inferred | Additional parameter specification |
| C10 | Undefined Order Runs count as unsuccessful in System Health | Additional parameter specification §30 |
| C11 | A new cycle's matching successful order is a new occurrence; it may resolve the previous failure without overwriting it | Both specifications |
| C12 | Manual workflow changes never change Detection Result | Both specifications |
| C13 | Prefer deterministic cycle boundaries. At cycle close, unfinished orders become Undefined. Profile timeouts/grace periods are fallback mechanisms when a reliable boundary is unavailable; application expected-duration/grace rules are profile-specific, never one global business timeout | Approved pre-implementation decision 1 |
| C14 | Detection classification uses explicit priority; the highest-priority matching rule wins, including intentional Ignore overrides. Validation/simulation warns about overlapping active matches; severity is not a conflict-resolution order | Approved pre-implementation decision 2 |
| C15 | Repeated failures of the same unresolved problem append Occurrences to one Incident. A later successful retry resolves it while preserving every failed occurrence and the successful recovery event; different problems remain separate | Approved pre-implementation decision 3 |
| C16 | Home Logs Today counts completed logical Order Runs processed today only; excludes raw lines, incidents, and application cycles. Internal metric name: `completedOrderRunsProcessedToday` | Approved pre-implementation decision 4 |
| C17 | Profile simulation is a first-class pre-publication/activation workflow: sample logs must explain runs, extracted identifiers, outcomes, severities, and incidents before live use | Approved pre-implementation decision 5 |
| C18 | Overall architecture direction approved; implementation remains paused pending review of the revised summary and Phase 1 scope | Current direct user request |
| C19 | Phase 1 implementation authorized; equal priorities invalid only for active rules competing on the same input/target; unrelated rules may share priority | Subsequent Phase 1 approval, amendment 1; supersedes C18's pause |
| C20 | Problem Identity / Incident Key is an explicit exact domain concept; simulation shows the calculated key deciding new Incident versus appended Occurrence | Phase 1 approval, amendment 2 |
| C21 | Acceptance fixture has multiple successful/failed orders in one successful cycle; independent outcomes, incident, completed-order volume, and separate cycle/order health units are verified | Phase 1 approval, amendment 3 |
| C22 | Phase 1 implementation and 42 passing tests sufficiently demonstrate approved behavior; preserve them throughout later phases | User acceptance of Phase 1 |
| C23 | Prepare bounded Phase 2 durable-persistence proposal only: PostgreSQL/EF adapter around existing engine, transactions, real disposable database tests; no implementation before approval | Current explicit request |
| C24 | Preserve distinct source event time, processing time, database commit time and server reporting dates; modular monolith without speculative infrastructure | Current explicit request |
| C25 | @Canva → Sonda Home Page Design, all pages, is permanent primary visual authority; no generic dashboard replacement or visual reinterpretation; explain technical conflicts before changes | Subsequent Canva designation; [review record](../reference/canva-approved-design.md) |
| C26 | Page 1 is Home; Page 2 is Search | User clarification, verified against both rendered Canva pages |
| C27 | Expanded Phase 2 proposal must explicitly include Profile lifecycle, admin/activation concurrency, parsing/identifier configuration and normalized/completion-processing timestamps; still no implementation | Subsequent bounded proposal request |

The earlier response “a separate desktop installation for each user” was clarified by C02. The final proposal is a shared website; a native shell, per-user agent/database, and browser-local metric timezone are not the proposed architecture.

## Approved overall architecture direction

One Windows-hosted .NET 10/ASP.NET Core backend, PostgreSQL 18, EF Core/Npgsql, React/TypeScript/Vite website, ASP.NET Core Identity with team memberships, and a file monitor independent of browser sessions. Use a modular monolith with a deterministic engine and durable transactional checkpoints. Exact package patches and server installation details are chosen at foundation setup.

The first implementation targets one team and one designated monitoring host with access to its logs. Database ownership includes team IDs from the start; this does not imply a public multi-tenant SaaS rollout. Additional collectors, notifications, AI, and broad analytics are deferred.

## Decision disposition and remaining detail

The five decisions below are no longer open questions. Rows marked approved retain only specifically identified follow-up details; other rows remain proposals unless separately confirmed. Overall direction approval does not silently approve every earlier edge-case recommendation. Production durations and source details still require real samples.

| ID | Decision/question | Recommended proposal | Why it matters / implementation gate |
| --- | --- | --- | --- |
| D01 | Where will the shared monitor run, and can it read the GiroSol locations? | Windows service on a team-designated host; local/UNC read-only access; no per-browser reads | Verify OS, service identity, paths, network reachability before the real-log pilot; do not deploy yet |
| D02 — approved policy | Boundary-first incomplete-run handling | Unfinished orders become Undefined at cycle close. Use configured timeout/grace only without a reliable boundary; support application-specific expected duration/grace, not a global timeout | Actual durations, incomplete-application outcome/severity, and production timing remain Profile-specific follow-up work; boundary-first behavior is approved |
| D03 | How should explicit-end outcome conflicts and repeated order attempts behave? | ExplicitEnd mode: Failure evidence wins over Success at End; no evidence → Undefined. Same-line contradictory terminal match → diagnostic. Explicit new Begin after a finalized same-cycle order creates a new attempt and can resolve an earlier attempt if approved | Optional End is required; conflict precedence and same-cycle retries are not fully specified |
| D04 — priority approved | Competing detection classifications | Highest configured priority wins for the same input/target, regardless of Error/Warning/Ignore; warn on overlaps and show suppressed matches | Replaces the old exclusive-group design that could let a lower-priority rule survive. Remaining separate question: interaction of an independent structural Failure with a winning Ignore, and health contribution of terminal Success plus its own diagnostic problem; do not hardcode an answer |
| D05 — approved | Repeated failures and recovery | One Incident per unresolved scoped problem, with separate failed Occurrences and preserved successful recovery event; different problem identities remain separate | Core grouping/retry policy is approved. Late/out-of-order recovery remains D10; new failure after resolution is still proposed as a new episode |
| D06 | What is the effect of manual resolution and reopening? | Manual resolution removes the problem from current health but never from historical health. Initially support Active ↔ Investigating and either → Resolved; defer manual reopening, with recurrence opening a new episode | Manual result preservation is confirmed; exact transition permissions/current-state effect need approval |
| D07 — Logs Today approved | Completed Order Run activity | `completedOrderRunsProcessedToday`: count one first committed completion-processing fact per logical Order Run, including finalized Success/Failure/Undefined and retries; no raw lines, incidents, or application cycles | “Processed today” uses the monitoring server's date of first committed completion processing, separately from log event time. Earlier event-time-only volume recommendation is superseded. Other System Health policies (Success plus diagnostic, incomplete cycles, disabled-app scope) remain distinct proposals |
| D08 | Can an ID represent unrelated orders within the same Profile? | Never global identity. Match unresolved earlier failures by Profile/identifier namespace and later cycle. Preserve cycle-specific attempts. Verify reuse before production; if ambiguous, require a configured discriminator or restrict auto-recovery | Confirmed scope does not prove uniqueness forever inside that scope; false recovery would hide real problems |
| D09 | Do cycles overlap, or do multiple files contain interleaved parts of one cycle? | Serial stream mode unless a reliable cycle correlation key/order policy is configured. Proposed unexpected Begin closes previous serial cycle Undefined then starts the next. Reject unsupported merged-source configurations | Cannot safely guess association; verify with samples before selecting mode |
| D10 | What should late evidence do after a run is finalized? | Preserve/flag it without rewriting the finalized result; a properly detected later attempt may recover an incident. Record partial occurrence recovery without clearing newer failures | Protects historical consistency; late corrections/reprocessing remain explicit |
| D11 | What may team members change? | Admin manages accounts/configuration/sources. Members view evidence and update incident status; all changes identify the actor | Accounts and one-time team configuration are confirmed; exact permissions are not |
| D12 | Where should first ingestion start, and how long is evidence retained? | Require explicit first-read choice per source. No automatic purge until retention is approved; source logs must survive expected downtime/backlog | Backfill affects metrics/storage; retention affects recovery and history |
| D13 | How should unobserved/disconnected applications appear? | Preserve last-known Stable/Warning/Error with separate availability/staleness metadata; no unqualified green state before observation | Avoids presenting missing evidence as health while preserving the Home layout |
| D14 | What happens when a diagnostic Error/Warning matches outside a detected cycle? | Explicit application-scoped rules can create evidence-linked incidents without a fabricated run; current health changes, evaluated-run counts do not. Order-scoped rules require reliable order association | The specification permits configured conditions but does not define out-of-cycle association/recovery; approve this boundary |
| D15 — approved workflow | Simulation before publication/activation | A draft can be tested with sample logs using the same engine; show exact parsing, identifiers, rule matches/winner, cycles, orders, results, severities, incidents/occurrences/recovery, and overlap warnings | Phase 1 supplies the engine/report contract and runnable sample harness; later website work supplies the visual editor and review/publish flow. Persist report provenance; editing a draft makes prior results stale |

## Operational recommendations that do not change business meaning

- Persist raw bytes, normalized evidence links, run facts, and workflow history with integrity constraints.
- Do not use file notifications as the sole source of truth; reconcile and checkpoint byte positions.
- Use bounded regex and decoding; a parser failure is a monitoring issue, not inferred business success.
- Use source event times with quality flags; server reporting timezone does not dictate how every source timestamp is parsed.
- Use repeatable-read dashboard snapshots and transactional per-application state transitions.
- After an OS timezone change, audit and rebuild date projections consistently from UTC facts.
- Add security and team-scope tests before exposing the shared website to members.

## Approval record

| Date | Scope | Status |
| --- | --- | --- |
| 2026-09-23 | Visual baseline preserved | Approved by user |
| 2026-09-23 | Shared website, team account model, reporting timezone, scoped IDs | Confirmed user requirements; see C02–C05 |
| 2026-09-23 | Additional Profile parameter requirements | Received and incorporated; see C06–C12 |
| 2026-09-23 | Proposed stack, schema, processing policies, roadmap | **Awaiting approval** |
| 2026-09-23 | Subsequent user approval of overall architecture direction | **Approved in principle**; supersedes the preceding overall-direction status, not every outstanding edge case |
| 2026-09-23 | Five pre-implementation decisions: boundaries, priority, occurrences, Logs Today, simulation | **Approved and incorporated**; see C13–C17 and D02/D04/D05/D07/D15 |
| 2026-09-23 | Implementation / Phase 1 | **Historical gate: awaiting approval at that time**, superseded below |
| 2026-09-23 | Subsequent Phase 1 approval with three amendments | **Implementation authorized within Phase 1 only**; source/tests/reports must be reviewed before Phase 2 |
| 2026-09-23 | Phase 1 delivery | **Implemented; stopped for user review**. See [source/tests/report results](../phase-1-review.md). Phase 2 has not started |
| 2026-09-23 | Phase 1 acceptance | **Accepted by user**; preserve the 42-test reference contract |
| 2026-09-23 | Phase 2 proposal | **Documentation only; awaiting scope approval**. See [scope](phase-2-scope.md), [schema](phase-2-schema.md), [transactions](phase-2-transactions.md) |

Subsequent user approval: **Phase 2 implementation authorized**, with open-run activation rejection explicitly temporary and COMMIT-success/process-crash-before-response/restart-retry explicitly required. Deployment, notifications and Phase 3 remain unauthorized. Future sessions must preserve this bounded approval and the 42-test reference contract.

## Approved Phase 2 decisions

| ID | Proposed decision | Boundary |
| --- | --- | --- |
| P2-D01 | Separate EF storage entities; explicit interpreter export/restore and typed change-set seam shared by simulator and durable harness | Preserve existing behavior/tests; no engine rewrite |
| P2-D02 | One input and all effects commit under application runtime lock, with receipt/evidence deduplication and first-completion facts | Fresh context/state after rollback or uncertain commit |
| P2-D03 | Immutable exact Profile snapshot plus sealed rules; publication provenance and activation history | No configuration UI or historical reinterpretation |
| P2-D04 | Activation only at closed-cycle boundary; compatible identity/recovery settings while problems remain unresolved | Reject unsafe change rather than invent mid-cycle/cross-version policies |
| P2-D05 | Nullable actual database commit instant; distinct insertion and acknowledgment observations | Optional commit tracking enriches audit; no fabricated commit time |
| P2-D06 | Canonical completion facts retain event/processing clocks/context; health uses event date, Logs Today processing date | Preserve Phase 1 arithmetic; rebuild without new facts |
| P2-D07 | Real disposable PostgreSQL constraint/crash/race/migration/restore tests plus unchanged 42-test baseline | No mocked database proof, production deployment or monitor |
| P2-D08 | Bigint incident revisions and atomic history/command receipt primitives | Manual workflow policy and user-facing commands remain Phase 3/later |

P2-D01–P2-D08 are now approved for Phase 2, subject to the two amendments above. P2-D04 is a temporary safeguard, not permanent business policy. Outstanding D02–D15 business details are not implicitly approved by adding storage columns.

Phase 2 delivery: implemented and stopped for review. 42 Phase 1 plus 39 PostgreSQL tests pass; the PostgreSQL suite also passes with commit tracking disabled. Both approved amendments are verified. See [review and limitations](../phase-2-review.md). Phase 3 remains unauthorized.

## Phase 2 acceptance and proposed Phase 3 decisions

The user accepted Phase 2's implementation/evidence and explicitly requires all Phase 1/2 tests to remain regression gates. The user authorized a bounded Phase 3 proposal only, with no implementation. These supersede the earlier Phase 2 delivery review gate. Existing behavior stays in force until a new policy is approved and explicitly selected.

The following decisions are **proposed, awaiting approval**, not confirmed user requirements. Full rationale and edge cases: [scope](phase-3-scope.md); [persistence impact](phase-3-persistence.md); [acceptance fixtures](phase-3-acceptance.md).

| ID | Proposed decision | Prior decision addressed |
| --- | --- | --- |
| P3-D01 | Explicit revision-2 policy opt-in; legacy snapshots/tests retain their accepted behavior | C22, Phase 2 acceptance, no historical reinterpretation |
| P3-D02 | Missing completion Undefined; observed Failure remains Failure; profile-specific timing/severity, no global timer | D02/D03 |
| P3-D03 | Explicit frontier-driven order fallback to Undefined; separate synthetic deadline time from raw/processing/commit clocks | D02/D07 |
| P3-D04 | Explicit new Begin after finalization creates linked same-cycle attempt; causally later success may recover | D03/D08 |
| P3-D05 | Overlap only with reliable exact correlation; serial rejection or explicitly configured incomplete closure | D09 |
| P3-D06 | Same-input contradictions block; separate explicit-End Failure dominates Success; immediate occurrence precedes final contribution | D03 |
| P3-D07 | Ignore suppresses diagnostic classification only; independent structural Failure, outcome Incident and unsuccessful metric remain | D04 |
| P3-D08 | Success stays Success despite independent diagnostic; current health can remain Error/Warning, no self-recovery | D04 |
| P3-D09 | Four specified manual transitions, no reopening; revision/actor/reason/idempotent audit; historical metrics unchanged | D06 |
| P3-D10 | Later eligible success can confirm manual resolution without rewriting it; recurrence creates separate episode under same exact key | D05/D06/D10 |
| P3-D11 | Finalized results immutable; late evidence retained separately, only explicit diagnostic rules create business incidents | D10 |
| P3-D12 | Compatible activation pins open parents/children to original version; stable routing contract; incompatible changes drain | P2-D04 temporary safeguard |
| P3-D13 | Explicit application-wide out-of-cycle diagnostics create evidence occurrences without runs or metric units | D14 |
| P3-D14 | Separate business health from source freshness/availability; no unqualified green from absent evidence | D13 |

No production monitoring, real scheduler, frontend, accounts UI, API, notifications, deployment or new infrastructure is included. Actor authorization, real file delivery frontiers and production source guarantees remain future work. D01/D11/D12 and other operational questions are not implicitly resolved by this proposal.

Phase 3 approval: user explicitly approved P3-D01 through P3-D14, compatibility, additive persistence and acceptance sequence. Implementation is authorized within Phase 3 only. Preserve all 81 existing assertions unchanged; stop and report any legacy conflict. Phase 4 remains unauthorized.

Phase 3 implementation conflict: accepted migration test requires exactly one applied migration; approved additive migration requires two. Work stopped per user instruction. See [checkpoint](../phase-3-progress.md). Original test sources remain unchanged; no implicit approval to modify the assertion.

Migration gate resolved by explicit user approval: only the migration-count expectation may change to exact ordered IDs 20260924023552_DurablePersistence and 20260924032911_InterpretationPolicies. All other assertions remain unchanged. An explicit delivered-v1-data upgrade test is required. Phase 3 work resumes.

Phase 3 delivery for review: approved P3-D01–P3-D14 are implemented only for explicit revision-2 Profiles. All 237 tests pass, including the original 81; all 119 PostgreSQL tests also pass with commit tracking off. The scope_guard defect is fixed. The exact-chain assertion is the sole authorized original-test edit, verified by source hashes. Two v1-schema/data upgrade variants, 41 memory/PostgreSQL fixture comparisons, crash/race/restore and migration/model checks pass. Concrete storage representation is recorded in phase-3-persistence.md; final results and limitations are in ../phase-3-review.md. Implementation is delivered, not yet accepted by the user. Phase 4 remains unauthorized.

## Phase 3 acceptance and proposed Phase 4 decisions

The user accepted Phase 3 and its 237-test verification. All 237 tests are regression gates. The preserved request is `docs/reference/phase-4-request.txt`. Only a Phase 4 proposal is authorized; no implementation, migration, service installation or real-file connection has been performed.

| ID | Proposed decision, awaiting approval | Reason / boundary |
|---|---|---|
| P4-D01 | Same modular monolith; Windows worker uses existing engine and shared PostgreSQL unit of work | No second engine, HTTP API or distributed infrastructure |
| P4-D02 | Commit physical evidence, interpretation receipt/effects and contiguous byte checkpoint in one transaction | Offset never outruns safely committed evidence/effects |
| P4-D03 | One bounded durable pending command per Application before interpretation | Stable bytes, processing timestamp, sequence and fingerprint across pre-COMMIT crashes |
| P4-D04 | One fenced Application owner on a single host; stable monitoring session across restarts | Prevent stale/duplicate workers from publishing; reuse existing application lock |
| P4-D05 | Physical file identity plus persisted generation/epoch, separate from path | Rename continuity, truncate/replacement separation, no content-based duplicate guessing |
| P4-D06 | Verify archive lineage for copy/truncate; reject uncertain identity/gaps | Arbitrary overwrite/delete histories cannot be made lossless by a read-only tailer |
| P4-D07 | Strict byte framing, UTF-8/UTF-16, bounded partial tails, no live-EOF flush | Preserve exact byte checkpoints and avoid interpreting incomplete records |
| P4-D08 | Per-source/proven generation ordering and durable cross-source admission order | No invented temporal order or latest-cycle heuristic; correlation alone is not causality |
| P4-D09 | Separate caught-up byte scan from semantic completeness; no deadline without proof | EOF/quiet periods cannot prove no earlier evidence will arrive; default clock deferral |
| P4-D10 | Producer watermark/coverage contract generates durable frontier certificate; scheduler submits existing AdvanceTime | Existing business deadlines remain deterministic; backlog/source gaps hold timers |
| P4-D11 | Operational source failures drive availability observations only | No fabricated application Failure/Incident or metric effect |
| P4-D12 | Immutable acquisition revisions, retained source obligations and checkpoints across disable/re-enable | No unsafe source/framing change or hidden completeness reset |
| P4-D13 | Read-only dedicated service account, least-privilege DB, no auto-purge | Preserve producer files and evidence needed for restart/recovery |
| P4-D14 | Additive migration 003; request narrow exact-three-chain assertion authorization | Original two migrations/history and all other regression assertions preserved |
| P4-D15 | Controlled local/SMB/service acceptance, then separately authorized one-source pilot | Do not claim real UNC/service/GiroSol verification from mocked or unavailable environments |

Full proposal: [scope](phase-4-scope.md), [schema/transaction/interfaces](phase-4-persistence.md), [42-scenario acceptance matrix](phase-4-acceptance.md). These acquisition policies were subsequently approved as recorded below. They do not change the accepted business interpretation contract.

Phase 4 implementation approval received: P4-D01 through P4-D15 and the acceptance sequence are approved. Preserve all 237 tests. The current-schema exact migration-chain assertion may extend to the actual generated migration 003 ID; preserve all other assertions and original migrations. No real GiroSol source access or Phase 5 authorization.

Phase 4 implementation record (2026-09-24): migration `20260924044645_FileAcquisition` and acquisition adapters are implemented around the accepted engine. Exact physical ranges, pending reservations, deferred COMMIT fencing, immutable acquisition revisions, explicit rotation lineage and proof-only deadlines are delivered. Earlier bytes discovered after an already committed successor fail safe as a gap. These are implementations of P4-D03–D12, not a new business-policy revision. The historical v1-to-v2 test still asserts two migrations and now explicitly targets migration 002 in setup; only the current-schema assertion extends to three. Source-hash evidence verifies no other accepted test changes.

Review boundary: controlled SMB/dedicated-account and installed SCM/boot capability tests remain environmentally blocked. Real console lifecycle and local synthetic/PostgreSQL results are documented in [Phase 4 review](../phase-4-review.md). Full capability sign-off and real GiroSol pilot remain pending; no Phase 5 authorization is inferred from implementation.

## Phase 4 provisional acceptance and Phase 5 proposal (2026-09-24)

The user provisionally accepted Phase 4 and all **309 passing tests**, retaining the **237 original regression cases**. All 309 are now regression gates. The five pending environmental checks are controlled SMB under a dedicated identity, disconnect/reconnect/share identity, installed SCM lifecycle, boot/recovery, and service-account ACLs. They remain unverified; no mock can close them. Original shared-server/direct-file-monitoring/PostgreSQL/engine architecture is reaffirmed; remote collectors are explicitly excluded.

Only Phase 5 **proposal preparation** is authorized. [Request record](../reference/phase-5-request.md), [scope](phase-5-scope.md), [security](phase-5-security.md), [API contracts](phase-5-api.md), [acceptance](phase-5-acceptance.md). Phase 6 retains the Canva frontend; no implementation or design change now.

The following are **proposed for approval**, not approved decisions:

| ID | Bounded proposal | Reason / limit |
|---|---|---|
| P5-D01 | One shared ASP.NET Core host with existing in-process fenced worker | Direct local/UNC acquisition; no new remote service/collector |
| P5-D02 | Identity, secure cookie sessions, one team/account and Admin/Member policies | Team membership is server authority; no public registration/SSO/MFA in this increment |
| P5-D03 | Explicit local first-admin bootstrap; expiring invitation/reset grants | No default credentials/email integration; last-admin and revocation rules tested |
| P5-D04 | Separate access-only schema/context/migration history in existing PostgreSQL | Core model and exact three-migration regression stay unchanged |
| P5-D05 | Authoritative core receipt plus access audit/idempotency ledger | Existing adapters own transactions; recover acknowledgment loss without claiming nonexistent cross-context atomicity |
| P5-D06 | Supported live mutations through local owner/admission only | Never bypass fences or expose internal revision-test primitive; LegacyV1 unsupported mutations remain explicit |
| P5-D07 | Coherent dashboard and bounded evidence Search | Preserve actual event-date health, processing-date order volume and distinct fact identities |
| P5-D08 | Existing-Profile draft/simulation/publication/activation endpoints | New application/Profile/session provisioning and source controls are deliberately outside this bounded increment |
| P5-D09 | Bounded first-class preview using the existing simulator | Exact draft/sample/report provenance; stale reports cannot publish; no second interpreter |
| P5-D10 | Read-only role-filtered monitoring diagnostics | Operational states do not create business failures; no file-access/checkpoint repair HTTP backdoor |
| P5-D11 | All 309 unchanged plus HTTP/security/real-PG acceptance | Environmental Phase 4 gates remain separate; no frontend or rollout implied |

Approval is requested for the complete bounded proposal, including its explicit capability limits. Do not implement any P5 decision before that approval.

## Phase 5 implementation authorization and delivery (2026-09-24)

The subsequent user approval supersedes the proposal-only status above. P5-D01–D11 were approved, with P5-D08 expanded by the following narrow product amendment. Phase 5 is implemented for review; user acceptance of this delivery and Phase 6 approval are not implied.

| ID | Approved/implemented decision | Boundary |
|---|---|---|
| P5-D12 | Admin creates Application, initial inactive Profile Draft and allowed-root source configuration | Team/actor are authenticated; no public Team registration, general CRUD, credentials or arbitrary filesystem access |
| P5-D13 | Provisioning is transactional and idempotent; activation is explicit after validation/simulation/publication | No combined harness publication/activation; enabled configuration alone does not read logs |
| P5-D14 | Live source configuration uses existing owner/drained/generation guards with an atomic core command receipt | Added Infrastructure partial adapter only; all original core files/model/migrations/assertions unchanged; no repair/control exposure |
| P5-D15 | Activation precondition is the accepted revision-2 activation count | It is distinct from lane per-input revision; open runs retain their pinned version |
| P5-D16 | Two access-only migrations in `sonda_access`, separate history | Core three-migration chain unchanged; restricted runtime role and upgrade/restore evidence required |

See [delivery review](../phase-5-review.md), [operational/HTTP limits](../development/shared-backend.md), and [amendment](phase-5-provisioning.md). All 309 accepted tests and original 237 subset remain gates. The five Phase 4 environmental gates remain pending. Canva Home/Search authority is unchanged; no frontend/Phase 6, collectors, real GiroSol access, notifications or deployment was added.


## Phase 5 acceptance and Phase 6 proposal (2026-09-24)

The user explicitly accepted Phase 5 backend/security/provisioning and all 382 tests. All accepted behavior is now a regression gate. This supersedes the delivery-awaiting-review language above. The five Phase 4 environmental gates remain pending separately.

Phase 6 is documentation-only pending approval: see [scope](phase-6-scope.md), [frontend architecture](phase-6-frontend.md), [acceptance](phase-6-acceptance.md), and [verbatim request](../reference/phase-6-request.txt).

| ID | Proposed decision (not implementation approval) |
|---|---|
| P6-D01 | React/TypeScript/Vite, CSS Modules/tokens, existing same-origin cookie API; no second backend or client business engine |
| P6-D02 | Canva Page 1 Home/Page 2 Search fidelity gate, verified captures/assets, explicit conflict review; no redesign |
| P6-D03 | Bounded read-only Incident summary/time projection and parent-Run child filter, only if approved; no schema/policy changes |
| P6-D04 | Neutral greeting and explicit metric-badge/availability/filter interaction review rather than fabricated backend values |
| P6-D05 | Visual configuration and existing simulation/publication workflow, server-authoritative rules and eligibility |
| P6-D06 | Explicit operation reconciliation, role/session cache isolation, CSRF, bounded polling that pauses on inactivity |
| P6-D07 | All 382 unchanged tests plus frontend/security/real-PostgreSQL/visual acceptance; stop for review after Phase 6 |

## Phase 6 authorized implementation — latest authority (2026-09-24)

The user approved Phase 6 implementation with exact Canva fidelity and the two narrow read-only query additions. See docs/reference/phase-6-approval.txt. Preserve all 382 accepted tests and backend behavior. Frontend work is authorized; earlier proposal-only statements are historical and superseded. No Phase 7, deployment, real GiroSol, notifications, collectors or AI.

The user subsequently confirmed the CURRENT live Canva mapping: **Page 1 Home; Page 2 Monitoring; Page 3 Search**. These are three separate approved screens, not alternate Search designs. Earlier two-page mappings are obsolete. Match all three; preserve the original Home image as a historical unchanged reference. Current Canva API page dimensions are 1366×768; visible design content includes a 1366×736 region. Exact API element geometry is recorded in docs/reference/canva-phase6-elements.json. Do not stretch the earlier raster to resolve this difference.

Greeting: use authenticated account display/login name; no hardcoded Salvador. Morning/Afternoon/Evening follows SONDA reporting/server timezone. Preserve greeting typography, location and structure. The smallest read-only session name field is approved.

Home Error Logs Info opens a centered Incident modal over Home: visible background with slight blur/subtle darkening, white rounded shadowed panel, top-right X, narrow left sections Overview / Order-Run / Evidence / History and larger right detail. Preserve page scroll and return focus on close. Use backend allowedActions. Overview includes application, severity/status, summary, first-detected/last-updated, identifiers, linked runs/result/version; Order-Run includes parent, attempt/previous attempt/recovery; Evidence is chronological and grouped with explicit provenance; History preserves transitions/recovery/manual actors/times. Do not invent absent fields or group distinct facts implicitly.

Five Phase 4 environmental gates remain pending separately. Phase 6 completion requires actual reference-versus-implementation captures/overlays, tests and review; no generated baseline is automatic approval.
